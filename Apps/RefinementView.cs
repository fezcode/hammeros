using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class RefinementView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly NumberField _numbers;
    private readonly TextBlock _status = Ui.Text("Select the numbers that speak to you.", 11, Ui.Phosphor, true);
    private readonly TextBlock _quota = Ui.Text("", 15, Ui.Phosphor, true);
    public RefinementView(SystemState state)
    {
        _state = state; Background = Ui.Dark;
        var root = new Grid { RowDefinitions = new RowDefinitions("88,*,36,111,30") };
        var fileTitle = new TextBlock { Text = "Siena", FontSize = 34, FontFamily = Ui.Serif, Foreground = Ui.Phosphor };
        var heading = Ui.Stack(6, Ui.Label("ACTIVE FILE  /  0X 4A 92", Brush.Parse("#749D97")), fileTitle);
        var right = Ui.Stack(6, Ui.Label("QUARTERLY QUOTA", Brush.Parse("#749D97")), _quota); right.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new Border { Padding = new Thickness(27, 16), BorderBrush = Brush.Parse("#35565A"), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Columns("*,Auto", heading, right) }; root.Children.Add(header);
        _numbers = new NumberField(state); Grid.SetRow(_numbers, 1); root.Children.Add(_numbers);
        _numbers.SelectionChanged += () => _status.Text = _numbers.Selected.Count == 0 ? "Select the numbers that speak to you." : $"{_numbers.Selected.Count:00} numbers selected. Choose a bin below.";
        var hint = new Border { Padding = new Thickness(27, 0), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush.Parse("#35565A"), Child = Ui.Columns("*,Auto", _status, Ui.Label("CLICK TO SELECT · ESC TO CLEAR", Brush.Parse("#749D97"))) }; Grid.SetRow(hint, 2); root.Children.Add(hint);
        var binGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), Margin = new Thickness(27, 15, 27, 12) };
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var bin = Ui.Button("", () => Assign(index), "dark"); bin.Padding = new Thickness(12, 9); bin.BorderBrush = Brush.Parse("#537B78"); bin.BorderThickness = new Thickness(1); bin.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 4 ? 0 : 6, 0); bin.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            bin.Content = Ui.Stack(9); Grid.SetColumn(bin, i); binGrid.Children.Add(bin);
        }
        Grid.SetRow(binGrid, 3); root.Children.Add(binGrid);
        var footer = new Border { Background = Brush.Parse("#193A40"), Padding = new Thickness(27, 0), Child = Ui.Columns("*,Auto", Ui.Label("MDR  /  REFINEMENT PROTOCOL 2.1", Brush.Parse("#88A9A2")), Ui.Label("PLEASE ENJOY EACH NUMBER EQUALLY.", Brush.Parse("#88A9A2"))) }; Grid.SetRow(footer, 4); root.Children.Add(footer);
        _binGrid = binGrid; Content = root; state.Changed += Update; Update();
    }
    private readonly Grid _binGrid;
    private void Update()
    {
        _quota.Text = $"{_state.Preferences.Bins.Average():0}%  COMPLETE";
        for (var i = 0; i < 5; i++)
        {
            var bin = (Button)_binGrid.Children[i]; var stack = (StackPanel)bin.Content!; stack.Children.Clear();
            var percent = _state.Preferences.Bins[i];
            stack.Children.Add(Ui.Columns("*,Auto", Ui.Text($"0{i + 1}", 20, Ui.Phosphor, true), Ui.Text($"{percent}%", 11, Ui.Phosphor, true)));
            var track = new Grid { Height = 4, Background = Brush.Parse("#345457"), ColumnDefinitions = new ColumnDefinitions($"{Math.Max(.001, percent)}*,{Math.Max(.001, 100 - percent)}*") };
            track.Children.Add(new Border { Background = Ui.Phosphor }); stack.Children.Add(track);
            stack.Children.Add(Ui.Label("WO  FC  DR  MA", Brush.Parse("#719B95")));
        }
        if (!_state.Services["Refinement engine"]) _status.Text = "Refinement engine stopped. Resume it in Control Panel.";
    }
    public void Assign(int bin)
    {
        if (!_state.Services["Refinement engine"]) { _status.Text = "Refinement engine stopped. Resume it in Control Panel."; return; }
        if (_numbers.Selected.Count == 0) { _status.Text = "First select a group of numbers above."; return; }
        if (_state.Preferences.Bins[bin] >= 100) { _status.Text = "This bin is complete. Please choose another."; return; }
        var count = _numbers.Selected.Count;
        _state.Preferences.Bins[bin] = Math.Min(100, _state.Preferences.Bins[bin] + count);
        _numbers.Collect(); _state.Record($"Refined {count} numbers into bin 0{bin + 1}"); _state.Save();
        _status.Text = _state.Preferences.Bins.All(x => x == 100) ? "Siena complete. Management appreciates your contribution." : $"Numbers accepted into bin 0{bin + 1}. The work continues.";
        Motion.Enter((Control)_binGrid.Children[bin], 4);
    }
    public void Dispose() { _numbers.Dispose(); _state.Changed -= Update; }
}

public sealed class NumberField : Control, IDisposable
{
    private readonly SystemState _state;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly int[] _digits;
    private readonly FormattedText[,] _textCache = new FormattedText[4, 10];
    private readonly HashSet<int> _collecting = [];
    private double _time, _collection;
    private int _hover = -1;
    public HashSet<int> Selected { get; } = [];
    public event Action? SelectionChanged;
    public NumberField(SystemState state)
    {
        _state = state; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        var random = new Random(417); _digits = Enumerable.Range(0, 216).Select(_ => random.Next(10)).ToArray();
        for (var style = 0; style < 4; style++) for (var digit = 0; digit < 10; digit++)
            _textCache[style, digit] = new FormattedText(digit.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Mono), style == 3 ? 19 : 15, Brush.Parse(style >= 2 ? "#DCFFF0" : style == 1 ? "#6CABA8" : "#97D1CA"));
        _timer.Tick += (_, _) => { if (_state.Preferences.Motion && IsEffectivelyVisible) { _time += .016; InvalidateVisual(); } }; _timer.Start();
        PointerMoved += (_, e) => { _hover = Hit(e.GetPosition(this)); InvalidateVisual(); };
        PointerExited += (_, _) => { _hover = -1; InvalidateVisual(); };
        PointerPressed += (_, e) => { Focus(); SelectAt(Hit(e.GetPosition(this))); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Selected.Clear(); SelectionChanged?.Invoke(); InvalidateVisual(); e.Handled = true; }
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { _hover = Math.Clamp((_hover < 0 ? 0 : _hover) + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : e.Key == Key.Up ? -24 : 24), 0, 215); InvalidateVisual(); e.Handled = true; }
            if (e.Key is Key.Enter or Key.Space) { SelectAt(_hover < 0 ? 0 : _hover); e.Handled = true; }
        };
    }
    public void SelectAt(int index)
    {
        if (index < 0 || _collecting.Count > 0) return;
        var remove = Selected.Contains(index);
        for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
        { var x = index % 24 + dx; var y = index / 24 + dy; if (x < 0 || x >= 24 || y < 0 || y >= 9) continue; if (remove) Selected.Remove(y * 24 + x); else Selected.Add(y * 24 + x); }
        SelectionChanged?.Invoke(); InvalidateVisual();
    }
    private int Hit(Point p)
    {
        var x = (int)((p.X - 18) / Math.Max(1, (Bounds.Width - 36) / 24)); var y = (int)((p.Y - 13) / Math.Max(1, (Bounds.Height - 26) / 9));
        return p.X < 18 || p.Y < 13 || x < 0 || x >= 24 || y < 0 || y >= 9 ? -1 : y * 24 + x;
    }
    public void Collect()
    {
        _collecting.UnionWith(Selected); Selected.Clear(); SelectionChanged?.Invoke();
        Motion.Animate(430, t => { _collection = t; InvalidateVisual(); }, () => { foreach (var i in _collecting) _digits[i] = (_digits[i] + 7) % 10; _collecting.Clear(); _collection = 0; InvalidateVisual(); });
    }
    public override void Render(DrawingContext c)
    {
        c.FillRectangle(Ui.Dark, Bounds);
        var cellW = (Bounds.Width - 36) / 24; var cellH = (Bounds.Height - 26) / 9;
        for (var i = 0; i < _digits.Length; i++)
        {
            var x = 18 + (i % 24) * cellW; var y = 13 + (i / 24) * cellH;
            var drift = _state.Preferences.Motion ? Math.Sin(_time * .7 + i * 1.9) * 2.0 : 0;
            var selected = Selected.Contains(i); var hover = _hover >= 0 && Math.Abs(i % 24 - _hover % 24) <= 1 && Math.Abs(i / 24 - _hover / 24) <= 1;
            if (selected || hover) c.DrawRectangle(Brush.Parse(selected ? "#315D5F" : "#1B4247"), selected ? new Pen(Brush.Parse("#668E84"), .5) : null, new Rect(x, y, cellW, cellH));
            var text = _textCache[hover ? 3 : selected ? 2 : i % 7 == 0 ? 1 : 0, _digits[i]];
            using (c.PushOpacity(_collecting.Contains(i) ? 1 - _collection : 1))
                c.DrawText(text, new Point(x + (cellW - text.Width) / 2, y + (cellH - text.Height) / 2 + drift + (_collecting.Contains(i) ? 50 * _collection : 0)));
        }
        if (_state.Preferences.Scanlines) for (var y = 0; y < Bounds.Height; y += 4) c.DrawLine(new Pen(Brush.Parse("#11000000"), 1), new Point(0, y), new Point(Bounds.Width, y));
    }
    public void Dispose() => _timer.Stop();
}
