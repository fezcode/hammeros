using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class FilesView : UserControl, IDisposable
{
    private readonly DepartmentFilesView _department;
    private readonly List<LocalFilesView> _tabs = [];
    public LocalFilesView Local => _tabs[ActiveIndex];
    public int ActiveIndex { get; private set; }
    public IReadOnlyList<string> TabPaths => _tabs.Select(x => x.CurrentDirectory).ToArray();
    private readonly ContentControl _content = new();
    private readonly StackPanel _strip = Ui.Row(3);
    private readonly Func<LocalFilesView> _create;
    private bool _host = true;
    public FilesView(SystemState state, Action<string> toast, Action<string>? terminalAt = null, Action<string>? playMedia = null, Action<string>? openText = null, Func<string, Task>? openResource = null, Action<string>? openWith = null, TransferQueue? transfers = null)
    {
        _department = new(state, toast); _create = () => new(toast, terminalAt, playMedia, openText, state, openResource, openWith, transfers);
        var root = new Grid { RowDefinitions = new("43,*") };
        root.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 3), Child = Ui.Columns("*,Auto", new ScrollViewer { Content = _strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, Ui.Row(3, Ui.Button("+", () => AddTab()), Ui.Button("Department", () => Select(false)))) });
        Grid.SetRow(_content, 1); root.Children.Add(_content); Content = root; AddTab();
        AddHandler(KeyDownEvent, (_, e) => { if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.T) { AddTab(Local.CurrentDirectory); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W) { CloseTab(ActiveIndex); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    public void AddTab(string? path = null) { var tab = _create(); tab.LocationChanged += RenderTabs; _tabs.Add(tab); ActiveIndex = _tabs.Count - 1; if (path is not null) tab.Navigate(path); Select(true); }
    public void CloseTab(int index) { if (_tabs.Count <= 1) return; _content.Content = null; _tabs[index].Dispose(); _tabs.RemoveAt(index); ActiveIndex = Math.Min(ActiveIndex, _tabs.Count - 1); Select(true); }
    public void Select(bool host) { _host = host; _content.Content = host ? Local : _department; RenderTabs(); Motion.Enter(_content, 4); }
    private void RenderTabs() { _strip.Children.Clear(); for (var i = 0; i < _tabs.Count; i++) { var index = i; var title = Path.GetFileName(_tabs[i].CurrentDirectory); if (string.IsNullOrEmpty(title)) title = _tabs[i].CurrentDirectory; var tab = WorkspaceTab.Create(title, _host && i == ActiveIndex, () => { ActiveIndex = index; Select(true); }, _tabs.Count > 1 ? () => CloseTab(index) : null); ToolTip.SetTip(tab, _tabs[i].CurrentDirectory); _strip.Children.Add(tab); } }
    public void RestoreTabs(IEnumerable<string> paths, int selected) { _content.Content = null; foreach (var tab in _tabs) tab.Dispose(); _tabs.Clear(); foreach (var path in paths.Where(Directory.Exists).Take(20)) AddTab(path); if (_tabs.Count == 0) AddTab(); ActiveIndex = Math.Clamp(selected, 0, _tabs.Count - 1); Select(true); }
    public void Navigate(string path) { Select(false); _department.Navigate(path); }
    public void Dispose() { foreach (var tab in _tabs) tab.Dispose(); _department.Dispose(); }
}

public sealed class LocalFilesView : UserControl, IDisposable
{
    private readonly Action<string> _toast;
    private readonly SystemState? _state;
    private readonly Func<string, Task>? _openResource;
    private readonly Action<string>? _openWith;
    private readonly TransferQueue? _transfers;
    private readonly StackPanel _breadcrumbs = Ui.Row(0);
    private readonly ContentControl _preview = new() { IsVisible = false, Width = 230 };
    private readonly StackPanel _favorites = Ui.Stack(3);
    private Action? _undo;
    public event Action? LocationChanged;
    public IReadOnlyList<LocalEntry> SelectedEntries => _list.SelectedItems?.OfType<LocalEntry>().ToArray() ?? [];

    private readonly Action<string>? _terminalAt;
    private readonly Action<string>? _playMedia;
    private readonly Action<string>? _openText;
    private readonly TextBox _address = new() { Name = "LocalAddress", FontSize = 12, MinWidth = 0 };
    private readonly TextBox _filter = new() { Name = "FileFilter", Watermark = "Search this folder", FontSize = 12, MinWidth = 0, Height = 36 };
    private readonly ListBox _list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle };
    private readonly TextBlock _status = Ui.Text("", 11, Ui.Muted);
    private readonly ContentControl _body = new();
    private readonly StackPanel _sidebar = Ui.Stack(4);
    private IReadOnlyList<LocalEntry> _entries = [];
    private string _sort = "Name";
    private bool _descending;
    private readonly Dictionary<string, TextBlock> _sortLabels = new();
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private CancellationTokenSource? _loading;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private static LocalEntry[]? _clipboard;
    private static bool _cut;
    private bool _disposed, _editing, _busy;
    private Point? _dragStart;
    private LocalEntry? _dragEntry;
    public string CurrentDirectory { get; private set; } = LocalFiles.Home;
    public Task Loading { get; private set; } = Task.CompletedTask;
    public LocalFilesView(Action<string> toast, Action<string>? terminalAt, Action<string>? playMedia = null, Action<string>? openText = null, SystemState? state = null, Func<string, Task>? openResource = null, Action<string>? openWith = null, TransferQueue? transfers = null)
    {
        _state = state; _openResource = openResource; _openWith = openWith; _transfers = transfers; _toast = toast; _terminalAt = terminalAt; _playMedia = playMedia; _openText = openText;
        var navTitle = Ui.Label("THIS COMPUTER"); navTitle.Margin = new Thickness(12, 8); _sidebar.Children.Add(navTitle);
        foreach (var (name, path) in new[] { ("Home", LocalFiles.Home), ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), ("Downloads", Path.Combine(LocalFiles.Home, "Downloads")) })
            AddPlace(name, path);
        _sidebar.Children.Add(Ui.Rule());
        foreach (var drive in DriveInfo.GetDrives()) AddPlace(drive.Name, drive.RootDirectory.FullName);
        _sidebar.Children.Add(Ui.Rule()); _sidebar.Children.Add(_favorites); RefreshFavorites(); _sidebar.Margin = new Thickness(9, 12);
        var side = new Border { Background = Brush.Parse("#DFE4D6"), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = Ui.Scroll(_sidebar) };
        var main = new Grid { RowDefinitions = new RowDefinitions("52,32,48,30,*,32") };
        var nav = Ui.Columns("Auto,*,Auto", Ui.Row(0, Ui.IconButton("back", "Back · Alt+Left", () => History(-1)), Ui.IconButton("arrow", "Forward · Alt+Right", () => History(1)), Ui.Button("↑", Up)), _address, Ui.IconButton("refresh", "Refresh · F5", () => Navigate(CurrentDirectory, false))); nav.Margin = new Thickness(10, 7); main.Children.Add(nav); var crumbs = new ScrollViewer { Content = _breadcrumbs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Grid.SetRow(crumbs, 1); main.Children.Add(crumbs);
        _filter.Classes.Add("search");
        var search = new Border { Height = 36, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Brush.Parse("#F3F2E8"), Padding = new Thickness(10, 0, 0, 0), Child = Ui.Columns("18,*", new Glyph("search") { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center }, _filter) };
        var create = Ui.Button("New ▾", () => { }); var createMenu = new ContextMenu();
        Menu(createMenu, "New folder", () => NameForm("New folder", "", name => LocalFiles.Create(CurrentDirectory, name, true)));
        Menu(createMenu, "New text file", () => NameForm("New text file", "untitled.txt", name => LocalFiles.Create(CurrentDirectory, name, false))); create.Click += (_, _) => createMenu.Open(create);
        var actions = Ui.Button("Actions ▾", () => { }); actions.Click += (_, _) => { actions.ContextMenu = BuildActions(); actions.ContextMenu.Open(actions); };
        var toolbar = Ui.Columns("Auto,*,220", Ui.Row(2, create, actions, Ui.Button("Preview", () => { _preview.IsVisible = !_preview.IsVisible; PreviewSelected(); })), new Border(), search); toolbar.Margin = new Thickness(12, 4); Grid.SetRow(toolbar, 2); main.Children.Add(toolbar);
        Button SortButton(string name) { var label = Ui.Label(name.ToUpperInvariant()); _sortLabels[name] = label; var b = Ui.Button("", () => SortBy(name)); b.Name = "Sort" + name; b.Content = label; b.Padding = new Thickness(0); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.VerticalContentAlignment = VerticalAlignment.Center; ToolTip.SetTip(b, "Sort by " + name.ToLowerInvariant() + " · click again to reverse"); return b; }
        var labels = Ui.Columns("*,85,90,120", SortButton("Name"), SortButton("Type"), SortButton("Size"), SortButton("Modified")); labels.Margin = new Thickness(25, 0, 18, 0); Grid.SetRow(labels, 3); main.Children.Add(labels);
        _list.ItemTemplate = new FuncDataTemplate<LocalEntry>((entry, _) =>
        {
            if (entry is null) return new Border();
            var name = Ui.Text(entry.Name, 12); name.TextTrimming = TextTrimming.CharacterEllipsis;
            var left = Ui.Columns("29,*", new Glyph(entry.IsDirectory ? "folder" : "notes") { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center }, name);
            var row = Ui.Columns("*,85,90,120", left, Ui.Text(entry.Kind, 10, Ui.Muted), Ui.Text(entry.SizeLabel, 11, Ui.Muted, true), Ui.Text(entry.Modified.ToString("dd MMM yyyy"), 10, Ui.Muted)); row.Height = 39;
            row.SizeChanged += (_, e) => CompactColumns(row, e.NewSize.Width);
            ToolTip.SetTip(row, entry.Path); return row;
        });
        _list.DoubleTapped += (_, _) => OpenSelected(); _list.SelectionChanged += (_, _) => { UpdateStatus(); PreviewSelected(); };
        _list.AddHandler(PointerPressedEvent, (_, e) => {
            if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
            _dragEntry = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().Select(x => x.DataContext).OfType<LocalEntry>().FirstOrDefault(); _dragStart = e.GetPosition(_list);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.PointerReleased += (_, _) => { _dragStart = null; _dragEntry = null; };
        _list.PointerMoved += async (_, e) => {
            if (_dragStart is not { } start || _dragEntry is not { } entry || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || (Math.Abs(e.GetPosition(_list).X - start.X) < 6 && Math.Abs(e.GetPosition(_list).Y - start.Y) < 6)) return;
            _dragStart = null; _dragEntry = null; try { await FileDrop.Start(_list, e, SelectedEntries.Any(x => x.Path == entry.Path) ? SelectedEntries.Select(x => x.Path).ToArray() : [entry.Path]); } catch (Exception ex) { _toast("Could not drag file: " + ex.Message); }
        };
        DragDrop.SetAllowDrop(_list, true);
        string DropDestination(DragEventArgs e) => (_list.InputHitTest(e.GetPosition(_list)) as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().Select(x => x.DataContext).OfType<LocalEntry>().FirstOrDefault() is { IsDirectory: true } folder ? folder.Path : CurrentDirectory;
        _list.AddHandler(DragDrop.DragOverEvent, (_, e) => { var paths = FileDrop.Paths(e.Data); var allowed = !_busy && paths.Length > 0; e.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None; _list.Classes.Set("file-drop", allowed); if (allowed) _status.Text = "Copy to " + DropDestination(e); e.Handled = true; });
        _list.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { _list.Classes.Remove("file-drop"); UpdateStatus(); });
        _list.AddHandler(DragDrop.DropEvent, (_, e) => { _list.Classes.Remove("file-drop"); var paths = FileDrop.Paths(e.Data); if (!_busy && paths.Length > 0) CopyDroppedFiles(paths, DropDestination(e)); e.Handled = true; });
        _list.ContextRequested += (_, e) => { BuildActions().Open(_list); e.Handled = true; };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key is Key.C or Key.X) { CopySelected(e.Key == Key.X); e.Handled = true; }
                if (e.Key == Key.V) { Paste(); e.Handled = true; }
            }
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } if (e.Key == Key.F2) { RenameSelected(); e.Handled = true; } if (e.Key == Key.Delete) { RecycleSelected(); e.Handled = true; }
        };
        _body.Content = _list; var pane = Ui.Columns("*,Auto", _body, _preview); Grid.SetRow(pane, 4); main.Children.Add(pane);
        var footer = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(17, 0), Child = _status }; Grid.SetRow(footer, 5); main.Children.Add(footer);
        labels.SizeChanged += (_, e) => CompactColumns(labels, e.NewSize.Width);
        var layout = Ui.Columns("154,*", side, main); Content = layout;
        SizeChanged += (_, e) => { var compact = e.NewSize.Width < 740; layout.ColumnDefinitions[0].Width = new GridLength(compact ? 112 : 154); toolbar.ColumnDefinitions[2].Width = new GridLength(compact ? 170 : 220); };
        _address.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await OpenPathAsync(_address.Text ?? ""); } };
        _filter.TextChanged += (_, _) => Filter();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.F6 || e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _address.Focus(); _address.SelectAll(); e.Handled = true; } if (e.Key == Key.F5) { Navigate(CurrentDirectory, false); e.Handled = true; } if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Left) { History(-1); e.Handled = true; } if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Right) { History(1); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _refresh.Tick += (_, _) => { _refresh.Stop(); if (!_editing && !_busy) Navigate(CurrentDirectory, false); };
        Navigate(LocalFiles.Home);
    }
    private void AddPlace(string name, string path) { if (string.IsNullOrEmpty(path)) return; var b = Ui.Button(name, () => Navigate(path)); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.FontSize = 12; _sidebar.Children.Add(b); }
    private static void Menu(ContextMenu menu, string title, Action action, bool enabled = true) { var item = new MenuItem { Header = title, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private ContextMenu BuildActions()
    {
        var menu = new ContextMenu(); var selected = _list.SelectedItem as LocalEntry;
        Menu(menu, "Open", OpenSelected, selected is not null); Menu(menu, "Open in Text Editor", () => { if (selected is null) return; if (_openText is not null) _openText(selected.Path); else EditSelected(); }, selected is { IsDirectory: false });
        Menu(menu, "Edit here (inline editor)", EditSelected, selected is { IsDirectory: false });
        Menu(menu, "Open with…", () => { if (selected is not null) _openWith?.Invoke(selected.Path); }, selected is { IsDirectory: false } && _openWith is not null);
        Menu(menu, "Favorite this folder", ToggleFavorite, _state is not null); Menu(menu, "Recent files", ShowRecent, _state is not null); Menu(menu, "Create ZIP from selection…", CreateArchive, selected is not null); Menu(menu, "Undo last rename", Undo, _undo is not null); Menu(menu, "Undo last transfer", async () => { try { if (_transfers is not null) await _transfers.UndoLast(); Navigate(CurrentDirectory, false); } catch (Exception ex) { _toast(ex.Message); } }, _transfers?.CanUndo == true);
        Menu(menu, "Open with Windows", () => { if (selected is not null) Try(() => LocalFiles.Open(selected.Path)); }, selected is { IsDirectory: false });
        Menu(menu, "Rename…", RenameSelected, selected is not null); Menu(menu, "Copy", () => CopySelected(false), selected is not null);
        Menu(menu, "Cut", () => CopySelected(true), selected is not null);
        Menu(menu, "Paste here", Paste, _clipboard is not null);
        Menu(menu, "Move to Recycle Bin…", RecycleSelected, selected is not null); menu.Items.Add(new Separator());
        Menu(menu, "Terminal here", () => _terminalAt?.Invoke(selected is { IsDirectory: true } ? selected.Path : CurrentDirectory), _terminalAt is not null);
        Menu(menu, "Open in Windows Explorer", () => Try(() => LocalFiles.Open(CurrentDirectory))); return menu;
    }
    public void Navigate(string path, bool history = true) => Loading = Load(path, history);
    public async void CopyDroppedFiles(string[] paths, string destination)
    {
        if (_transfers is not null) { await _transfers.Enqueue(paths, destination, false); Navigate(CurrentDirectory, false); return; }
        RunOperation(() => { foreach (var path in paths) { var directory = Directory.Exists(path); FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path); if (!info.Exists) throw new FileNotFoundException("The source file is no longer available.", path); LocalFiles.Transfer(new(info.FullName, info.Name, directory, directory ? 0 : ((FileInfo)info).Length, info.LastWriteTime, info.Attributes.HasFlag(FileAttributes.ReparsePoint)), destination, false); } }, $"Copied {paths.Length} item(s).");
    }
    private void CopySelected(bool cut) { if (SelectedEntries.Count == 0) return; _clipboard = SelectedEntries.ToArray(); _cut = cut; _status.Text = cut ? "Ready to move. Choose a folder and paste." : $"Copied {_clipboard.Length} item(s) to the HammerOS clipboard."; }
    private async void Paste() { if (_clipboard is not { } entries) return; var cut = _cut; var target = CurrentDirectory; if (_transfers is not null) { await _transfers.Enqueue(entries.Select(x => x.Path).ToArray(), target, cut); if (cut) _clipboard = null; Navigate(CurrentDirectory, false); } else RunOperation(() => { foreach (var entry in entries) LocalFiles.Transfer(entry, target, cut); }, "Transfer complete.", () => { if (cut && _clipboard == entries) _clipboard = null; }); }
    private async Task Load(string path, bool history)
    {
        _loading?.Cancel(); var cancellation = new CancellationTokenSource(); _loading = cancellation;
        _status.Text = "Reading folder…";
        try
        {
            var resolved = LocalFiles.Resolve(path); var entries = await Task.Run(() => LocalFiles.List(resolved, cancellation.Token));
            if (_disposed || cancellation.IsCancellationRequested) return;
            CurrentDirectory = resolved; UpdateBreadcrumbs(); LocationChanged?.Invoke(); _address.Text = resolved; _entries = entries; _editing = false; _body.Content = _list;
            if (history) { if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1); _history.Add(resolved); _historyIndex = _history.Count - 1; }
            Filter(); Watch();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (!cancellation.IsCancellationRequested && !_disposed) { _status.Text = "Folder unavailable: " + ex.Message; _address.Text = CurrentDirectory; } }
        finally { if (_loading == cancellation) _loading = null; cancellation.Dispose(); }
    }
    private void Watch()
    {
        _watcher?.Dispose(); _watcher = null;
        try { _watcher = new FileSystemWatcher(CurrentDirectory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite }; FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => { if (!_disposed) { _refresh.Stop(); _refresh.Start(); } }); _watcher.Changed += changed; _watcher.Created += changed; _watcher.Deleted += changed; _watcher.Renamed += (_, e) => changed(this, e); _watcher.EnableRaisingEvents = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    public void SortBy(string column) { if (column is not ("Name" or "Type" or "Size" or "Modified")) return; _descending = _sort == column && !_descending; _sort = column; Filter(); }
    private void Filter()
    {
        var selected = (_list.SelectedItem as LocalEntry)?.Path;
        var files = _entries.Where(x => x.Name.Contains(_filter.Text ?? "", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.IsDirectory);
        IOrderedEnumerable<LocalEntry> ordered = _sort switch {
            "Size" => _descending ? files.ThenByDescending(x => x.Size) : files.ThenBy(x => x.Size),
            "Modified" => _descending ? files.ThenByDescending(x => x.Modified) : files.ThenBy(x => x.Modified),
            "Type" => _descending ? files.ThenByDescending(x => x.Kind, StringComparer.OrdinalIgnoreCase) : files.ThenBy(x => x.Kind, StringComparer.OrdinalIgnoreCase),
            _ => _descending ? files.ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase) : files.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        };
        _list.ItemsSource = ordered.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected is not null) _list.SelectedItem = _entries.FirstOrDefault(x => x.Path == selected);
        foreach (var (key, label) in _sortLabels) { label.Text = key.ToUpperInvariant() + (key == _sort ? _descending ? " ↓" : " ↑" : ""); label.Foreground = key == _sort ? Ui.Ink : Ui.Muted; }
        UpdateStatus();
    }
    private void UpdateStatus() => _status.Text = SelectedEntries.Count > 1 ? $"{SelectedEntries.Count} items selected" : _list.SelectedItem is LocalEntry item ? $"{item.Name}  ·  {item.SizeLabel}  ·  REAL FILE" : $"{_list.ItemCount} items  ·  This PC";
    private void Up() { var parent = Directory.GetParent(CurrentDirectory); if (parent is not null) Navigate(parent.FullName); }
    private void History(int delta) { var next = _historyIndex + delta; if (next >= 0 && next < _history.Count) { _historyIndex = next; Navigate(_history[next], false); } }
    private async void OpenSelected()
    {
        if (_list.SelectedItem is not LocalEntry entry) return;
        await OpenPathAsync(entry.Path);
    }
    public async Task OpenPathAsync(string path)
    {
        try
        {
            path = LocalFiles.Resolve(path.Trim().Trim('"'));
            if (Directory.Exists(path)) { Navigate(path); return; }
            if (!File.Exists(path)) throw new FileNotFoundException("This file or folder no longer exists: " + path);
            if (_openResource is not null) await _openResource(path);
            else if (_playMedia is not null && MediaView.Supports(path)) _playMedia(path);
            else if (_openText is not null && await TextFiles.IsDocumentAsync(path)) { if (!_disposed) _openText(path); }
            else if (!_disposed) LocalFiles.Open(path);
        }
        catch (Exception ex) { if (!_disposed) _toast("Could not open file: " + ex.Message); }
    }
    private void RenameSelected() { if (_list.SelectedItem is LocalEntry entry) NameForm("Rename item", entry.Name, name => { LocalFiles.Rename(entry, name); var renamed = Path.Combine(Path.GetDirectoryName(entry.Path)!, name); _undo = () => { if (File.Exists(entry.Path) || Directory.Exists(entry.Path)) throw new IOException("The original name is now in use."); if (entry.IsDirectory) Directory.Move(renamed, entry.Path); else File.Move(renamed, entry.Path); }; }); }
    public void Undo() { if (_undo is null) return; Try(() => { _undo(); _undo = null; Navigate(CurrentDirectory, false); }); }
    private void UpdateBreadcrumbs()
    {
        _breadcrumbs.Children.Clear(); var root = Path.GetPathRoot(CurrentDirectory)!; var path = root;
        _breadcrumbs.Children.Add(Ui.Button(root, () => Navigate(root)));
        foreach (var part in CurrentDirectory[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) { path = Path.Combine(path, part); var target = path; _breadcrumbs.Children.Add(Ui.Label("›")); var b = Ui.Button(part, () => Navigate(target)); b.Padding = new Thickness(5, 0); b.MinHeight = 24; _breadcrumbs.Children.Add(b); }
    }
    private void RefreshFavorites()
    { _favorites.Children.Clear(); if (_state is null) return; _favorites.Children.Add(Ui.Label("FAVORITES")); foreach (var path in _state.Workspace.Favorites.ToArray()) { var b = Ui.Button(Path.GetFileName(path.TrimEnd('\\')), () => Navigate(path)); b.HorizontalContentAlignment = HorizontalAlignment.Left; var menu = new ContextMenu(); Menu(menu, "Remove favorite", () => { _state.Workspace.Favorites.Remove(path); _state.Save(false); RefreshFavorites(); }); b.ContextMenu = menu; _favorites.Children.Add(b); } _favorites.Children.Add(Ui.Button("Recent files", ShowRecent)); }
    private void ToggleFavorite() { if (_state is null) return; if (!_state.Workspace.Favorites.Contains(CurrentDirectory, StringComparer.OrdinalIgnoreCase)) _state.Workspace.Favorites.Add(CurrentDirectory); _state.Save(false); RefreshFavorites(); }
    private void ShowRecent()
    { if (_state is null) return; var menu = new ContextMenu(); foreach (var path in _state.Workspace.RecentFiles.Take(25)) Menu(menu, Path.GetFileName(path), () => _ = OpenPathAsync(path)); if (menu.Items.Count == 0) Menu(menu, "No recent files yet", () => { }, false); menu.Open(this); }
    private int _previewVersion;
    private async void PreviewSelected()
    {
        var version = ++_previewVersion; if (!_preview.IsVisible) return;
        if (_preview.Content is Border { Child: StackPanel old } && old.Children.OfType<Image>().FirstOrDefault()?.Source is IDisposable bitmap) bitmap.Dispose();
        if (_list.SelectedItem is not LocalEntry item) { _preview.Content = Ui.Pad(Ui.Text("Select a file to preview.", 12)); return; }
        var content = Ui.Stack(12, Ui.Text(item.Name, 18), Ui.Label(item.Kind + " · " + item.SizeLabel), Ui.Text(item.Modified.ToString("dd MMM yyyy HH:mm"), 11)); _preview.Content = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(14), Child = content };
        if (item.IsDirectory) return;
        try
        {
            if (ImageView.Supports(item.Path)) { using var stream = File.OpenRead(item.Path); var image = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 200); content.Children.Add(new Image { Source = image, MaxHeight = 220, Stretch = Stretch.Uniform }); }
            else if (await TextFiles.IsDocumentAsync(item.Path)) { using var reader = new StreamReader(item.Path); var buffer = new char[3000]; var count = await reader.ReadAsync(buffer); if (_disposed || version != _previewVersion) return; var text = Ui.Text(new string(buffer, 0, count), 11); text.TextWrapping = TextWrapping.Wrap; content.Children.Add(Ui.Scroll(text)); }
        }
        catch (Exception ex) { if (version == _previewVersion) content.Children.Add(Ui.Text(ex.Message, 11)); }
    }
    private async void CreateArchive()
    { var paths = SelectedEntries.Select(x => x.Path).ToArray(); if (paths.Length == 0 || await PickFile.Save(this, "Archive.zip") is not { } output) return; RunOperation(() => ArchiveFiles.Create(output, paths), "Archive created."); }
    private void NameForm(string title, string initial, Action<string> operation)
    {
        var input = new TextBox { Text = initial, Watermark = "Name", Name = "LocalFileName" }; var error = Ui.Text("", 12, Brush.Parse("#99513D")); error.TextWrapping = TextWrapping.Wrap;
        var form = Ui.Stack(18, Ui.Heading("THIS PC", title, CurrentDirectory), input, error);
        form.Children.Add(Ui.Row(10, Ui.Button("Cancel", () => { _editing = false; _body.Content = _list; }, "outline"), Ui.Button("Save", () => { try { operation(input.Text ?? ""); Navigate(CurrentDirectory, false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { error.Text = ex.Message; } }, "primary")));
        _editing = true; _body.Content = Ui.Pad(form, 28); input.Focus(); input.SelectAll();
    }
    private void RecycleSelected()
    {
        var entries = SelectedEntries.ToArray(); if (entries.Length == 0) return;
        var prompt = Ui.Stack(20, Ui.Heading("THIS PC", "Move to Recycle Bin?", entries.Length == 1 ? entries[0].Path : $"{entries.Length} selected items"), Ui.Row(12, Ui.Button("Keep items", () => { _editing = false; _body.Content = _list; }, "outline"), Ui.Button("Recycle", () => RunOperation(() => { foreach (var entry in entries) LocalFiles.Recycle(entry); }, "Items moved to Recycle Bin."), "primary")));
        _editing = true; _body.Content = Ui.Pad(prompt, 28);
    }
    private async void RunOperation(Action action, string success, Action? after = null)
    {
        if (_busy) return; _busy = true; IsEnabled = false; _status.Text = "Working…";
        try { await Task.Run(action); if (_disposed) return; after?.Invoke(); _toast(success); Navigate(CurrentDirectory, false); }
        catch (Exception ex) { if (!_disposed) _toast("Operation did not complete: " + ex.Message); }
        finally { _busy = false; if (!_disposed) IsEnabled = true; }
    }
    private async void EditSelected()
    {
        if (_list.SelectedItem is not LocalEntry { IsDirectory: false } entry) return;
        try
        {
            if (entry.Size > 2 * 1024 * 1024) throw new IOException("Use the default application for files larger than 2 MB.");
            var bytes = await File.ReadAllBytesAsync(entry.Path); var bom = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
            var encoding = new System.Text.UTF8Encoding(bom, true);
            var original = encoding.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); if (original.Contains('\0')) throw new IOException("This appears to be a binary file. Use Open instead."); if (_disposed) return;
            var timestamp = File.GetLastWriteTimeUtc(entry.Path);
            var editor = new TextBox { Text = original, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = Ui.Mono, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Top };
            var status = Ui.Text("UTF-8 text editor · " + entry.Name, 12, Ui.Muted);
            var grid = new Grid { RowDefinitions = new RowDefinitions("30,*,50"), Margin = new Thickness(20) }; grid.Children.Add(status); Grid.SetRow(editor, 1); grid.Children.Add(editor);
            var actions = Ui.Row(10, Ui.Button("Back", () => Navigate(CurrentDirectory, false), "outline"), Ui.Button("Save file", async () => { try { if (File.GetLastWriteTimeUtc(entry.Path) != timestamp) throw new IOException("This file changed on disk. Reopen it before saving."); await File.WriteAllTextAsync(entry.Path, editor.Text ?? "", encoding); timestamp = File.GetLastWriteTimeUtc(entry.Path); status.Text = "Saved to disk · " + DateTime.Now.ToString("HH:mm"); } catch (Exception ex) { status.Text = ex.Message; } }, "primary")); Grid.SetRow(actions, 2); grid.Children.Add(actions); _editing = true; _body.Content = grid;
        }
        catch (Exception ex) { _toast(ex.Message); }
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { _toast(ex.Message); } }
    private static void CompactColumns(Grid row, double width) { row.ColumnDefinitions[3].Width = new GridLength(width >= 540 ? 120 : 0); row.Children[3].IsVisible = width >= 540; row.ColumnDefinitions[1].Width = new GridLength(width >= 360 ? 85 : 0); row.Children[1].IsVisible = width >= 360; }
    public void Dispose() { _disposed = true; _loading?.Cancel(); _watcher?.Dispose(); _refresh.Stop(); }
}
