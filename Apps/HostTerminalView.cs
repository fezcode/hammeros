using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Clockt.Terminal;
using Clockt.Terminal.Input;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class HostTerminalView : UserControl, IDisposable
{
    private HostTerminalSession? _session;
    private readonly TerminalSurface _surface = new();
    private readonly TextBlock _status = Ui.Label("READY  /  START POWERSHELL", Ui.Muted);
    private readonly TextBlock _message = Ui.Text("", 12, Ui.Phosphor, true);
    private bool _starting, _disposed;
    private readonly string? _directory;
    private readonly string _shell;
    private string? _initialCommand;
    public HostTerminalSession? Session => _session;
    public HostTerminalView(string? directory = null, string shell = "powershell", string? initialCommand = null)
    {
        _directory = directory; _shell = shell; _initialCommand = initialCommand;
        Background = Ui.Dark;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,30") };
        root.Children.Add(new Panel { Children = { _surface, _message } }); _message.Margin = new Thickness(18); _message.VerticalAlignment = VerticalAlignment.Top; _message.IsHitTestVisible = false;
        var copy = Ui.Button("Copy", () => _ = _surface.Copy(), "dark"); copy.FontSize = 10; copy.Padding = new Thickness(8, 4);
        var paste = Ui.Button("Paste", () => _ = _surface.Paste(), "dark"); paste.FontSize = 10; paste.Padding = new Thickness(8, 4);
        var restart = Ui.Button("Restart", () => Restart(), "dark"); restart.FontSize = 10; restart.Padding = new Thickness(8, 4);
        var footer = new Border { Background = Tint.Bar, Padding = new Thickness(14, 0), Child = Ui.Columns("*,Auto", _status, Ui.Row(0, copy, paste, restart)) }; Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
    }
    public async void Start()
    {
        if (_starting || _disposed || _session is not null) return;
        _starting = true; _message.Text = "Connecting to your workstation…";
        try
        {
            var session = await Task.Run(() => new HostTerminalSession(workingDirectory: _directory, shell: _shell, initialCommand: _initialCommand));
            _initialCommand = null;
            if (_disposed) { session.Dispose(); return; }
            _session = session; _surface.Attach(session); _message.Text = ""; _status.Text = $"{session.ShellName.ToUpperInvariant()}  /  PID {session.ProcessId}"; _surface.Focus();
            _ = ObserveExit(session);
        }
        catch (Exception ex) { _message.Text = "PowerShell could not start.\n" + ex.Message; _status.Text = "CONNECTION FAILED  /  RESTART TO RETRY"; }
        finally { _starting = false; }
    }
    private async Task ObserveExit(HostTerminalSession session)
    {
        var code = await session.Completion;
        if (!_disposed && _session == session) _status.Text = $"SESSION ENDED  /  EXIT {code}";
    }
    private void Restart() { if (_starting) return; _surface.Detach(); _session?.Dispose(); _session = null; Start(); }
    public void Dispose() { _disposed = true; _surface.Dispose(); _session?.Dispose(); }
}

/// <summary>Native cell renderer; only changed terminal frames are redrawn. No HTML surface.</summary>
public sealed class TerminalSurface : Control, IDisposable
{
    private HostTerminalSession? _session;
    public HostTerminalSession? Session => _session;
    private readonly InputEncoder _encoder = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private ulong _version, _generation;
    private int _scroll, _blink;
    private (int Row, int Col)? _selectionStart, _selectionEnd;
    private bool _dragging;
    private readonly Dictionary<(string, CellStyle), FormattedText> _textCache = [];
    private const double FontSize = 13, CellWidth = 7.8, CellHeight = 20, Inset = 17;
    private static readonly string[] Palette = ["#102B31", "#CF8074", "#9AC497", "#DBBE85", "#80AEB9", "#BFA3C4", "#9AD6D1", "#D5DDC9", "#6B8582", "#EDA394", "#BDE2AF", "#F1D8A2", "#B0CDD7", "#DDC1DF", "#C0F0E9", "#F2F3E5"];

    public TerminalSurface()
    {
        Focusable = true; ClipToBounds = true; Cursor = new Avalonia.Input.Cursor(StandardCursorType.Ibeam);
        var menu = new ContextMenu { MinWidth = 230 };
        var copy = new MenuItem { Header = "Copy selection or screen", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift) }; copy.Click += async (_, _) => await Copy();
        var paste = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Shift) }; paste.Click += async (_, _) => await Paste();
        menu.Items.Add(copy); menu.Items.Add(paste); menu.Opened += (_, _) => { copy.IsEnabled = _session is not null; paste.IsEnabled = _session is { HasExited: false }; }; ContextMenu = menu;
        _timer.Tick += (_, _) =>
        {
            if (_session is null || !IsEffectivelyVisible) return;
            var model = _session.Emulator; var blink = (int)(Environment.TickCount64 / 600 % 2);
            lock (model.SyncRoot)
            {
                if (model.IsRenderDeferred) return;
                if (model.HasChangedSince(_generation, _version) || blink != _blink) { _generation = model.Generation; _version = model.Version; _blink = blink; InvalidateVisual(); }
            }
        }; _timer.Start();
        _resize.Tick += (_, _) => { _resize.Stop(); ResizeSession(); };
        SizeChanged += (_, _) => { _resize.Stop(); _resize.Start(); };
        GotFocus += (_, _) => InvalidateVisual(); LostFocus += (_, _) => InvalidateVisual();
        TextInput += (_, e) => { if (e.Text is not null) { Send(Encoding.UTF8.GetBytes(e.Text)); e.Handled = true; } };
        KeyDown += OnKey;
        PointerPressed += (_, e) => { Focus(); if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _selectionStart = _selectionEnd = CellAt(e.GetPosition(this)); _dragging = true; e.Pointer.Capture(this); InvalidateVisual(); } };
        PointerMoved += (_, e) => { if (_dragging) { _selectionEnd = CellAt(e.GetPosition(this)); InvalidateVisual(); } };
        PointerReleased += (_, e) => { _dragging = false; e.Pointer.Capture(null); };
        PointerWheelChanged += (_, e) => { if (_session is not null) { lock (_session.Emulator.SyncRoot) _scroll = Math.Clamp(_scroll + (int)(e.Delta.Y * 3), 0, _session.Emulator.Scrollback.Count); InvalidateVisual(); e.Handled = true; } };
    }
    public void Attach(HostTerminalSession session) { _session = session; _scroll = 0; _version = ulong.MaxValue; _textCache.Clear(); ResizeSession(); InvalidateVisual(); }
    public void Detach() { _session = null; _selectionStart = _selectionEnd = null; InvalidateVisual(); }
    private void ResizeSession() { if (Bounds.Width > 20 && Bounds.Height > 20) _session?.Resize((int)((Bounds.Width - Inset * 2) / CellWidth), (int)((Bounds.Height - 20) / CellHeight)); }
    private void Send(byte[] data) { _scroll = 0; _selectionStart = _selectionEnd = null; _session?.Send(data); InvalidateVisual(); }
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_session is null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { if (e.Key == Key.C) { _ = Copy(); e.Handled = true; return; } if (e.Key == Key.V) { _ = Paste(); e.Handled = true; return; } }
        if (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { _ = Paste(); e.Handled = true; return; }
        var key = e.Key switch { Key.Return => TermKey.Enter, Key.Tab => TermKey.Tab, Key.Back => TermKey.Backspace, Key.Escape => TermKey.Escape, Key.Delete => TermKey.Delete, Key.Insert => TermKey.Insert, Key.Home => TermKey.Home, Key.End => TermKey.End, Key.PageUp => TermKey.PageUp, Key.PageDown => TermKey.PageDown, Key.Up => TermKey.Up, Key.Down => TermKey.Down, Key.Left => TermKey.Left, Key.Right => TermKey.Right, >= Key.F1 and <= Key.F12 => (TermKey)((int)TermKey.F1 + (e.Key - Key.F1)), _ => TermKey.None };
        var mods = TermModifiers.None; if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) mods |= TermModifiers.Ctrl; if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) mods |= TermModifiers.Alt; if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) mods |= TermModifiers.Shift;
        lock (_session.Emulator.SyncRoot) { _encoder.CursorKeysApp = _session.Emulator.Modes.CursorKeysApp; _encoder.NewLineMode = _session.Emulator.Modes.NewLine; }
        if (key != TermKey.None) { Send(_encoder.Encode(KeyInput.Special(key, mods))); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is >= Key.A and <= Key.Z) { Send(_encoder.Encode(KeyInput.Text(new Rune('a' + e.Key - Key.A), mods))); e.Handled = true; }
    }
    private (int Row, int Col) CellAt(Point point)
    {
        if (_session is null) return (0, 0);
        var model = _session.Emulator;
        lock (model.SyncRoot) return (Math.Clamp((int)((point.Y - 10) / CellHeight), 0, model.Rows - 1) + model.Scrollback.Count - _scroll, Math.Clamp((int)((point.X - Inset) / CellWidth), 0, model.Cols - 1));
    }
    public async Task Copy()
    {
        if (_session is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string text;
        var model = _session.Emulator;
        lock (model.SyncRoot)
        {
            if (_selectionStart is { } a && _selectionEnd is { } b && a != b)
            {
                if (a.CompareTo(b) > 0) (a, b) = (b, a);
                var result = new StringBuilder();
                for (var row = a.Row; row <= b.Row; row++)
                {
                    var line = GetLine(model, row); if (line is null) continue;
                    var from = row == a.Row ? a.Col : 0; var to = row == b.Row ? b.Col : line.Length - 1;
                    for (var col = from; col <= to && col < line.Length; col++) { var cell = line.Cells[col]; if (!cell.IsWideContinuation) result.Append(CellText(model, cell, row < model.Scrollback.Count)); }
                    if (row < b.Row && !line.Wrapped) result.AppendLine();
                }
                text = result.ToString();
            }
            else text = model.DumpScreen();
        }
        try { await clipboard.SetTextAsync(text); } catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
    }
    public async Task Paste()
    {
        if (_session is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { var text = await clipboard.GetTextAsync(); if (text is not null && _session is { } session) { bool bracketed; lock (session.Emulator.SyncRoot) bracketed = session.Emulator.Modes.BracketedPaste; Send(InputEncoder.EncodePaste(text, bracketed)); } }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
    }
    private static Line? GetLine(TerminalEmulator model, int row) => row < 0 ? null : row < model.Scrollback.Count ? model.Scrollback[row] : row - model.Scrollback.Count < model.Rows ? model.Screen[row - model.Scrollback.Count] : null;
    private static string CellText(TerminalEmulator model, Cell cell, bool history) => cell.IsGrapheme ? (history ? model.Primary.Graphemes : model.Screen.Graphemes).Get(cell.GraphemeIndex) : cell.Rune.ToString();
    private static IBrush Color(TermColor value, bool background)
    {
        if (value.Kind == TermColorKind.Default) return background ? Ui.Dark : Ui.Phosphor;
        if (value.Kind == TermColorKind.Rgb) return new SolidColorBrush(Avalonia.Media.Color.FromRgb(value.R, value.G, value.B));
        if (value.Index < 16) return Brush.Parse(Palette[value.Index]);
        if (value.Index >= 232) { var gray = (byte)(8 + (value.Index - 232) * 10); return new SolidColorBrush(Avalonia.Media.Color.FromRgb(gray, gray, gray)); }
        var n = value.Index - 16; byte C(int x) => x == 0 ? (byte)0 : (byte)(55 + x * 40);
        return new SolidColorBrush(Avalonia.Media.Color.FromRgb(C(n / 36), C(n / 6 % 6), C(n % 6)));
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Ui.Dark, Bounds); if (_session is null) return;
        var model = _session.Emulator;
        lock (model.SyncRoot)
        {
            _scroll = Math.Min(_scroll, model.Scrollback.Count);
            for (var row = 0; row < model.Rows; row++)
            {
                var absolute = model.Scrollback.Count - _scroll + row; var line = GetLine(model, absolute); if (line is null) continue;
                for (var col = 0; col < Math.Min(line.Length, model.Cols); col++)
                {
                    var cell = line.Cells[col]; if (cell.IsWideContinuation) continue;
                    var style = cell.Style; var fg = Color(style.Fg, false); var bg = Color(style.Bg, true); if (style.Has(StyleFlags.Inverse)) (fg, bg) = (bg, fg);
                    var rect = new Rect(Inset + col * CellWidth, 10 + row * CellHeight, cell.IsWide ? CellWidth * 2 : CellWidth, CellHeight);
                    if (!style.Bg.IsDefault || style.Has(StyleFlags.Inverse)) context.FillRectangle(bg, rect);
                    if (_selectionStart is { } a && _selectionEnd is { } b && a != b)
                    { if (a.CompareTo(b) > 0) (a, b) = (b, a); if ((absolute, col).CompareTo(a) >= 0 && (absolute, col).CompareTo(b) <= 0) context.FillRectangle(Brush.Parse("#5A78977F"), rect); }
                    if (cell.IsBlank || style.Has(StyleFlags.Hidden)) continue;
                    var text = CellText(model, cell, absolute < model.Scrollback.Count);
                    if (!_textCache.TryGetValue((text, style), out var formatted))
                    {
                        if (_textCache.Count > 4096) _textCache.Clear();
                        formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Mono, style.Has(StyleFlags.Italic) ? FontStyle.Italic : FontStyle.Normal, style.Has(StyleFlags.Bold) ? FontWeight.Bold : FontWeight.Normal), FontSize, fg); _textCache[(text, style)] = formatted;
                    }
                    using (context.PushOpacity(style.Has(StyleFlags.Dim) ? .6 : 1)) context.DrawText(formatted, new Point(rect.X, rect.Y + 1));
                    if (style.UnderlineStyle != UnderlineStyle.None) context.DrawLine(new Pen(fg, 1), new Point(rect.X, rect.Bottom - 3), new Point(rect.Right, rect.Bottom - 3));
                }
            }
            if (_scroll == 0 && model.Modes.CursorVisible && IsFocused && _blink == 0 && !_session.HasExited)
                context.DrawRectangle(Brush.Parse("#709DCCBC"), null, new Rect(Inset + model.Cursor.Col * CellWidth, 10 + model.Cursor.Row * CellHeight, CellWidth, CellHeight));
            if (_scroll > 0) context.DrawText(new FormattedText($"↑ {_scroll} lines", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Mono), 10, Ui.Phosphor), new Point(Math.Max(0, Bounds.Width - 115), 3));
        }
    }
    public void Dispose() { _timer.Stop(); _resize.Stop(); Detach(); }
}
