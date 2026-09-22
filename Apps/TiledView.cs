using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class HostedWindowControl(WindowAttachment attachment, Action<string> error) : NativeControlHost
{
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private EventHandler? _tick;
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        try { attachment.Attach(handle.Handle); _tick ??= (_, _) => attachment.Resize(); _resize.Tick += _tick; _resize.Start(); } catch (Exception ex) { Dispatcher.UIThread.Post(() => error(ex.Message)); }
        return handle;
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control) { _resize.Stop(); if (_tick is not null) _resize.Tick -= _tick; attachment.Detach(); base.DestroyNativeControlCore(control); }
    protected override Size ArrangeOverride(Size finalSize) { var size = base.ArrangeOverride(finalSize); Dispatcher.UIThread.Post(attachment.Resize, DispatcherPriority.Render); return size; }
}

public sealed class TiledView : UserControl, INativeWorkspace, IDisposable
{
    private sealed record Tile(WindowAttachment Attachment, Border Frame, HostedWindowControl Host, TextBlock Title, Grid Panel);
    private readonly List<Tile> _tiles = [];
    private readonly Grid _grid = new() { Margin = new Thickness(16) };
    private readonly Panel _layers = new();
    private readonly Grid _root = new() { RowDefinitions = new RowDefinitions("59,0,*,34") };
    private readonly StackPanel _tabs = Ui.Row(3);
    private readonly Border _focusBar;
    private readonly ComboBox _layoutSelector;
    private readonly TextBlock _status = Ui.Text("No applications attached", 11, Ui.Muted);
    private readonly Action<string> _toast;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private Border? _dialog;
    private bool _active = true, _disposed;
    private string _layout = "Grid";
    private int _focused;
    private string _previousLayout = "Grid";
    private readonly Dictionary<string, (double[] Columns, double[] Rows)> _layoutSizes = new();
    private bool _dragging;
    public string LayoutMode => _layout;
    public int FocusedIndex => _focused;
    public int TileCount => _tiles.Count;
    public IReadOnlyList<WindowAttachment> Attachments => _tiles.Select(x => x.Attachment).ToArray();
    public TiledView(Action<string> toast)
    {
        _toast = toast;
        var root = _root;
        var title = Ui.Stack(3, Ui.Text("tiled", 23), Ui.Label("YOUR WINDOWS. ONE WORKSPACE.")); title.VerticalAlignment = VerticalAlignment.Center;
        var layout = new ComboBox { Name = "TiledLayout", ItemsSource = new[] { "Grid", "Columns", "Rows", "Focus" }, SelectedIndex = 0, Width = 112, Height = 36, MinHeight = 0, Padding = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        _layoutSelector = layout; layout.SelectionChanged += (_, _) => SetLayout(layout.SelectedItem as string ?? "Grid");
        var actions = Ui.Row(8, layout, Ui.Button("Attach window", ChooseWindow, "outline"), Ui.Button("Launch app", LaunchDialog, "primary")); actions.VerticalAlignment = VerticalAlignment.Center;
        foreach (var button in actions.Children.OfType<Button>()) { button.Height = 36; button.MinHeight = 0; button.Padding = new Thickness(14, 0); button.VerticalAlignment = VerticalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center; }
        var header = Ui.Columns("*,Auto", title, actions); header.Margin = new Thickness(20, 0); root.Children.Add(header);
        _focusBar = new Border { Name = "TiledFocusBar", Background = Brush.Parse("#DBE3D2"), Padding = new Thickness(8, 4), Child = Ui.Columns("*,Auto", new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, Ui.Button("Exit focus", () => SetLayout(_previousLayout), "outline")), IsVisible = false }; Grid.SetRow(_focusBar, 1); root.Children.Add(_focusBar);
        _layers.Children.Add(_grid); Grid.SetRow(_layers, 2); root.Children.Add(_layers);
        var footer = new Border { Padding = new Thickness(18, 0), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = Ui.Columns("*,Auto", _status, Ui.Button("Detach all", DetachAll)) }; Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        _poll.Tick += (_, _) => { var changed = false; foreach (var tile in _tiles.ToArray()) { if (!NativeWindows.BelongsTo(tile.Attachment.Window)) Remove(tile); else { var text = NativeWindows.Title(tile.Attachment.Window.Handle); if (!string.IsNullOrWhiteSpace(text) && text != tile.Title.Text) { tile.Title.Text = text; changed = true; } } } if (changed) RenderTabs(); }; _poll.Start(); ArrangeTiles();
    }
    public void SetWorkspaceActive(bool active) { _active = active; UpdateNativeVisibility(); }
    private void UpdateNativeVisibility()
    { foreach (var tile in _tiles) tile.Host.IsVisible = _active && !_dragging && _dialog is null && tile.Frame.IsVisible; }
    public void SetLayout(string layout)
    {
        if (layout is not ("Grid" or "Columns" or "Rows" or "Focus") || layout == _layout) return;
        if (_layout != "Focus") { _previousLayout = _layout; _layoutSizes[_layout] = (_grid.ColumnDefinitions.Where((_, i) => i % 2 == 0).Select(x => x.Width.Value).ToArray(), _grid.RowDefinitions.Where((_, i) => i % 2 == 0).Select(x => x.Height.Value).ToArray()); }
        _layout = layout; if (!Equals(_layoutSelector.SelectedItem, layout)) _layoutSelector.SelectedItem = layout; ArrangeTiles();
        if (_layoutSizes.TryGetValue(layout, out var sizes)) { if (sizes.Columns.Length * 2 - 1 == _grid.ColumnDefinitions.Count) for (var i = 0; i < sizes.Columns.Length; i++) _grid.ColumnDefinitions[i * 2].Width = new GridLength(Math.Max(.01, sizes.Columns[i]), GridUnitType.Star); if (sizes.Rows.Length * 2 - 1 == _grid.RowDefinitions.Count) for (var i = 0; i < sizes.Rows.Length; i++) _grid.RowDefinitions[i * 2].Height = new GridLength(Math.Max(.01, sizes.Rows[i]), GridUnitType.Star); }
    }
    public void FocusTile(int index) { if (index < 0 || index >= _tiles.Count) return; _focused = index; ArrangeTiles(); }
    public void MoveTile(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _tiles.Count || to >= _tiles.Count) return;
        var selected = _tiles.ElementAtOrDefault(_focused); var tile = _tiles[from]; _tiles.RemoveAt(from); _tiles.Insert(to, tile);
        _focused = selected is null ? 0 : _tiles.IndexOf(selected); ArrangeTiles();
    }
    private void RenderTabs()
    {
        _tabs.Children.Clear();
        foreach (var tile in _tiles)
        {
            var label = Ui.Text(tile.Title.Text ?? tile.Attachment.Window.Application, 12); label.MaxWidth = 200; label.TextTrimming = TextTrimming.CharacterEllipsis;
            var button = Ui.Button("", () => FocusTile(_tiles.IndexOf(tile))); button.Name = "TiledFocusTab"; button.Content = label; button.Height = 32; button.Padding = new Thickness(12, 0); button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Background = _tiles.IndexOf(tile) == _focused ? Ui.Cream : Brushes.Transparent; button.BorderThickness = new Thickness(0, 0, 0, 2); button.BorderBrush = _tiles.IndexOf(tile) == _focused ? Ui.Ink : Brushes.Transparent; ToolTip.SetTip(button, tile.Title.Text); _tabs.Children.Add(button);
        }
    }
    public void Attach(DesktopWindow window)
    {
        if (_tiles.Any(x => x.Attachment.Window.Handle == window.Handle)) { _toast("This window is already attached."); return; }
        try
        {
            var attachment = new WindowAttachment(window); var title = Ui.Text(window.Title, 12); title.TextTrimming = TextTrimming.CharacterEllipsis;
            Tile? tile = null;
            var host = new HostedWindowControl(attachment, message => { _toast(message); if (tile is not null) Remove(tile); });
            var panel = new Grid { RowDefinitions = new RowDefinitions("37,*") };
            var controls = Ui.Row(0, Ui.Button("↗", () => { if (tile is not null) Remove(tile); }), Ui.Button("×", () => { attachment.Detach(); NativeWindows.RequestClose(window.Handle); if (tile is not null) Remove(tile); }));
            ToolTip.SetTip(controls.Children[0], "Detach and restore original window"); ToolTip.SetTip(controls.Children[1], "Ask this application to close");
            var bar = new Border { Background = Brush.Parse("#DBE3D2"), Padding = new Thickness(12, 0, 0, 0), Child = Ui.Columns("*,Auto", title, controls) }; panel.Children.Add(bar);
            var backdrop = new Border { Background = Ui.Dark, Child = Ui.Label(window.Application.ToUpperInvariant(), Ui.Phosphor), Padding = new Thickness(20) }; Grid.SetRow(backdrop, 1); panel.Children.Add(backdrop);
            Grid.SetRow(host, 1); panel.Children.Add(host);
            var frame = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Margin = new Thickness(5), Child = panel, ClipToBounds = true };
            tile = new(attachment, frame, host, title, panel); ConnectMove(bar, tile); _tiles.Add(tile); _focused = _tiles.Count - 1; CloseDialog(); ArrangeTiles();
        }
        catch (Exception ex) { _toast(ex.Message); }
    }
    private void Remove(Tile tile) { tile.Attachment.Detach(); _tiles.Remove(tile); _grid.Children.Remove(tile.Frame); ArrangeTiles(); }
    public void DetachAll() { foreach (var tile in _tiles.ToArray()) { tile.Attachment.Detach(); _grid.Children.Remove(tile.Frame); } _tiles.Clear(); ArrangeTiles(); }
    private void ArrangeTiles()
    {
        var focus = _layout == "Focus" && _tiles.Count > 0;
        _root.RowDefinitions[0].Height = new GridLength(focus ? 0 : 59); _root.Children[0].IsVisible = !focus;
        _root.RowDefinitions[1].Height = new GridLength(focus ? 42 : 0); _focusBar.IsVisible = focus;
        _root.RowDefinitions[3].Height = new GridLength(focus ? 0 : 34); _root.Children[3].IsVisible = !focus;
        _grid.Margin = new Thickness(focus ? 0 : 12);
        RenderTabs();
        if (_tiles.Count == 0)
        {
            _grid.Children.Clear(); _grid.RowDefinitions = new RowDefinitions("*"); _grid.ColumnDefinitions = new ColumnDefinitions("*");
            var empty = Ui.Stack(21, new Glyph("refinement") { Width = 48, Height = 48 }, Ui.Heading("NATIVE WINDOWS WORKSPACE", "Everything, in its place.", "Launch a Windows application or bring an existing window into HammerOS."), Ui.Row(10, Ui.Button("Launch an application", LaunchDialog, "primary"), Ui.Button("Attach an open window", ChooseWindow, "outline")), Ui.Text("Detach returns apps to their original desktop position.\nClosing tiled detaches your apps; it does not end their processes.", 12, Ui.Muted));
            empty.Margin = new Thickness(36); empty.VerticalAlignment = VerticalAlignment.Center; _grid.Children.Add(empty); _status.Text = "No applications attached"; return;
        }
        foreach (var item in _grid.Children.Where(x => !_tiles.Any(t => t.Frame == x)).ToArray()) _grid.Children.Remove(item);
        var columns = _layout == "Rows" || _layout == "Focus" ? 1 : _layout == "Columns" ? _tiles.Count : (int)Math.Ceiling(Math.Sqrt(_tiles.Count));
        var rows = _layout == "Focus" ? 1 : (int)Math.Ceiling(_tiles.Count / (double)columns);
        _focused = Math.Clamp(_focused, 0, _tiles.Count - 1);
        if (_grid.ColumnDefinitions.Count != columns * 2 - 1) _grid.ColumnDefinitions = new ColumnDefinitions(string.Join(",6,", Enumerable.Repeat("*", columns)));
        if (_grid.RowDefinitions.Count != rows * 2 - 1) _grid.RowDefinitions = new RowDefinitions(string.Join(",6,", Enumerable.Repeat("*", rows)));
        for (var col = 1; col < columns * 2 - 1; col += 2) { var split = new GridSplitter { Name = "TiledColumnDivider", Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brush.Parse("#B9C9BA"), ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext }; Grid.SetColumn(split, col); Grid.SetRowSpan(split, rows * 2 - 1); _grid.Children.Add(split); ToolTip.SetTip(split, "Drag to resize columns"); }
        for (var row = 1; row < rows * 2 - 1; row += 2) { var split = new GridSplitter { Name = "TiledRowDivider", Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brush.Parse("#B9C9BA"), ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext }; Grid.SetRow(split, row); Grid.SetColumnSpan(split, columns * 2 - 1); _grid.Children.Add(split); ToolTip.SetTip(split, "Drag to resize rows"); }
        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i]; if (!_grid.Children.Contains(tile.Frame)) _grid.Children.Add(tile.Frame);
            tile.Frame.IsVisible = _layout != "Focus" || i == _focused;
            tile.Panel.RowDefinitions[0].Height = new GridLength(focus ? 0 : 37); tile.Panel.Children[0].IsVisible = !focus; tile.Frame.Margin = new Thickness(focus ? 0 : 2);
            Grid.SetColumn(tile.Frame, _layout == "Focus" ? 0 : (i % columns) * 2); Grid.SetRow(tile.Frame, _layout == "Focus" ? 0 : (i / columns) * 2);
        }
        _status.Text = $"{_tiles.Count} windows  ·  Drag titles to move · Drag dividers to resize"; UpdateNativeVisibility();
    }
    private void ConnectMove(Border title, Tile tile)
    {
        title.Cursor = new Cursor(StandardCursorType.SizeAll); ToolTip.SetTip(title, "Drag this title onto another tile to change its position"); Point? start = null; Tile? target = null;
        title.PointerPressed += (_, e) => { if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any() || !e.GetCurrentPoint(title).Properties.IsLeftButtonPressed) return; start = e.GetPosition(_grid); target = null; e.Pointer.Capture(title); e.Handled = true; };
        title.PointerMoved += (_, e) => { if (start is not { } point) return; var now = e.GetPosition(_grid); if (Math.Abs(now.X - point.X) + Math.Abs(now.Y - point.Y) < 6 && !_dragging) return; _dragging = true; UpdateNativeVisibility(); target = _tiles.FirstOrDefault(x => x != tile && x.Frame.Bounds.Contains(now)); foreach (var item in _tiles) item.Frame.BorderBrush = item == target ? Ui.Ink : Ui.Line; _status.Text = target is null ? "Drop onto another tile to move this application" : "Release to move to " + target.Title.Text; };
        title.PointerReleased += (_, e) => { var moveTo = target; start = null; _dragging = false; e.Pointer.Capture(null); foreach (var item in _tiles) item.Frame.BorderBrush = Ui.Line; if (moveTo is not null) MoveTile(_tiles.IndexOf(tile), _tiles.IndexOf(moveTo)); else UpdateNativeVisibility(); e.Handled = true; };
        title.PointerCaptureLost += (_, _) => { start = null; _dragging = false; UpdateNativeVisibility(); };
    }
    public void ConfirmClose(Action close)
    {
        if (_tiles.Count == 0) { close(); return; }
        Dialog(Ui.Stack(22, Ui.Heading("CLOSE WORKSPACE", "Return these apps to Windows?", $"{_tiles.Count} attached applications will leave tiled and return to their original desktop positions. They will keep running."), Ui.Row(12, Ui.Button("Keep workspace open", CloseDialog, "outline"), Ui.Button("Close tiled", () => { CloseDialog(); close(); }, "primary"))));
    }
    private void Dialog(Control content)
    {
        CloseDialog(); _dialog = new Border { Background = Ui.Cream, Padding = new Thickness(27), Child = content }; _layers.Children.Add(_dialog); UpdateNativeVisibility(); Motion.Enter(_dialog, 7);
    }
    private void CloseDialog() { if (_dialog is not null) _layers.Children.Remove(_dialog); _dialog = null; UpdateNativeVisibility(); }
    private void ChooseWindow()
    {
        var search = new TextBox { Watermark = "Find an open application…" }; var list = Ui.Stack(5);
        void Populate()
        {
            list.Children.Clear();
            var windows = NativeWindows.Enumerate().Where(x => !_tiles.Any(t => t.Attachment.Window.Handle == x.Handle) && (x.Title + x.Application).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var window in windows)
            {
                var label = Ui.Text(window.Title, 13); label.TextTrimming = TextTrimming.CharacterEllipsis;
                var button = Ui.Button("", () => Attach(window), "outline"); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Content = Ui.Columns("*,90", Ui.Stack(5, label, Ui.Label(window.Application + " / " + window.ProcessId)), Ui.Text("Attach  →", 12)); list.Children.Add(button);
            }
            if (windows.Length == 0) list.Children.Add(Ui.Text("No matching windows. Launch an app or refresh this list.", 13, Ui.Muted));
        }
        search.TextChanged += (_, _) => Populate(); Populate();
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,52,*,46") }; grid.Children.Add(Ui.Heading("BRING YOUR WORK INSIDE", "Attach a window.", "Choose the exact Windows application to include in this workspace.")); Grid.SetRow(search, 1); search.Margin = new Thickness(0, 12, 0, 4); grid.Children.Add(search);
        var scroll = Ui.Scroll(list); Grid.SetRow(scroll, 2); grid.Children.Add(scroll); var actions = Ui.Row(10, Ui.Button("Cancel", CloseDialog, "outline"), Ui.Button("Refresh", Populate)); Grid.SetRow(actions, 3); grid.Children.Add(actions); Dialog(grid); search.Focus();
    }
    private void LaunchDialog()
    {
        var path = new TextBox { Watermark = "Application executable or command, e.g. notepad.exe" };
        var args = new TextBox { Watermark = "Arguments (optional)" }; var status = Ui.Text("", 12, Ui.Muted); status.TextWrapping = TextWrapping.Wrap;
        var browse = Ui.Button("Browse…", async () => { var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a Windows application", FileTypeFilter = [new("Windows applications") { Patterns = ["*.exe", "*.lnk"] }] }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } selected) path.Text = selected; }, "outline");
        var launch = Ui.Button("Launch and attach", () => { }, "primary");
        launch.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(path.Text)) { status.Text = "Choose an application first."; return; }
            launch.IsEnabled = false; status.Text = "Waiting for the application window…";
            try
            {
                using var process = Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(path.Text.Trim())) { Arguments = args.Text ?? "", UseShellExecute = true });
                if (process is null) { status.Text = "The app was launched by Windows. Use Attach window to select it."; return; }
                var clock = Stopwatch.StartNew();
                while (!_disposed && clock.Elapsed.TotalSeconds < 12)
                {
                    var match = NativeWindows.Enumerate().FirstOrDefault(x => x.ProcessId == process.Id);
                    if (match is not null) { Attach(match); return; } await Task.Delay(200);
                }
                status.Text = "The app started outside this workspace. Some launchers use a separate process; use Attach window to select the resulting window.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { launch.IsEnabled = true; }
        };
        Dialog(Ui.Scroll(Ui.Stack(21, Ui.Heading("NATIVE WINDOWS APPLICATIONS", "Open a line of work.", "Launch a desktop app and give it a place in your workspace."), Ui.Columns("*,Auto", path, browse), args, status, Ui.Row(12, Ui.Button("Cancel", CloseDialog, "outline"), launch), Ui.Rule(), Ui.Text("Desktop apps can be embedded when Windows permits it.\nElevated apps and some packaged apps may require running separately.", 12, Ui.Muted)))); path.Focus();
    }
    public void Dispose() { _disposed = true; _poll.Stop(); DetachAll(); }
}
