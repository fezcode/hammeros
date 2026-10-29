using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HammerOS.Apps;
using HammerOS.Core;

namespace HammerOS.Desktop;

public sealed partial class ShellWindow
{
    public int CurrentDesktop => State.Workspace.Desktop;
    public void OnFrameMinimized(AppFrame frame)
    { if (_active == frame && Frames.Where(x => x != frame && x.DesktopId == CurrentDesktop && !x.IsMinimized).MaxBy(x => x.ZIndex) is { } next) FocusFrame(next); else UpdateTaskbar(); }
    public TransferQueue Transfers { get; } = new();
    public List<string> ClipboardItems { get; } = [];
    private readonly Button _notificationsButton = new();
    private readonly DispatcherTimer _workspaceTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource _searchCancellation = new();
    private IReadOnlyList<string> _fileIndex = [];
    private DateTime _indexedAt;
    private bool _indexing, _polling, _restoring;
    private Border? _overview;
    private string _lastSession = "";
    private string? _lastClipboard;
    private readonly HashSet<TransferJob> _reportedJobs = [];
    private void InitializeWorkspace()
    {
        _notificationsButton.Click += (_, _) => OpenApp("notifications");
        Transfers.Changed += () => { foreach (var job in Transfers.Jobs.Where(x => x.Finished)) if (_reportedJobs.Add(job)) RecordNotification(job.Label + " · " + job.Status, "notifications"); };
        _workspaceTimer.Tick += async (_, _) =>
        {
            if (_polling || IsLocked) return; _polling = true;
            try
            {
                if (State.Persistent && _started) SaveSession();
                if (State.Workspace.ClipboardHistory && Clipboard is { } clipboard && await clipboard.GetTextAsync() is { Length: > 0 and <= 100000 } value && _lastClipboard != value)
                { _lastClipboard = value; ClipboardItems.Remove(value); ClipboardItems.Insert(0, value); if (ClipboardItems.Count > 30) ClipboardItems.RemoveAt(30); }
            }
            catch (Exception) { /* Clipboard may be temporarily owned by another process. */ }
            finally { _polling = false; }
        };
        _workspaceTimer.Start();
    }
    public void RecordNotification(string message, string? app = null, string? path = null)
    {
        State.Workspace.Notifications.Insert(0, new() { Message = message, App = app, Path = path });
        State.Workspace.Notifications = State.Workspace.Notifications.Take(100).ToList(); State.Save();
    }
    public void SwitchDesktop(int desktop)
    {
        desktop = (desktop % State.Workspace.Desktops.Count + State.Workspace.Desktops.Count) % State.Workspace.Desktops.Count;
        State.Workspace.Desktop = desktop;
        foreach (var frame in Frames) frame.IsVisible = frame.DesktopId == desktop && !frame.IsMinimized;
        _active = Frames.Where(x => x.DesktopId == desktop && !x.IsMinimized).MaxBy(x => x.ZIndex);
        foreach (var frame in Frames) frame.SetActive(frame == _active);
        UpdateTaskbar(); ResolveOverlaps(); State.Save(false);
    }
    public void MoveToDesktop(AppFrame frame, int desktop)
    { frame.DesktopId = Math.Clamp(desktop, 0, State.Workspace.Desktops.Count - 1); SwitchDesktop(CurrentDesktop); }
    private bool HandleWorkspaceShortcut(KeyEventArgs e)
    {
        foreach (var (action, gesture) in State.Workspace.Shortcuts)
        {
            KeyGesture key; try { key = KeyGesture.Parse(gesture); } catch { continue; }
            if (!key.Matches(e)) continue;
            switch (action) { case "Search": ToggleSpotlight(); break; case "Applications": ToggleLauncher(); break; case "Next desktop": SwitchDesktop(CurrentDesktop + 1); break; case "Previous desktop": SwitchDesktop(CurrentDesktop - 1); break; case "Overview": ShowOverview(); break; default: continue; }
            e.Handled = true; return true;
        }
        if (e.Key == Key.Escape && _overview is not null) { CloseOverview(); e.Handled = true; return true; } return false;
    }
    public Action? NativeShortcut(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F11) return ToggleFullscreen; if (IsLocked) return null;
        foreach (var (action, gesture) in State.Workspace.Shortcuts)
        {
            KeyGesture parsed; try { parsed = KeyGesture.Parse(gesture); } catch { continue; }
            if (parsed.Key != key || parsed.KeyModifiers != modifiers) continue;
            return action switch { "Search" => ToggleSpotlight, "Applications" => ToggleLauncher, "Next desktop" => () => SwitchDesktop(CurrentDesktop + 1), "Previous desktop" => () => SwitchDesktop(CurrentDesktop - 1), "Overview" => ShowOverview, _ => null };
        }
        return null;
    }
    private void CloseOverview() { if (_overview is null) return; _layers.Children.Remove(_overview); _overview = null; _overlayOpen = false; SyncHostedWindows(); }
    public void ShowOverview()
    {
        if (IsLocked) return; if (_overview is not null) { CloseOverview(); return; } if (_overlayOpen) return;
        CloseLauncher(); CloseSpotlight();
        var columns = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < State.Workspace.Desktops.Count; i++)
        {
            var index = i; var list = Ui.Stack(10);
            var name = new TextBox { Text = State.Workspace.Desktops[i], MaxLength = 30 };
            name.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { State.Workspace.Desktops[index] = name.Text.Trim(); State.Save(false); } };
            list.Children.Add(name); list.Children.Add(Ui.Button(index == CurrentDesktop ? "Current workspace" : "Switch here", () => { CloseOverview(); SwitchDesktop(index); }, "primary"));
            foreach (var frame in Frames.Where(x => x.DesktopId == index).OrderByDescending(x => x.ZIndex))
            {
                var destination = new ComboBox { ItemsSource = State.Workspace.Desktops, SelectedIndex = index, Width = 120 };
                destination.SelectionChanged += (_, _) => { if (destination.SelectedIndex >= 0) { MoveToDesktop(frame, destination.SelectedIndex); Dispatcher.UIThread.Post(() => { if (_overview is not null) { CloseOverview(); ShowOverview(); } }); } };
                list.Children.Add(Ui.Stack(4, Ui.Button(frame.DisplayName + (frame.IsMinimized ? " · minimized" : ""), () => { CloseOverview(); frame.Restore(); }), Ui.Row(4, Ui.Label("MOVE TO"), destination)));
            }
            if (!Frames.Any(x => x.DesktopId == index)) list.Children.Add(Ui.Text("Room for something new.", 13, Ui.Muted));
            columns.Children.Add(new Border { Width = 270, Margin = new Thickness(8), Padding = new Thickness(18), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = list });
        }
        var body = Ui.Stack(18, Ui.Columns("*,Auto", Ui.Heading("WORKSPACES", "A place for every task.", "Switch windows or move them between desktops."), Ui.Button("Close", CloseOverview)), columns,
            Ui.Row(8, Ui.Button("Add workspace", () => { if (State.Workspace.Desktops.Count >= 9) return; State.Workspace.Desktops.Add("Workspace " + (State.Workspace.Desktops.Count + 1)); CloseOverview(); ShowOverview(); }), Ui.Button("Profiles & shortcuts", () => { CloseOverview(); OpenApp("workspace"); })));
        _overview = new Border { Background = Ui.Dark, Padding = new Thickness(36), Child = new Border { Background = Ui.Cream, Padding = new Thickness(24), Child = Ui.Scroll(body) } };
        _layers.Children.Add(_overview); _overlayOpen = true; SyncHostedWindows(); Motion.Enter(_overview, 12);
    }
    public List<WindowStateRecord> CaptureWindows(bool includeDrafts = true)
    {
        return Frames.Select(frame =>
        {
            var r = frame.RestoreBounds; var record = new WindowStateRecord { App = frame.AppId, Desktop = frame.DesktopId, X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Minimized = frame.IsMinimized, Maximized = frame.IsMaximized };
            switch (frame.AppContent)
            {
                case EditorView editor: editor.Capture(record, includeDrafts); break;
                case FilesView files: record.Tabs = files.TabPaths.ToList(); record.ActiveTab = files.ActiveIndex; break;
                case BrowserView browser: record.Tabs = browser.TabUris.ToList(); record.ActiveTab = browser.ActiveIndex; break;
                case TerminalView terminal: record.ActiveTab = terminal.Tabs.IndexOf(terminal.ActiveTab!); record.Terminals = terminal.Tabs.Select(t => new TerminalStateRecord { Kind = t.Kind, Title = t.Title, Directory = (t.Content as HostTerminalView)?.WorkingDirectory }).ToList(); break;
                case MediaView media: record.Path = media.CurrentFile; break;
                case ImageView image: record.Path = image.FilePath; break;
                case PdfView pdf: record.Path = pdf.FilePath; break;
                case ArchiveView archive: record.Path = archive.FilePath; break;
            }
            return record;
        }).ToList();
    }
    public void SaveSession()
    {
        if (_restoring) return;
        var windows = CaptureWindows(); var json = System.Text.Json.JsonSerializer.Serialize(windows);
        if (_lastSession == json) return; State.Workspace.Windows = windows; State.Save(false); if (State.StorageWarning is null) _lastSession = json;
    }
    public async void RestoreWindows(IEnumerable<WindowStateRecord> records, bool profile) => await RestoreWindowsAsync(records, profile);
    public async Task RestoreWindowsAsync(IEnumerable<WindowStateRecord> records, bool profile = false)
    {
        if (_restoring) return; _restoring = true; var desktop = CurrentDesktop;
        try
        {
            foreach (var record in records.Take(50).ToArray())
            {
                if (!AppCatalog.All.Any(x => x.Id == record.App)) continue;
                SwitchDesktop(Math.Clamp(record.Desktop, 0, State.Workspace.Desktops.Count - 1)); OpenApp(record.App); var frame = Frames.Last();
                frame.FitTo(new Rect(double.IsFinite(record.X) ? record.X : 40, double.IsFinite(record.Y) ? record.Y : 40, double.IsFinite(record.Width) ? Math.Clamp(record.Width, 320, 10000) : 800, double.IsFinite(record.Height) ? Math.Clamp(record.Height, 220, 10000) : 560)); frame.Constrain();
                switch (frame.AppContent)
                {
                    case EditorView editor: await editor.RestoreDocument(record); break;
                    case FilesView files: files.RestoreTabs(record.Tabs, record.ActiveTab); break;
                    case BrowserView browser: browser.RestoreTabs(record.Tabs, record.ActiveTab); break;
                    case TerminalView terminal: terminal.RestoreTabs(record.Terminals, record.ActiveTab); break;
                    case MediaView media when File.Exists(record.Path): media.OpenFile(record.Path!); break;
                    case ImageView image when File.Exists(record.Path): image.Load(record.Path!); break;
                    case PdfView pdf when File.Exists(record.Path): pdf.Load(record.Path!); break;
                    case ArchiveView archive when File.Exists(record.Path): archive.Load(record.Path!); break;
                }
                // Restore geometry without an asynchronous transition racing the desktop switch.
                var motion = Motion.Enabled; Motion.Enabled = false;
                try { if (record.Maximized) frame.ToggleMaximize(); if (record.Minimized) frame.Minimize(); } finally { Motion.Enabled = motion; }
            }
        }
        catch (Exception ex) { Toast("Some workspace items could not be restored: " + ex.Message); }
        finally { SwitchDesktop(desktop); _restoring = false; }
        if (profile) Toast("Profile opened. Existing windows were kept.");
    }
    public async Task OpenResource(string path, string? application = null)
    {
        if (IsLocked) return;
        try
        {
            path = Path.GetFullPath(path);
            if (Directory.Exists(path)) { OpenApp("files"); ((FilesView)Frames.Last().AppContent).Local.Navigate(path); return; }
            if (!File.Exists(path)) throw new FileNotFoundException("This file is no longer available.", path);
            application ??= State.Workspace.Associations.GetValueOrDefault(Path.GetExtension(path));
            application ??= MediaView.Supports(path) ? "media" : ImageView.Supports(path) ? "image" : Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? "archive" : await TextFiles.IsDocumentAsync(path) ? "editor" : "windows";
            if (application == "windows") LocalFiles.Open(path);
            else
            {
                if (application == "web") { Toast("Local documents open in their registered Windows browser."); LocalFiles.Open(path); }
                else
                {
                    OpenApp(application); switch (Frames.Last().AppContent) { case EditorView editor: await editor.LoadFileAsync(path); break; case MediaView media: media.OpenFile(path); break; case ImageView image: image.Load(path); break; case PdfView pdf: pdf.Load(path); break; case ArchiveView archive: archive.Load(path); break; default: throw new IOException("Choose a supported file application."); }
                }
            }
            State.Workspace.RecentFiles.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase)); State.Workspace.RecentFiles.Insert(0, path); State.Workspace.RecentFiles = State.Workspace.RecentFiles.Take(50).ToList(); State.Save(false);
        }
        catch (Exception ex) { Toast("Could not open file: " + ex.Message); }
    }
    public void ShowOpenWith(string path)
    {
        var flyout = new Flyout(); var list = Ui.Stack(8, Ui.Label("OPEN WITH"), Ui.Text(Path.GetFileName(path), 16)); var always = new CheckBox { Content = "Use for " + Path.GetExtension(path) + " files" }; list.Children.Add(always);
        foreach (var id in new[] { "editor", "media", "image", "pdf", "archive", "windows" }) list.Children.Add(Ui.Button(id == "windows" ? "Windows default" : AppCatalog.All.First(x => x.Id == id).Name, () => { if (always.IsChecked == true) { State.Workspace.Associations[Path.GetExtension(path)] = id; State.Save(false); } flyout.Hide(); _ = OpenResource(path, id); }));
        flyout.Content = list; flyout.ShowAt(_active ?? (Control)Workspace);
    }
    private async void CaptureScreen(Action<Bitmap> receive)
    {
        var frame = Frames.LastOrDefault(x => x.AppContent is ScreenshotView); frame?.Minimize();
        try { await Task.Delay(3000); if (frame is null || !Frames.Contains(frame)) return; if (!IsActive || IsLocked || WindowState == WindowState.Minimized) { Toast("Bring HammerOS to the front before capturing."); return; } var origin = this.PointToScreen(new Point()); receive(ScreenCapture.Capture(origin.X, origin.Y, (int)(Bounds.Width * RenderScaling), (int)(Bounds.Height * RenderScaling))); }
        catch (Exception ex) { Toast("Capture failed: " + ex.Message); }
        finally { if (frame is not null && Frames.Contains(frame)) frame.Restore(); }
    }
    private async void StartFileIndex()
    {
        if (_indexing || DateTime.UtcNow - _indexedAt < TimeSpan.FromMinutes(2)) return; _indexing = true;
        try { _fileIndex = await Task.Run(() => FileSearch.Build(new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(LocalFiles.Home, "Downloads") }.Concat(State.Workspace.Favorites).ToArray(), _searchCancellation.Token)); _indexedAt = DateTime.UtcNow; _spotlight?.Refresh(); }
        catch (OperationCanceledException) { } finally { _indexing = false; }
    }
    private IEnumerable<PaletteAction> WorkspaceSearch(string query)
    {
        if (Calculator.TryEvaluate(query.TrimStart('='), out var result)) yield return new(result, "Calculator · copy result", "control", "", async () => { if (Clipboard is { } cb) await cb.SetTextAsync(result); });
        foreach (var path in State.Workspace.RecentFiles.Concat(State.Workspace.Favorites).Concat(query.Length >= 2 ? _fileIndex : []).Distinct(StringComparer.OrdinalIgnoreCase).Where(x => Path.GetFileName(x).Contains(query, StringComparison.OrdinalIgnoreCase)).Take(30)) yield return new(Path.GetFileName(path), path, "folder", "file", () => _ = OpenResource(path));
        foreach (var command in State.Workspace.CommandHistory.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5)) yield return new(command, "Command history · run in a new terminal", "terminal", "history", () => RunCommand(command));
        if ("workspace overview desktops".Contains(query, StringComparison.OrdinalIgnoreCase)) yield return new("Workspace overview", "Switch desktops and move windows", "refinement", "", ShowOverview);
        for (var i = 0; i < State.Workspace.Desktops.Count; i++) { var index = i; var name = State.Workspace.Desktops[i]; if (("desktop " + name).Contains(query, StringComparison.OrdinalIgnoreCase)) yield return new("Desktop " + name, "Switch workspace", "maximize", "", () => SwitchDesktop(index)); }
    }
}
