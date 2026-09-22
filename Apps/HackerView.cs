using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class HackerView : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly TextBlock _feed = Ui.Text("", 12, Ui.Phosphor, true), _metric = Ui.Text("", 36, Ui.Phosphor, true);
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 4 };
    private readonly Queue<string> _lines = new();
    private readonly SignalField _field = new();
    private readonly Button _pause;
    private bool _running = true;
    private int _tick;
    private readonly string[] _events = ["routing synthetic packet", "aligning signal phase", "decoding training frame", "rebuilding checksum", "mapping sandbox node", "handshake acknowledged", "sequence verified", "entropy stream stable"];
    public HackerView()
    {
        Background = Ui.Dark; Focusable = true; _feed.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var root = new Grid { RowDefinitions = new RowDefinitions("80,*,42") };
        _pause = Ui.Button("Pause", Toggle, "dark");
        root.Children.Add(new Border { Padding = new Thickness(24, 14), Child = Ui.Columns("*,Auto", Ui.Stack(5, Ui.Label("HAMMER SIGNAL LAB  /  CINEMATIC SIMULATION", Ui.Muted), Ui.Text("Ghost protocol.", 28, Ui.Phosphor)), Ui.Row(6, _pause, Ui.Button("Clear", () => _lines.Clear(), "dark"))) });
        var right = Ui.Stack(20, Ui.Label("SIGNAL RECONSTRUCTION", Ui.Muted), _metric, _progress, Ui.Rule(Brush.Parse("#35545A")), Ui.Label("TRAINING NODES", Ui.Muted), Ui.Text("192.0.2.14    ONLINE\n192.0.2.27    RELAY\n198.51.100.8  STANDBY", 12, Ui.Phosphor, true), _field);
        var body = Ui.Columns("*,220", new Border { Padding = new Thickness(24, 12), Child = _feed, ClipToBounds = true }, new Border { Padding = new Thickness(18), BorderBrush = Brush.Parse("#35545A"), BorderThickness = new Thickness(1, 0, 0, 0), Child = right }); Grid.SetRow(body, 1); root.Children.Add(body);
        var footer = Ui.Pad(Ui.Label("TYPE ANYTHING TO ACCELERATE   ·   SPACE TO PAUSE   ·   LOCAL SIMULATION", Ui.Muted), 14); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _timer.Tick += (_, _) => { if (_running && IsEffectivelyVisible) Tick(); }; _timer.Start();
        PointerPressed += (_, _) => Focus(); KeyDown += (_, e) => { if (e.Key == Key.Space) Toggle(); else { for (var i = 0; i < 3; i++) Tick(); } e.Handled = true; };
        for (var i = 0; i < 18; i++) Tick();
    }
    private void Toggle() { _running = !_running; _pause.Content = _running ? "Pause" : "Resume"; }
    private void Tick()
    {
        _tick++; var value = _tick % 101; _progress.Value = value; _metric.Text = $"{value:00}%";
        if (_tick % 2 == 0) { _lines.Enqueue($"{DateTime.Now:HH:mm:ss}  {Random.Shared.Next(65536):X4}  {_events[Random.Shared.Next(_events.Length)]}"); while (_lines.Count > 23) _lines.Dequeue(); _feed.Text = string.Join('\n', _lines); }
        _field.Phase += .13; _field.InvalidateVisual();
    }
    public void Dispose() => _timer.Stop();
    private sealed class SignalField : Control
    {
        public double Phase;
        public SignalField() { Height = 120; }
        public override void Render(DrawingContext context) { var pen = new Pen(Ui.Phosphor, 1); for (var x = 0; x < Bounds.Width - 3; x += 3) { double Y(double n) => 60 + Math.Sin(n * .06 + Phase) * 28 * Math.Sin(n * .015); context.DrawLine(pen, new Point(x, Y(x)), new Point(x + 3, Y(x + 3))); } }
    }
}
