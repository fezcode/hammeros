using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Net.NetworkInformation;
using HammerOS.Apps;
using HammerOS.Core;

namespace HammerOS.Desktop;

public sealed class ShellWindow : Window
{
    public SystemState State { get; }
    public Canvas Workspace { get; } = new() { ClipToBounds = true };
    public List<AppFrame> Frames { get; } = [];
    private readonly Dictionary<string, int> _instanceCounts = new();
    private readonly Panel _layers = new();
    private readonly StackPanel _tasks = Ui.Row(4);
    private readonly DesktopBackdrop _backdrop = new();
    private readonly TextBlock _clock = Ui.Text("", 12, Ui.Ink, true);
    private readonly TextBlock _network = Ui.Label("●  INTRANET CONNECTED");
    private readonly TextBlock _employee = Ui.Text("", 12);
    private readonly TextBlock _greeting = new() { FontSize = 35, FontFamily = Ui.Serif, Foreground = Brush.Parse("#C0D1BD"), LineHeight = 43 };
    private AppFrame? _active;
    private readonly Grid _desktopRoot;
    private readonly MenuItem _windowMenu = new() { Header = "Window" };
    private bool _overlayOpen, _allowExit, _taskbarMenuOpen;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Border? _launcher, _lock, _toast;
    private int _z;
    private bool _started;
    private CommandPalette? _spotlight;
    private IReadOnlyList<WindowsApplication> _windowsApps = [];
    private Task<IReadOnlyList<WindowsApplication>>? _windowsAppsLoading;
    public bool IsLocked => _lock is not null;

    public ShellWindow(SystemState state, bool windowed = false)
    {
        State = state; Title = "HammerOS — Workspace"; Width = 1440; Height = 900; MinWidth = 900; MinHeight = 640;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://HammerOS/Assets/icon.png")));
        Background = Ui.Dark; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!windowed) WindowState = WindowState.FullScreen;
        var layout = new Grid { RowDefinitions = new RowDefinitions("52,*,62") }; _desktopRoot = layout;
        var wordmark = Ui.Text("hammer", 27); wordmark.FontFamily = Ui.Serif; wordmark.FontWeight = FontWeight.Bold;
        var brand = Ui.Button("", ToggleLauncher); brand.Content = Ui.Row(12, new Glyph("refinement") { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center }, wordmark); brand.Padding = new Thickness(0, 3, 8, 3); ToolTip.SetTip(brand, "Application directory");
        var headerLeft = Ui.Row(10, brand, new Border { Width = 1, Height = 21, Background = Ui.Line }, BuildMenu());
        var netButton = Ui.Button("", () => OpenApp("network")); netButton.Content = _network; netButton.Padding = new Thickness(7, 5);
        var profile = Ui.Button("", () => OpenApp("settings")); profile.Content = _employee; profile.Padding = new Thickness(7, 5);
        var clockButton = Ui.Button("", () => { }); clockButton.Name = "ClockMenu"; clockButton.Content = _clock; clockButton.Click += (_, _) => { var flyout = new Flyout(); flyout.FlyoutPresenterClasses.Add("calendar-popup"); var calendar = new CalendarView(State, true, () => { flyout.Hide(); OpenApp("calendar"); }); flyout.Content = calendar; flyout.Closed += (_, _) => calendar.Dispose(); flyout.ShowAt(clockButton); };
        var headerRight = Ui.Row(5, netButton, profile, clockButton, Ui.IconButton("lock", "Lock workstation · Ctrl+L", Lock), Ui.IconButton("maximize", "Toggle fullscreen · F11", ToggleFullscreen), Ui.IconButton("power", "End session", ShowPower));
        var header = new Border { Background = Ui.Cream, Padding = new Thickness(23, 0, 12, 0), Child = Ui.Columns("*,Auto", headerLeft, headerRight) };
        layout.Children.Add(header);
        var desktop = new Panel { ClipToBounds = true, Children = { _backdrop } }; Grid.SetRow(desktop, 1); layout.Children.Add(desktop);
        var bgCopy = new StackPanel { Width = 430, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 52, 42, 0), Spacing = 17 };
        bgCopy.Children.Add(Ui.Label("PERSONNEL TERMINAL  /  004", Brush.Parse("#7DABA0")));
        bgCopy.Children.Add(_greeting);
        bgCopy.Children.Add(Ui.Rule(Brush.Parse("#3A5C57")));
        bgCopy.Children.Add(Ui.Columns("*,Auto", Ui.Label("DEPARTMENT", Brush.Parse("#72958C")), Ui.Text("Refinement", 11, Brush.Parse("#BDCCB9"))));
        bgCopy.Children.Add(Ui.Columns("*,Auto", Ui.Label("DISPOSITION", Brush.Parse("#72958C")), Ui.Label("●  NOMINAL", Brush.Parse("#BDCCB9"))));
        desktop.Children.Add(bgCopy);
        var credo = Ui.Stack(10, Ui.Label("MAKE ROOM FOR GOOD IDEAS.", Brush.Parse("#7FA397")), new TextBlock { Text = "Every number. Every day.", FontFamily = Ui.Serif, FontSize = 29, Foreground = Brush.Parse("#6F988D") });
        credo.VerticalAlignment = VerticalAlignment.Bottom; credo.HorizontalAlignment = HorizontalAlignment.Left; credo.Margin = new Thickness(164, 0, 0, 42); desktop.Children.Add(credo);
        var signature = Ui.Stack(12, new CorporateSeal { Width = 76, Height = 76, Color = Brush.Parse("#648F83"), HorizontalAlignment = HorizontalAlignment.Left }, Ui.Label("HAMMER INDUSTRIES", Brush.Parse("#8AB1A1")), Ui.Label("A BETTER YOU. A BETTER US.", Brush.Parse("#608A7D")));
        signature.HorizontalAlignment = HorizontalAlignment.Right; signature.VerticalAlignment = VerticalAlignment.Bottom; signature.Margin = new Thickness(0, 0, 48, 38); desktop.Children.Add(signature);
        var shortcuts = Ui.Stack(12); shortcuts.Margin = new Thickness(18, 27, 0, 0); shortcuts.HorizontalAlignment = HorizontalAlignment.Left; shortcuts.VerticalAlignment = VerticalAlignment.Top;
        foreach (var id in new[] { "refinement", "files", "terminal", "tiled", "web", "settings" })
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
        brand.Classes.Add("launcher-toggle"); launch.Classes.Add("launcher-toggle");
        var taskScroll = new ScrollViewer { Content = _tasks, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalAlignment = VerticalAlignment.Center };
        var lowerLeft = Ui.Columns("Auto,31,*", launch, new Border { Width = 1, Height = 24, Background = Ui.Line, VerticalAlignment = VerticalAlignment.Center }, taskScroll);
        var lowerRight = Ui.Row(18, Ui.Button("Show desktop", ShowDesktop), Ui.Label("WORKSPACE 01"));
        var taskbar = new Border { Name = "Taskbar", Background = Ui.Cream, Padding = new Thickness(10, 0, 22, 0), Child = Ui.Columns("*,Auto", lowerLeft, lowerRight), ContextMenu = CreateTaskbarMenu() }; Grid.SetRow(taskbar, 2); layout.Children.Add(taskbar);
        _layers.Children.Add(layout); Content = _layers;
        _timer.Tick += (_, _) => { _clock.Text = DateTime.Now.ToString("HH:mm   ·   dd MMM").ToUpperInvariant(); _network.Text = NetworkInterface.GetIsNetworkAvailable() ? "●  NETWORK ONLINE" : "○  NETWORK OFFLINE"; }; _timer.Start(); _clock.Text = DateTime.Now.ToString("HH:mm   ·   dd MMM").ToUpperInvariant();
        State.Changed += ApplyPreferences; ApplyPreferences();
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateWallpaper(); };
        Opened += (_, _) => Dispatcher.UIThread.Post(StartDesktop, DispatcherPriority.Loaded);
        Workspace.SizeChanged += (_, e) => { netButton.IsVisible = e.NewSize.Width >= 1250; profile.IsVisible = e.NewSize.Width >= 1100; bgCopy.IsVisible = e.NewSize.Width >= 1150; foreach (var frame in Frames) frame.Constrain(); };
        AddHandler(KeyDownEvent, OnGlobalKey, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) => { if (_launcher is not null && e.Source is Visual source) { var ancestors = source.GetSelfAndVisualAncestors().ToArray(); if (!ancestors.Contains(_launcher) && !ancestors.OfType<Control>().Any(x => x.Classes.Contains("launcher-toggle"))) CloseLauncher(); } }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => { CloseLauncher(); CloseSpotlight(); };
        AddHandler(PointerPressedEvent, (_, e) => { if (_spotlight is not null && e.Source is Visual source && !source.GetSelfAndVisualAncestors().Contains(_spotlight)) CloseSpotlight(); }, RoutingStrategies.Tunnel);
        Closing += (_, e) => { if (_allowExit) return; if (Frames.Any(x => x.AppContent is TiledView { TileCount: > 0 })) { e.Cancel = true; ShowPower(); } else if (Frames.Any(x => x.AppContent is EditorView { IsDirty: true })) { e.Cancel = true; Dispatcher.UIThread.Post(RequestExit); } };
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
        foreach (var frame in Frames) frame.UpdateResizeHandles();
        _backdrop.Palette = State.Preferences.Wallpaper; _backdrop.Scanlines = State.Preferences.Scanlines; _backdrop.InvalidateVisual();
        UpdateWallpaper();
        _employee.Text = $"{State.Preferences.Employee}  /  HMR";
        _greeting.Text = $"Welcome back,\n{State.Preferences.Employee}";
        _network.Text = NetworkInterface.GetIsNetworkAvailable() ? "●  NETWORK ONLINE" : "○  NETWORK OFFLINE";
    }
    public void OpenApp(string id)
    {
        if (IsLocked) return;
        CloseSpotlight();
        CloseLauncher();
        var app = AppCatalog.All.FirstOrDefault(x => x.Id == id); if (app is null) return;
        Control content = id switch
        {
            "refinement" => new RefinementView(State), "terminal" => new TerminalView(State, OpenApp),
            "files" => new FilesView(State, Toast, OpenTerminalAt, OpenMedia, OpenText), "browser" => new IntranetView(State), "notes" => new NotesView(State, Toast),
            "sudoku" => new SudokuView(State), "hacker" => new HackerView(), "editor" => new EditorView(), "web" => new BrowserView(), "media" => new MediaView(), "calendar" => new CalendarView(State),
            "tiled" => new TiledView(Toast),
            "settings" => new SettingsView(State, ToggleFullscreen, Toast), "control" => new ControlView(State, Toast),
            "network" => new NetworkView(State), "wellness" => new WellnessView(State), "handbook" => new HandbookView(),
            _ => new TextBlock()
        };
        var size = id switch { "refinement" => new Size(Workspace.Bounds.Width >= 1320 ? Math.Clamp(Workspace.Bounds.Width - 676, 760, 940) : 826, 568), "terminal" => new Size(640, 440), "files" => new Size(1000, 650), "tiled" => new Size(1100, 670), "settings" => new Size(770, 680), "sudoku" => new Size(640, 710), "calendar" => new Size(900, 640), "web" => new Size(1100, 700), "media" => new Size(960, 640), "wellness" => new Size(680, 568), "network" => new Size(860, 650), _ => new Size(790, 570) };
        var instance = _instanceCounts.GetValueOrDefault(id) + 1; _instanceCounts[id] = instance;
        var frame = new AppFrame(this, app, content, Math.Min(size.Width, Math.Max(440, Workspace.Bounds.Width - 40)), Math.Min(size.Height, Math.Max(300, Workspace.Bounds.Height - 36)), instance);
        Frames.Add(frame); Workspace.Children.Add(frame);
        double x = id == "refinement" ? 156 : id == "terminal" && Frames.Count == 2 ? Workspace.Bounds.Width - frame.Width - 32 : (Workspace.Bounds.Width - frame.Width) / 2 + (Frames.Count % 3) * 15;
        double y = id == "refinement" ? 52 : id == "terminal" && Frames.Count == 2 ? Workspace.Bounds.Height - frame.Height - 40 : 75 + (Frames.Count % 4) * 24;
        if (instance > 1) { x += ((instance - 1) % 6) * 28; y += ((instance - 1) % 6) * 28; }
        frame.SetPosition(x, y); FocusFrame(frame); Motion.Enter(frame, 22); UpdateTaskbar(); State.Record($"Opened {app.Name}");
    }
    public void FocusFrame(AppFrame active) { _active = active; active.ZIndex = ++_z; foreach (var frame in Frames) frame.SetActive(frame == active); UpdateTaskbar(); SyncHostedWindows(); }
    public void RemoveFrame(AppFrame frame) { (frame.AppContent as IDisposable)?.Dispose(); Frames.Remove(frame); Workspace.Children.Remove(frame); if (Frames.LastOrDefault(x => !x.IsMinimized) is { } next) FocusFrame(next); UpdateTaskbar(); }
    public void UpdateTaskbar()
    {
        _tasks.Children.Clear();
        foreach (var frame in Frames)
        {
            var app = AppCatalog.All.First(x => x.Id == frame.AppId);
            var b = Ui.Button("", () => { if (frame.IsMinimized) frame.Restore(); else FocusFrame(frame); });
            b.Name = "TaskbarApp"; b.Tag = frame; b.ContextMenu = CreateTaskbarMenu(frame);
            if (frame.AppContent is MediaView media) FileDrop.Attach(b, paths => paths.Length == 1 && MediaView.Supports(paths[0]), paths => { frame.Restore(); media.OpenFile(paths[0]); });
            else if (frame.AppContent is EditorView editor) FileDrop.Attach(b, paths => paths.Length == 1 && File.Exists(paths[0]), paths => { frame.Restore(); editor.OpenDroppedFile(paths[0]); });
            else if (frame.AppContent is FilesView files) FileDrop.Attach(b, paths => paths.Length > 0, paths => { frame.Restore(); files.Select(true); files.Local.CopyDroppedFiles(paths, files.Local.CurrentDirectory); });
            b.Content = Ui.Row(8, new Glyph(app.Symbol) { Width = 16, Height = 16 }, Ui.Text(frame.DisplayName.Replace("Refinement", "Refinement"), 11));
            b.BorderThickness = new Thickness(0, 0, 0, 2); b.BorderBrush = frame.IsMinimized ? Brushes.Transparent : Brush.Parse("#54796A");
            b.Background = frame == _active && !frame.IsMinimized ? Brush.Parse("#D4DECE") : Brushes.Transparent;
            if (Frames.Count > 5) b.Content = new Glyph(app.Symbol) { Width = 16, Height = 16 }; ToolTip.SetTip(b, frame.DisplayName);
            _tasks.Children.Add(b);
        }
        RefreshWindowMenu(); SyncHostedWindows();
    }
    private ContextMenu CreateTaskbarMenu(AppFrame? frame = null)
    {
        var menu = new ContextMenu { MinWidth = 225, Placement = frame is null ? PlacementMode.Pointer : PlacementMode.TopEdgeAlignedLeft, VerticalOffset = -8 };
        void Populate()
        {
            menu.Items.Clear();
            void Add(string label, string icon, Action action, bool enabled = true)
            { var item = new MenuItem { Header = label, Icon = new Glyph(icon) { Width = 15, Height = 15 }, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
            if (frame is not null)
            {
                Add(frame.IsMinimized ? "Restore" : "Bring to front", "arrow", frame.Restore);
                Add("Minimize", "minimize", frame.Minimize, !frame.IsMinimized);
                Add(frame.IsMaximized ? "Restore size" : "Maximize", "maximize", () => { frame.Restore(); frame.ToggleMaximize(); });
                menu.Items.Add(new Separator());
                Add("New window", "plus", () => OpenApp(frame.AppId));
                Add("Close window", "close", frame.Close);
            }
            else
            {
                Add("Search · Alt+Space", "search", ToggleSpotlight);
                Add("Applications", "refinement", ToggleLauncher);
                Add(Frames.Any(x => !x.IsMinimized) ? "Show desktop" : "Restore windows", "minimize", ShowDesktop, Frames.Count > 0);
                menu.Items.Add(new Separator());
                Add("Tile windows", "refinement", TileWindows, Frames.Count > 0);
                Add("Cascade windows", "maximize", CascadeWindows, Frames.Count > 0);
                menu.Items.Add(new Separator());
                Add("Settings", "settings", () => OpenApp("settings"));
            }
        }
        Populate();
        menu.Opened += (_, _) => { Populate(); _taskbarMenuOpen = true; SyncHostedWindows(); };
        menu.Closed += (_, _) => { _taskbarMenuOpen = false; SyncHostedWindows(); };
        return menu;
    }
    public void ShowDesktop() { if (Frames.Any(x => !x.IsMinimized)) foreach (var f in Frames) f.Minimize(); else foreach (var f in Frames) f.Restore(); }
    public void ToggleFullscreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    private void OnGlobalKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        if (IsLocked) return;
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { ToggleSpotlight(); e.Handled = true; return; }
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { ToggleLauncher(); e.Handled = true; }
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Source is not TerminalSurface && e.Source is not TextBox && !(e.Source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<LocalFilesView>().Any())) { Lock(); e.Handled = true; }
        if (e.Key == Key.Escape && _launcher is not null) { CloseLauncher(); e.Handled = true; }
    }
    public void ToggleLauncher()
    {
        CloseSpotlight();
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
        _layers.Children.Add(_launcher); SyncHostedWindows(); Motion.Enter(_launcher, 12); search.Focus();
    }
    private void CloseLauncher() { if (_launcher is null) return; var panel = _launcher; _launcher = null; Motion.Animate(120, t => panel.Opacity = 1 - t, () => { _layers.Children.Remove(panel); SyncHostedWindows(); }); }
    public void Lock()
    {
        if (IsLocked) return; CloseSpotlight(); CloseLauncher(); _desktopRoot.IsEnabled = false;
        var title = new TextBlock { Text = "Your work will be here.", FontFamily = Ui.Serif, FontSize = 48, Foreground = Ui.Cream, HorizontalAlignment = HorizontalAlignment.Center };
        var resume = Ui.Button("Resume session  →", Unlock, "primary"); resume.HorizontalAlignment = HorizontalAlignment.Center;
        var content = Ui.Stack(28, new CorporateSeal { Width = 110, Height = 110, HorizontalAlignment = HorizontalAlignment.Center }, title, Ui.Label($"{State.Preferences.Employee.ToUpperInvariant()}  /  DATA REFINEMENT", Ui.Phosphor), resume, Ui.Text("F11  Fullscreen    ·    Alt+F4  Exit", 11, Ui.Muted, true));
        foreach (var c in content.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center; content.HorizontalAlignment = HorizontalAlignment.Center;
        _lock = new Border { Background = Ui.Dark, Child = content }; _layers.Children.Add(_lock); SyncHostedWindows(); Motion.Enter(_lock, 0); resume.Focus();
    }
    public void Unlock() { if (_lock is null) return; var screen = _lock; _lock = null; _desktopRoot.IsEnabled = true; Motion.Animate(330, t => screen.Opacity = 1 - t, () => { _layers.Children.Remove(screen); SyncHostedWindows(); }); }
    private void ShowPower()
    {
        if (_overlayOpen) return;
        var overlay = new Border { Background = Brush.Parse("#B0102E34") };
        var panel = Ui.Stack(20, Ui.Heading("SESSION CONTROL", "The day is done.", "Preferences and refinement progress are saved. Save any text edits before leaving. Terminal sessions will end; tiled apps return to Windows."));
        var cancel = Ui.Button("Return to work", () => { _layers.Children.Remove(overlay); _overlayOpen = false; SyncHostedWindows(); }, "outline");
        panel.Children.Add(Ui.Row(12, cancel, Ui.Button("End session", () => { _layers.Children.Remove(overlay); _overlayOpen = false; SyncHostedWindows(); RequestExit(); }, "primary")));
        overlay.Child = new Border { Width = 475, Padding = new Thickness(36), Background = Ui.Cream, Child = panel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; _layers.Children.Add(overlay); _overlayOpen = true; SyncHostedWindows(); Motion.Enter(overlay, 0); cancel.Focus();
    }
    public void Toast(string message)
    {
        if (_toast is not null) _layers.Children.Remove(_toast);
        var text = Ui.Text(message, 12); text.Name = "NotificationText"; text.TextWrapping = TextWrapping.Wrap;
        var toast = new Border { Name = "Notification", Width = 470, Padding = new Thickness(20, 15), Background = Ui.Cream, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 24, 24, 76) };
        var dismiss = Ui.IconButton("close", "Dismiss notification", () => _layers.Children.Remove(toast)); dismiss.VerticalAlignment = VerticalAlignment.Top; dismiss.Margin = new Thickness(12, 0, 0, 0);
        toast.Child = Ui.Columns("*,Auto", text, dismiss);
        _toast = toast; _layers.Children.Add(toast); Motion.Enter(toast);
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(message.Length / 15d, 4, 20)) }; delay.Tick += (_, _) => { if (toast.IsPointerOver) return; delay.Stop(); Motion.Animate(180, t => toast.Opacity = 1 - t, () => _layers.Children.Remove(toast)); }; delay.Start();
    }
    private void RequestExit()
    {
        var pending = new Queue<AppFrame>(Frames.Where(x => x.AppContent is EditorView { IsDirty: true }));
        void Next() { if (pending.TryDequeue(out var frame)) { if (IsLocked) Unlock(); frame.Restore(); FocusFrame(frame); ((EditorView)frame.AppContent).ConfirmClose(Next); } else { _allowExit = true; State.Save(); Close(); } }
        Next();
    }
    private Menu BuildMenu()
    {
        var menu = new Menu { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        var workspace = new MenuItem { Header = "Workspace" }; var system = new MenuItem { Header = "System" };
        void Add(MenuItem parent, string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); parent.Items.Add(item); }
        Add(workspace, "Search    Alt+Space", ToggleSpotlight); Add(workspace, "Application directory    Ctrl+Space", ToggleLauncher); Add(workspace, "New PowerShell terminal", () => OpenTerminalAt(LocalFiles.Home)); Add(workspace, "File Explorer", () => OpenApp("files")); Add(workspace, "tiled · Windows applications", () => OpenApp("tiled")); Add(workspace, "Show desktop", ShowDesktop);
        Add(system, "HammerOS preferences", () => OpenApp("settings")); Add(system, "Network connections", () => OpenApp("network")); Add(system, "Windows Settings", () => OpenExternal("ms-settings:")); Add(system, "Windows Task Manager", () => OpenExternal("taskmgr.exe")); Add(system, "Windows Explorer", () => OpenExternal(LocalFiles.Home)); system.Items.Add(new Separator()); Add(system, "Lock workspace", Lock); Add(system, "End HammerOS session", ShowPower);
        menu.Items.Add(workspace); menu.Items.Add(_windowMenu); menu.Items.Add(system); return menu;
    }
    private void RefreshWindowMenu()
    {
        _windowMenu.Items.Clear();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); _windowMenu.Items.Add(item); }
        Add("Tile HammerOS windows", TileWindows); Add("Cascade windows", CascadeWindows); Add("Toggle fullscreen    F11", ToggleFullscreen); _windowMenu.Items.Add(new Separator());
        foreach (var frame in Frames) Add((frame.IsMinimized ? "○  " : frame == _active ? "●  " : "   ") + frame.DisplayName, frame.Restore);
    }
    public void OpenTerminalAt(string directory)
    { if (IsLocked) return; OpenApp("terminal"); if (Frames.LastOrDefault(x => x.AppId == "terminal")?.AppContent is TerminalView view) view.AddHost(directory); }
    public void OpenMedia(string path)
    { if (IsLocked) return; OpenApp("media"); if (Frames.LastOrDefault(x => x.AppId == "media")?.AppContent is MediaView view) view.OpenFile(path); }
    public async void OpenText(string path)
    { if (IsLocked) return; OpenApp("editor"); if (Frames.LastOrDefault(x => x.AppId == "editor")?.AppContent is EditorView view) await view.LoadFileAsync(path); }
    public void TileWindows()
    {
        var list = Frames.Where(x => !x.IsMinimized).ToArray(); if (list.Length == 0) return;
        var cols = Math.Min(3, (int)Math.Ceiling(Math.Sqrt(list.Length))); var rows = (int)Math.Ceiling(list.Length / (double)cols); var w = Workspace.Bounds.Width / cols; var h = Workspace.Bounds.Height / rows;
        for (var i = 0; i < list.Length; i++) list[i].ArrangeTo(new Rect(i % cols * w + 5, i / cols * h + 5, Math.Max(100, w - 10), Math.Max(100, h - 10)));
    }
    public void CascadeWindows()
    { var i = 0; foreach (var frame in Frames) { frame.ArrangeTo(new Rect(100 + i * 28, 25 + i * 25, Math.Min(820, Workspace.Bounds.Width - 140), Math.Min(570, Workspace.Bounds.Height - 60))); i = (i + 1) % 6; } }
    private void SyncHostedWindows()
    { UpdateWallpaper(); foreach (var frame in Frames) if (frame.AppContent is INativeWorkspace native) native.SetWorkspaceActive(frame == _active && !frame.IsMinimized && !IsLocked && _launcher is null && _spotlight is null && !_overlayOpen && !_taskbarMenuOpen); }
    private void UpdateWallpaper() => _backdrop.Configure(State.Preferences.WallpaperScene, State.Preferences.Motion && State.Preferences.WallpaperAnimation && !IsLocked && WindowState != WindowState.Minimized);
    public async void ToggleSpotlight()
    {
        if (IsLocked || _overlayOpen) return;
        if (_spotlight is not null) { CloseSpotlight(); return; }
        CloseLauncher(); var palette = new CommandPalette(SearchCommands, CloseSpotlight); _spotlight = palette; _layers.Children.Add(palette); SyncHostedWindows(); Motion.Enter(palette, -8); palette.Search.Focus();
        _windowsAppsLoading ??= WindowsApplications.LoadAsync();
        _windowsApps = await _windowsAppsLoading; if (_spotlight == palette) palette.Refresh();
    }
    private void CloseSpotlight()
    { if (_spotlight is not { } palette) return; _spotlight = null; _layers.Children.Remove(palette); SyncHostedWindows(); _active?.Focus(); }
    public IEnumerable<PaletteAction> SearchCommands(string query)
    {
        query = query.Trim();
        if (query.StartsWith('>') || query.StartsWith("run ", StringComparison.OrdinalIgnoreCase))
        {
            var command = query[(query.StartsWith('>') ? 1 : 4)..].Trim();
            if (command.Length > 0) yield return new("Run in PowerShell", command + "  ·  Real terminal", "terminal", "", () => RunCommand(command));
            yield break;
        }
        var switching = query.StartsWith("switch ", StringComparison.OrdinalIgnoreCase);
        var term = switching ? query[7..].Trim() : query.StartsWith("open ", StringComparison.OrdinalIgnoreCase) ? query[5..].Trim() : query;
        bool Match(string value) => term.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));
        var actions = new List<PaletteAction>();
        foreach (var frame in Frames.OrderByDescending(x => x.ZIndex)) actions.Add(new("Switch to " + frame.DisplayName, frame.IsMinimized ? "Open window · minimized" : "Open window", AppCatalog.All.First(x => x.Id == frame.AppId).Symbol, frame.AppId, frame.Restore));
        if (!switching)
        {
            foreach (var app in AppCatalog.All) actions.Add(new(app.Name, "Open HammerOS app · " + app.Description, app.Symbol, app.Id, () => OpenApp(app.Id)));
            void Add(string title, string detail, string icon, Action run) => actions.Add(new(title, detail, icon, "", run));
            Add("Toggle fullscreen", "Switch between fullscreen and windowed · F11", "maximize", ToggleFullscreen);
            Add("Toggle smooth motion", "Switch animations " + (State.Preferences.Motion ? "off" : "on"), "settings", () => { State.Preferences.Motion = !State.Preferences.Motion; State.Save(); });
            Add("Toggle CRT texture", "Switch the desktop scanline texture", "settings", () => { State.Preferences.Scanlines = !State.Preferences.Scanlines; State.Save(); });
            Add("Toggle edge resizing", "Switch window edge resize handles", "settings", () => { State.Preferences.EdgeResize = !State.Preferences.EdgeResize; State.Save(); });
            Add("Show desktop", "Minimize or restore all windows", "minimize", ShowDesktop);
            Add("Tile windows", "Arrange HammerOS windows in a grid", "refinement", TileWindows);
            Add("Cascade windows", "Stack HammerOS windows", "maximize", CascadeWindows);
            Add("Lock workspace", "Keep your work open", "lock", Lock);
            foreach (var app in _windowsApps) actions.Add(new(app.Name, "Windows app · opens outside HammerOS", "maximize", "windows", () => OpenExternal(app.Shortcut)));
        }
        foreach (var action in actions.Where(x => Match(x.Title + " " + x.Keywords)).OrderByDescending(x => x.Title.Equals(term, StringComparison.OrdinalIgnoreCase)).ThenByDescending(x => x.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))) yield return action;
        if (!switching && query.Length > 0) yield return new("Run in PowerShell", query + "  ·  Real terminal", "terminal", "", () => RunCommand(query));
    }
    private void RunCommand(string command)
    { if (IsLocked) return; OpenApp("terminal"); if (Frames.Last().AppContent is TerminalView view) { var tab = view.AddHost(initialCommand: command); view.RenameTab(tab, command); } }
    private void OpenExternal(string target)
    { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Toast(ex.Message); } }
}
