using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerOS.Apps;
using HammerOS.Core;

namespace HammerOS.Desktop;

public sealed class ShellWindow : Window
{
    public SystemState State { get; }
    public Canvas Workspace { get; } = new() { ClipToBounds = true };
    public List<AppFrame> Frames { get; } = [];
    private readonly Panel _layers = new();
    private readonly StackPanel _tasks = Ui.Row(4);
    private readonly DesktopBackdrop _backdrop = new();
    private readonly TextBlock _clock = Ui.Text("", 12, Ui.Ink, true);
    private readonly TextBlock _network = Ui.Label("●  INTRANET CONNECTED");
    private readonly TextBlock _employee = Ui.Text("", 12);
    private readonly TextBlock _greeting = new() { FontSize = 35, FontFamily = Ui.Serif, Foreground = Brush.Parse("#C0D1BD"), LineHeight = 43 };
    private readonly TextBlock _edition = Ui.Label("OS  /  SEVERED FLOOR");
    private AppFrame? _active;
    private readonly Grid _desktopRoot;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Border? _launcher, _lock, _toast;
    private int _z;
    private bool _started;
    public bool IsLocked => _lock is not null;

    public ShellWindow(SystemState state, bool windowed = false)
    {
        State = state; Title = "HammerOS — Severed Floor"; Width = 1440; Height = 900; MinWidth = 900; MinHeight = 640;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://HammerOS/Assets/icon.png")));
        Background = Ui.Dark; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!windowed) WindowState = WindowState.FullScreen;
        var layout = new Grid { RowDefinitions = new RowDefinitions("52,*,62") }; _desktopRoot = layout;
        var wordmark = Ui.Text("hammer", 27); wordmark.FontFamily = Ui.Serif; wordmark.FontWeight = FontWeight.Bold;
        var headerLeft = Ui.Row(16, new Glyph("refinement") { Width = 23, Height = 23, VerticalAlignment = VerticalAlignment.Center }, wordmark, new Border { Width = 1, Height = 21, Background = Ui.Line }, _edition);
        var headerRight = Ui.Row(18, _network, Ui.Text("│", 14, Ui.Line), _employee, Ui.IconButton("lock", "Lock workstation · Ctrl+L", Lock), Ui.IconButton("maximize", "Toggle fullscreen · F11", ToggleFullscreen), Ui.IconButton("power", "End session", ShowPower));
        var header = new Border { Background = Ui.Cream, Padding = new Thickness(23, 0, 12, 0), Child = Ui.Columns("*,Auto", headerLeft, headerRight) };
        layout.Children.Add(header);
        var desktop = new Panel { ClipToBounds = true, Children = { _backdrop } }; Grid.SetRow(desktop, 1); layout.Children.Add(desktop);
        var bgCopy = new StackPanel { Width = 430, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 52, 42, 0), Spacing = 17 };
        bgCopy.Children.Add(Ui.Label("PERSONNEL TERMINAL  /  004", Brush.Parse("#7DABA0")));
        bgCopy.Children.Add(_greeting);
        bgCopy.Children.Add(Ui.Rule(Brush.Parse("#3A5C57")));
        bgCopy.Children.Add(Ui.Columns("*,Auto", Ui.Label("DEPARTMENT", Brush.Parse("#72958C")), Ui.Text("Macrodata Refinement", 11, Brush.Parse("#BDCCB9"))));
        bgCopy.Children.Add(Ui.Columns("*,Auto", Ui.Label("DISPOSITION", Brush.Parse("#72958C")), Ui.Label("●  NOMINAL", Brush.Parse("#BDCCB9"))));
        desktop.Children.Add(bgCopy);
        var credo = Ui.Stack(10, Ui.Label("THE WORK IS MYSTERIOUS AND IMPORTANT.", Brush.Parse("#7FA397")), new TextBlock { Text = "Every number. Every day.", FontFamily = Ui.Serif, FontSize = 29, Foreground = Brush.Parse("#6F988D") });
        credo.VerticalAlignment = VerticalAlignment.Bottom; credo.HorizontalAlignment = HorizontalAlignment.Left; credo.Margin = new Thickness(164, 0, 0, 42); desktop.Children.Add(credo);
        var signature = Ui.Stack(12, new CorporateSeal { Width = 76, Height = 76, Color = Brush.Parse("#648F83"), HorizontalAlignment = HorizontalAlignment.Left }, Ui.Label("HAMMER INDUSTRIES", Brush.Parse("#8AB1A1")), Ui.Label("A BETTER YOU. A BETTER US.", Brush.Parse("#608A7D")));
        signature.HorizontalAlignment = HorizontalAlignment.Right; signature.VerticalAlignment = VerticalAlignment.Bottom; signature.Margin = new Thickness(0, 0, 48, 38); desktop.Children.Add(signature);
        var shortcuts = Ui.Stack(12); shortcuts.Margin = new Thickness(18, 27, 0, 0); shortcuts.HorizontalAlignment = HorizontalAlignment.Left; shortcuts.VerticalAlignment = VerticalAlignment.Top;
        foreach (var id in new[] { "refinement", "files", "terminal", "browser", "control", "settings" })
        {
            var app = AppCatalog.All.First(x => x.Id == id);
            var glyph = new Glyph(app.Symbol) { Width = 25, Height = 25, Color = Brush.Parse("#CFDCC4"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var label = Ui.Text(id == "refinement" ? "Refinement" : id == "control" ? "Control Panel" : app.Name, 11, Brush.Parse("#BDCEBA")); label.HorizontalAlignment = HorizontalAlignment.Center;
            var tile = new Border { Width = 48, Height = 44, BorderBrush = Brush.Parse("#506D61"), BorderThickness = new Thickness(1), Background = Brush.Parse("#213F43"), CornerRadius = new CornerRadius(3), Child = glyph, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = Ui.Button("", () => OpenApp(id), "dark"); icon.Content = Ui.Stack(9, tile, label); icon.Width = 106; icon.Height = 86;
            ToolTip.SetTip(icon, app.Description); var hover = new TranslateTransform(); glyph.RenderTransform = hover;
            Action? cancelHover = null;
            icon.PointerEntered += (_, _) => { cancelHover?.Invoke(); var start = hover.Y; cancelHover = Motion.Animate(180, t => hover.Y = start + (-3 - start) * t); };
            icon.PointerExited += (_, _) => { cancelHover?.Invoke(); var start = hover.Y; cancelHover = Motion.Animate(180, t => hover.Y = start * (1 - t)); };
            shortcuts.Children.Add(icon);
        }
        desktop.Children.Add(new ScrollViewer { Content = shortcuts, Width = 136, HorizontalAlignment = HorizontalAlignment.Left, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden }); desktop.Children.Add(Workspace);
        var launch = Ui.Button("", ToggleLauncher); launch.Content = Ui.Row(10, new Glyph("refinement") { Width = 18, Height = 18 }, Ui.Text("Applications", 12)); launch.Padding = new Thickness(14, 10);
        var lowerLeft = Ui.Row(15, launch, new Border { Width = 1, Height = 24, Background = Ui.Line }, _tasks);
        var lowerRight = Ui.Row(18, Ui.Button("Show desktop", ShowDesktop), _clock);
        var taskbar = new Border { Background = Ui.Cream, Padding = new Thickness(10, 0, 22, 0), Child = Ui.Columns("*,Auto", lowerLeft, lowerRight) }; Grid.SetRow(taskbar, 2); layout.Children.Add(taskbar);
        _layers.Children.Add(layout); Content = _layers;
        _timer.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("HH:mm   ·   dd MMM").ToUpperInvariant(); _timer.Start(); _clock.Text = DateTime.Now.ToString("HH:mm   ·   dd MMM").ToUpperInvariant();
        State.Changed += ApplyPreferences; ApplyPreferences();
        Opened += (_, _) => Dispatcher.UIThread.Post(StartDesktop, DispatcherPriority.Loaded);
        Workspace.SizeChanged += (_, e) => { _edition.IsVisible = e.NewSize.Width >= 1150; bgCopy.IsVisible = e.NewSize.Width >= 1150; foreach (var frame in Frames) frame.Constrain(); };
        AddHandler(KeyDownEvent, OnGlobalKey, RoutingStrategies.Tunnel);
        Closed += (_, _) => { _timer.Stop(); State.Save(); State.Changed -= ApplyPreferences; foreach (var frame in Frames.ToArray()) (frame.AppContent as IDisposable)?.Dispose(); };
    }
    public void StartDesktop()
    {
        if (_started) return; _started = true;
        OpenApp("refinement");
        if (Workspace.Bounds.Width >= 1150) OpenApp("terminal");
        if (State.StorageWarning is { } message) Toast(message);
    }
    public void ApplyPreferences()
    {
        Motion.Enabled = State.Preferences.Motion;
        _backdrop.Palette = State.Preferences.Wallpaper; _backdrop.Scanlines = State.Preferences.Scanlines; _backdrop.InvalidateVisual();
        _employee.Text = $"{State.Preferences.Employee}  /  MDR";
        _greeting.Text = $"Welcome back,\n{State.Preferences.Employee}";
        _network.Text = State.Online ? "●  INTRANET CONNECTED" : "○  INTRANET OFFLINE";
    }
    public void OpenApp(string id)
    {
        if (IsLocked) return;
        CloseLauncher();
        var existing = Frames.FirstOrDefault(x => x.AppId == id);
        if (existing is not null) { existing.Restore(); return; }
        var app = AppCatalog.All.FirstOrDefault(x => x.Id == id); if (app is null) return;
        Control content = id switch
        {
            "refinement" => new RefinementView(State), "terminal" => new TerminalView(State, OpenApp),
            "files" => new FilesView(State, Toast), "browser" => new IntranetView(State), "notes" => new NotesView(State, Toast),
            "settings" => new SettingsView(State, ToggleFullscreen, Toast), "control" => new ControlView(State, Toast),
            "network" => new NetworkView(State), "wellness" => new WellnessView(State), "handbook" => new HandbookView(),
            _ => new TextBlock()
        };
        var size = id switch { "refinement" => new Size(Workspace.Bounds.Width >= 1320 ? Math.Clamp(Workspace.Bounds.Width - 676, 760, 940) : 826, 568), "terminal" => new Size(470, 352), "files" => new Size(820, 570), "settings" => new Size(770, 610), "wellness" => new Size(680, 568), "network" => new Size(785, 600), _ => new Size(790, 570) };
        var frame = new AppFrame(this, app, content, Math.Min(size.Width, Math.Max(440, Workspace.Bounds.Width - 40)), Math.Min(size.Height, Math.Max(300, Workspace.Bounds.Height - 36)));
        Frames.Add(frame); Workspace.Children.Add(frame);
        double x = id == "refinement" ? 156 : id == "terminal" && Frames.Count == 2 ? Workspace.Bounds.Width - frame.Width - 32 : (Workspace.Bounds.Width - frame.Width) / 2 + (Frames.Count % 3) * 15;
        double y = id == "refinement" ? 52 : id == "terminal" && Frames.Count == 2 ? Workspace.Bounds.Height - frame.Height - 40 : 75 + (Frames.Count % 4) * 24;
        frame.SetPosition(x, y); FocusFrame(frame); Motion.Enter(frame, 22); UpdateTaskbar(); State.Record($"Opened {app.Name}");
    }
    public void FocusFrame(AppFrame active) { _active = active; active.ZIndex = ++_z; foreach (var frame in Frames) frame.SetActive(frame == active); UpdateTaskbar(); }
    public void RemoveFrame(AppFrame frame) { (frame.AppContent as IDisposable)?.Dispose(); Frames.Remove(frame); Workspace.Children.Remove(frame); if (Frames.LastOrDefault(x => !x.IsMinimized) is { } next) FocusFrame(next); UpdateTaskbar(); }
    public void UpdateTaskbar()
    {
        _tasks.Children.Clear();
        foreach (var frame in Frames)
        {
            var app = AppCatalog.All.First(x => x.Id == frame.AppId);
            var b = Ui.Button("", () => { if (frame.IsMinimized) frame.Restore(); else FocusFrame(frame); });
            b.Content = Ui.Row(8, new Glyph(app.Symbol) { Width = 16, Height = 16 }, Ui.Text(app.Id == "refinement" ? "Refinement" : app.Name, 11));
            b.BorderThickness = new Thickness(0, 0, 0, 2); b.BorderBrush = frame.IsMinimized ? Brushes.Transparent : Brush.Parse("#54796A");
            b.Background = frame == _active && !frame.IsMinimized ? Brush.Parse("#D4DECE") : Brushes.Transparent;
            if (Frames.Count > 5) { b.Content = new Glyph(app.Symbol) { Width = 16, Height = 16 }; ToolTip.SetTip(b, app.Name); }
            _tasks.Children.Add(b);
        }
    }
    public void ShowDesktop() { if (Frames.Any(x => !x.IsMinimized)) foreach (var f in Frames) f.Minimize(); else foreach (var f in Frames) f.Restore(); }
    public void ToggleFullscreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    private void OnGlobalKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        if (IsLocked) return;
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { ToggleLauncher(); e.Handled = true; }
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Source is not TerminalSurface) { Lock(); e.Handled = true; }
        if (e.Key == Key.Escape && _launcher is not null) { CloseLauncher(); e.Handled = true; }
    }
    public void ToggleLauncher()
    {
        if (IsLocked) return; if (_launcher is not null) { CloseLauncher(); return; }
        var search = new TextBox { Watermark = "Find an application…", Name = "AppSearch" };
        var list = Ui.Stack(3);
        void Populate()
        {
            list.Children.Clear();
            foreach (var app in AppCatalog.All.Where(x => (x.Name + x.Description).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                var text = Ui.Stack(4, Ui.Text(app.Name, 13), Ui.Text(app.Description, 11, Ui.Muted));
                var b = Ui.Button("", () => OpenApp(app.Id)); b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Padding = new Thickness(12, 10);
                b.Content = Ui.Columns("38,*,20", new Glyph(app.Symbol) { Width = 21, Height = 21, VerticalAlignment = VerticalAlignment.Center }, text, new Glyph("arrow") { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center }); list.Children.Add(b);
            }
            if (list.Children.Count == 0) list.Children.Add(Ui.Pad(Ui.Text("No matching applications.", 12, Ui.Muted), 18));
        }
        search.TextChanged += (_, _) => Populate(); search.KeyDown += (_, e) => { if (e.Key == Key.Enter && list.Children.FirstOrDefault() is Button b) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        Populate();
        var panel = Ui.Stack(18, Ui.Label("YOUR WORKSTATION"), Ui.Text("Application directory", 27), search, Ui.Scroll(list), Ui.Rule(), Ui.Label("CTRL + SPACE TO OPEN  ·  ESC TO DISMISS"));
        ((ScrollViewer)panel.Children[3]).MaxHeight = Math.Max(240, Math.Min(440, Bounds.Height - 310));
        _launcher = new Border { Width = 420, Background = Ui.Cream, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Padding = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 69), Child = panel, BoxShadow = new BoxShadows(new BoxShadow { Blur = 35, Color = Color.Parse("#60000000") }) };
        _layers.Children.Add(_launcher); Motion.Enter(_launcher, 12); search.Focus();
    }
    private void CloseLauncher() { if (_launcher is null) return; var panel = _launcher; _launcher = null; Motion.Animate(120, t => panel.Opacity = 1 - t, () => _layers.Children.Remove(panel)); }
    public void Lock()
    {
        if (IsLocked) return; CloseLauncher(); _desktopRoot.IsEnabled = false;
        var title = new TextBlock { Text = "Your work will be here.", FontFamily = Ui.Serif, FontSize = 48, Foreground = Ui.Cream, HorizontalAlignment = HorizontalAlignment.Center };
        var resume = Ui.Button("Resume session  →", Unlock, "primary"); resume.HorizontalAlignment = HorizontalAlignment.Center;
        var content = Ui.Stack(28, new CorporateSeal { Width = 110, Height = 110, HorizontalAlignment = HorizontalAlignment.Center }, title, Ui.Label($"{State.Preferences.Employee.ToUpperInvariant()}  /  MACRODATA REFINEMENT", Ui.Phosphor), resume, Ui.Text("F11  Fullscreen    ·    Alt+F4  Exit", 11, Ui.Muted, true));
        foreach (var c in content.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center; content.HorizontalAlignment = HorizontalAlignment.Center;
        _lock = new Border { Background = Ui.Dark, Child = content }; _layers.Children.Add(_lock); Motion.Enter(_lock, 0); resume.Focus();
    }
    public void Unlock() { if (_lock is null) return; var screen = _lock; _lock = null; _desktopRoot.IsEnabled = true; Motion.Animate(330, t => screen.Opacity = 1 - t, () => _layers.Children.Remove(screen)); }
    private void ShowPower()
    {
        if (IsLocked) return;
        var overlay = new Border { Background = Brush.Parse("#B0102E34") };
        var panel = Ui.Stack(20, Ui.Heading("SESSION CONTROL", "The day is done.", "Your files, preferences, and refinement progress are saved automatically."));
        var cancel = Ui.Button("Return to work", () => _layers.Children.Remove(overlay), "outline");
        panel.Children.Add(Ui.Row(12, cancel, Ui.Button("End session", () => { State.Save(); Close(); }, "primary")));
        overlay.Child = new Border { Width = 475, Padding = new Thickness(36), Background = Ui.Cream, Child = panel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; _layers.Children.Add(overlay); Motion.Enter(overlay, 0); cancel.Focus();
    }
    public void Toast(string message)
    {
        if (_toast is not null) _layers.Children.Remove(_toast);
        var text = Ui.Text(message, 12); text.TextWrapping = TextWrapping.Wrap;
        var toast = new Border { MaxWidth = 470, Padding = new Thickness(20, 15), Background = Ui.Cream, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = Ui.Row(12, new Glyph("check") { Width = 18, Height = 18 }, text), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 24, 24, 76) };
        _toast = toast; _layers.Children.Add(toast); Motion.Enter(toast);
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) }; delay.Tick += (_, _) => { delay.Stop(); Motion.Animate(180, t => toast.Opacity = 1 - t, () => _layers.Children.Remove(toast)); }; delay.Start();
    }
}
