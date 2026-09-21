using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class SettingsView : UserControl
{
    public SettingsView(SystemState state, Action fullscreen, Action<string> toast)
    {
        var stack = Ui.Stack(23, Ui.Heading("WORKSTATION PREFERENCES", "Make yourself comfortable.", "Small adjustments. A more harmonious working day."), Ui.Rule());
        void Toggle(string title, string description, bool value, Action<bool> update)
        {
            var toggle = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            toggle.IsCheckedChanged += (_, _) => { update(toggle.IsChecked == true); state.Save(); };
            stack.Children.Add(Ui.Columns("*,Auto", Ui.Stack(5, Ui.Text(title, 14), Ui.Text(description, 11, Ui.Muted)), toggle));
        }
        Toggle("Smooth motion", "Eased windows, responsive interactions, and drifting numbers.", state.Preferences.Motion, x => state.Preferences.Motion = x);
        Toggle("Phosphor texture", "Subtle scanlines across your desktop and refinement display.", state.Preferences.Scanlines, x => state.Preferences.Scanlines = x);
        stack.Children.Add(Ui.Rule()); stack.Children.Add(Ui.Label("DESKTOP PALETTE"));
        var themes = Ui.Row(12);
        foreach (var (name, hex) in new[] { ("Petrol", "#1A3D43"), ("Evergreen", "#1D3935"), ("Midnight", "#172B3C") })
        {
            var swatch = new Border { Width = 127, Height = 48, Background = Brush.Parse(hex), Child = Ui.Label("H", Ui.Phosphor), Padding = new Thickness(14) };
            var b = Ui.Button("", () => { state.Preferences.Wallpaper = name; state.Save(); toast(name + " palette applied."); }, "outline"); b.Content = Ui.Stack(9, swatch, Ui.Text(name, 11)); b.Padding = new Thickness(7); themes.Children.Add(b);
        }
        stack.Children.Add(themes); stack.Children.Add(Ui.Rule());
        var employee = new TextBox { Text = state.Preferences.Employee, Width = 190, MaxLength = 24 };
        stack.Children.Add(Ui.Columns("*,Auto", Ui.Stack(5, Ui.Text("Employee designation", 14), Ui.Text("Displayed across your session.", 11, Ui.Muted)), Ui.Row(8, employee, Ui.Button("Apply", () => { if (!string.IsNullOrWhiteSpace(employee.Text)) { state.Preferences.Employee = employee.Text.Trim(); state.Save(); toast("Employee designation updated."); } }, "outline"))));
        stack.Children.Add(Ui.Columns("*,Auto", Ui.Stack(5, Ui.Text("Display mode", 14), Ui.Text("You can also press F11 at any time.", 11, Ui.Muted)), Ui.Button("Toggle fullscreen", fullscreen, "outline")));
        stack.Children.Add(Ui.Rule()); stack.Children.Add(Ui.Label("HAMMEROS 0.1.0  /  A FICTIONAL OPERATING ENVIRONMENT"));
        Content = Ui.Scroll(Ui.Pad(stack, 30));
    }
}

public sealed class NetworkView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly StackPanel _page = Ui.Stack(22);
    private TextBlock _diagnostic = Ui.Text("", 12, Ui.Ink, true);
    public NetworkView(SystemState state) { _state = state; Content = Ui.Scroll(Ui.Pad(_page, 30)); state.Changed += Refresh; Refresh(); }
    private void Refresh()
    {
        _diagnostic = Ui.Text("", 12, Ui.Ink, true);
        _page.Children.Clear(); _page.Children.Add(Ui.Heading("DEPARTMENT COMMUNICATIONS", "Connected. Within reason.", "Your workstation is part of something greater."));
        _page.Children.Add(Ui.Rule());
        var toggle = new ToggleSwitch { IsChecked = _state.Preferences.Network, OnContent = "Connected", OffContent = "Offline" };
        toggle.IsCheckedChanged += (_, _) => { _state.Preferences.Network = toggle.IsChecked == true; _state.Record(_state.Preferences.Network ? "Network adapter connected" : "Network adapter disconnected"); _state.Save(); };
        _page.Children.Add(Ui.Columns("*,Auto", Ui.Stack(8, Ui.Row(10, new Glyph("network") { Width = 23, Height = 23 }, Ui.Text("SEVERED-INTRANET", 17)), Ui.Text(_state.Online ? "●  Secure department connection" : "○  Connection unavailable", 12, Ui.Muted)), toggle));
        var topology = Ui.Row(20, Node("MDR-04", "WORKSTATION"), Ui.Text(_state.Online ? "────────" : "─ ─ ─ ─", 17, Ui.Muted), Node("10.4.0.1", "DEPARTMENT"), Ui.Text("────────", 17, Ui.Muted), Node("LUMON", "INTRANET"));
        _page.Children.Add(new Border { Background = Brush.Parse("#DFE4D6"), Padding = new Thickness(24), Child = topology });
        foreach (var (key, value) in new[] { ("IPv4 address", "10.4.0.104"), ("Gateway", "10.4.0.1"), ("DNS server", "10.4.0.2"), ("Network scope", "Internal / simulated"), ("External access", "Outside this environment") })
            _page.Children.Add(Ui.Columns("*,Auto", Ui.Text(key, 12, Ui.Muted), Ui.Text(value, 12, Ui.Ink, true)));
        _page.Children.Add(Ui.Rule()); _page.Children.Add(Ui.Row(15, Ui.Button("Run diagnostics", () => { _diagnostic.Text = _state.Online ? "✓ Adapter ready\n✓ Gateway reachable (simulated, 1 ms)\n✓ Intranet relay available" : "Connection unavailable. Enable the adapter and\ncheck the Intranet relay in Control Panel."; Motion.Enter(_diagnostic, 4); }, "primary"), _diagnostic));
    }
    private static Control Node(string title, string label) => Ui.Stack(9, Ui.Text(title, 14, Ui.Ink, true), Ui.Label(label));
    public void Dispose() => _state.Changed -= Refresh;
}

public sealed class ControlView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly StackPanel _services = Ui.Stack(12), _logs = Ui.Stack(9);
    public ControlView(SystemState state, Action<string> toast)
    {
        _state = state;
        var stack = Ui.Stack(22, Ui.Heading("ADMINISTRATION  /  MDR-04", "All systems in harmony.", "Manage the services that keep your work moving."), Ui.Rule());
        var metrics = Ui.Columns("*,*,*", Metric("WORKSTATION", "MDR-04"), Metric("MEMORY", "64 MB"), Metric("ENVIRONMENT", "Simulated")); stack.Children.Add(metrics); stack.Children.Add(Ui.Rule()); stack.Children.Add(Ui.Label("DEPARTMENT SERVICES")); stack.Children.Add(_services); stack.Children.Add(Ui.Rule());
        stack.Children.Add(Ui.Columns("*,Auto", Ui.Label("SESSION ACTIVITY"), Ui.Button("Refresh", Refresh, "outline"))); stack.Children.Add(_logs); Content = Ui.Scroll(Ui.Pad(stack, 30)); state.Changed += Refresh; Refresh();
    }
    private static Control Metric(string label, string text) => Ui.Stack(9, Ui.Label(label), Ui.Text(text, 22));
    private void Refresh()
    {
        _services.Children.Clear();
        foreach (var service in _state.Services)
        {
            var button = Ui.Button(service.Value ? "Stop" : "Start", () => { _state.Services[service.Key] = !service.Value; _state.Record($"{service.Key} {(!service.Value ? "started" : "stopped")}"); _state.Save(); }, "outline"); button.Width = 75;
            _services.Children.Add(Ui.Columns("*,110,Auto", Ui.Text(service.Key, 13), Ui.Label(service.Value ? "● RUNNING" : "○ STOPPED"), button));
        }
        _logs.Children.Clear(); foreach (var entry in _state.Events.Take(6)) _logs.Children.Add(Ui.Text("›  " + entry, 11, Ui.Muted, true));
    }
    public void Dispose() => _state.Changed -= Refresh;
}

public sealed class WellnessView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Border _orb;
    private readonly TextBlock _breath = Ui.Label("A MOMENT, JUST FOR YOU.");
    private readonly TextBlock _fact;
    private readonly Button _start;
    private bool _running;
    private DateTime _began;
    private int _index;
    private readonly string[] _facts = ["Your outie is kind to those around them.", "Your outie appreciates a well-organized drawer.", "Your outie has a lovely sense of curiosity.", "Your outie is capable of great patience.", "Your outie knows when it is time to rest."];
    public WellnessView(SystemState state)
    {
        _state = state;
        _fact = new TextBlock { Text = _facts[0], FontFamily = Ui.Serif, FontSize = 23, Foreground = Ui.Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 460 };
        _orb = new Border { Width = 130, Height = 130, CornerRadius = new CornerRadius(100), BorderBrush = Brush.Parse("#688A78"), BorderThickness = new Thickness(1), Background = Brush.Parse("#D6DFCB"), Child = new CorporateSeal { Width = 78, Height = 78, Color = Brush.Parse("#628875"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new ScaleTransform(1, 1) };
        _start = Ui.Button("Begin a quiet moment", Toggle, "primary");
        var stack = Ui.Stack(24, Ui.Label("DEPARTMENT OF WELLNESS"), new TextBlock { Text = "You are doing well.", FontFamily = Ui.Serif, FontSize = 36, Foreground = Ui.Ink }, _orb, _breath, _fact, Ui.Row(12, _start, Ui.Button("Another fact", () => { _index = (_index + 1) % _facts.Length; _fact.Text = _facts[_index]; Motion.Enter(_fact, 6); }, "outline")), Ui.Text("Please enjoy all facts equally.", 11, Ui.Muted));
        foreach (var child in stack.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Margin = new Thickness(30); Content = Ui.Scroll(stack);
        _timer.Tick += (_, _) =>
        {
            if (!_running) return;
            if (!_state.Services["Wellness scheduler"]) { Stop(); _breath.Text = "WELLNESS SCHEDULER IS PAUSED."; return; }
            var elapsed = (DateTime.Now - _began).TotalSeconds;
            if (elapsed >= 60) { Stop(); _breath.Text = "SESSION COMPLETE. TAKE THIS CALM WITH YOU."; return; }
            var phase = elapsed % 12; _breath.Text = (phase < 4 ? "BREATHE IN" : phase < 6 ? "HOLD" : "BREATHE OUT") + $"  /  {60 - (int)elapsed:00}s";
            var size = phase < 4 ? 1 + phase / 4 * .18 : phase < 6 ? 1.18 : 1.18 - (phase - 6) / 6 * .18;
            if (_state.Preferences.Motion && _orb.RenderTransform is ScaleTransform scale) { scale.ScaleX = size; scale.ScaleY = size; }
        }; _timer.Start();
    }
    private void Toggle() { if (_running) { Stop(); return; } if (!_state.Services["Wellness scheduler"]) { _breath.Text = "RESUME WELLNESS IN CONTROL PANEL."; return; } _running = true; _began = DateTime.Now; _start.Content = "End quiet moment"; }
    private void Stop() { _running = false; _start.Content = "Begin a quiet moment"; _breath.Text = "A MOMENT, JUST FOR YOU."; _orb.RenderTransform = new ScaleTransform(1, 1); }
    public void Dispose() => _timer.Stop();
}

public sealed class HandbookView : UserControl
{
    public HandbookView()
    {
        var stack = Ui.Stack(22, Ui.Heading("HAMMER INDUSTRIES  /  REV. 01", "The employee handbook.", "An informed employee is a content employee."), Ui.Rule());
        foreach (var (title, body) in new[] {
            ("01   Your workstation", "Open applications from the desktop or the Applications directory. Drag a window by its title to move it. Drag its lower-right corner to resize. The title controls minimize, maximize, and close; a double-click on the title also maximizes."),
            ("02   The work", "In Macrodata Refinement, click a number to select the surrounding group. Assign the group to a bin below. Each collected number advances that bin by one percent. Fill all five bins to complete Siena. Use the arrow keys and Space to select with the keyboard; Escape clears the selection."),
            ("03   Your records", "File Explorer and the Department terminal share the same virtual drive. Records can be created, edited, and deleted without affecting files on your computer. Memoranda saves to /personal/notes.txt when you press Save."),
            ("04   Your real terminal", "Select PowerShell inside Terminal to open a real, unelevated Windows shell. Its commands operate on your actual computer. Arrow keys, Tab completion, Ctrl+C, ANSI colors, Unicode, and scrollback are supported. Drag to select; Ctrl+Shift+C copies and Ctrl+Shift+V pastes. Ctrl+L clears PowerShell instead of locking HammerOS. Use the header lock button while working in PowerShell. Closing the terminal or ending HammerOS stops its shell and child processes."),
            ("05   Your connections", "The Intranet browser contains company pages, with address navigation and history. It is a fictional browser, with no external websites. Network and Control Panel manage the simulated connection and services. These switches do not change your computer's real network."),
            ("06   Your preferences", "Settings controls the desktop palette, phosphor texture, motion, and employee name. Files, refinement progress, and preferences persist between sessions in your local application data. Service switches reset when HammerOS starts."),
            ("07   Your shortcuts", "Ctrl+Space  ·  Applications\nCtrl+L  ·  Lock workstation (outside PowerShell)\nF11  ·  Fullscreen / windowed\nAlt+F4  ·  Exit HammerOS\nEscape  ·  Dismiss application directory") })
        { stack.Children.Add(Ui.Text(title, 18)); stack.Children.Add(new TextBlock { Text = body, FontSize = 13, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 22 }); stack.Children.Add(Ui.Rule()); }
        stack.Children.Add(Ui.Label("AN INDEPENDENT SEVERANCE-INSPIRED FAN PROJECT.")); Content = Ui.Scroll(Ui.Pad(stack, 34));
    }
}
