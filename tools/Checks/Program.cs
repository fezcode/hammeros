using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerOS;
using HammerOS.Apps;
using HammerOS.Core;
using HammerOS.Desktop;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont().SetupWithoutStarting();
var passed = 0;
void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS  " + name); } catch (Exception ex) { Console.Error.WriteLine("FAIL  " + name + "\n" + ex); Environment.Exit(1); } }
void Require(bool condition, string message = "Check failed") { if (!condition) throw new InvalidOperationException(message); }
void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Button ButtonWith(Control root, string text) => root.GetVisualDescendants().OfType<Button>().First(x => x.Content is string s && s == text);
var state = new SystemState(persistent: false); state.Preferences.Motion = false;
var terminal = new TerminalSession(state);
Check("Virtual paths stay rooted", () => Require(SystemState.Normalize("../../../../system", "/personal") == "/system"));
Check("Terminal quoted filenames and file contents round trip", () => { Require(terminal.Execute("write \"report one.txt\" \"The work continues\"") == "Saved."); Require(terminal.Execute("cat 'report one.txt'") == "The work continues"); });
Check("Terminal filesystem is shared with applications", () => Require(state.Find("/department/report one.txt")?.Content == "The work continues"));
Check("Protected system files cannot change", () => { Require(terminal.Execute("write /system/workstation.conf erased").Contains("read-only")); Require(terminal.Execute("rm /system/workstation.conf").Contains("protected")); });
Check("Nonempty folders cannot be deleted", () => Require(state.Delete("/department/handbook").Contains("empty")));
Check("Folder creation, navigation, and removal", () => { Require(terminal.Execute("mkdir scratch") == "Saved."); terminal.Execute("cd scratch"); Require(terminal.Cwd == "/department/scratch"); terminal.Execute("cd .."); Require(terminal.Execute("rm scratch") == "Removed."); });
Check("Unknown commands and applications are handled", () => { Require(terminal.Execute("format").Contains("Unknown command")); Require(terminal.Execute("open nonexistent").Contains("Unknown application")); });
Check("Sudoku has one solution, protects givens, and supports pencil notes", () => {
    var game = state.Sudoku; Require(game.Valid && SudokuGame.CountSolutions(game.Givens) == 1); var fixedCell = Array.FindIndex(game.Givens, x => x > 0); game.Enter(fixedCell, 0); Require(game.Cells[fixedCell] == game.Givens[fixedCell]);
    var empty = Array.FindIndex(game.Givens, x => x == 0); game.Enter(empty, 5, true); Require(game.Cells[empty] == 0 && (game.Notes[empty] & 1 << 5) != 0); game.Enter(empty, game.Solution[empty]); Require(game.Notes[empty] == 0 && !game.Conflict(empty)); game.Reset();
});
Check("Intranet checks respond to adapter and relay", () => { Require(terminal.Execute("ping").Contains("reply")); state.Preferences.Network = false; Require(terminal.Execute("ping").Contains("unavailable")); state.Preferences.Network = true; state.Services["Intranet relay"] = false; Require(terminal.Execute("ping").Contains("unavailable")); state.Services["Intranet relay"] = true; });
var temp = Path.Combine(Path.GetTempPath(), "HammerOS-check-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    Check("Session files, identity, and refinement survive restart", () => { var saved = new SystemState(temp); saved.Write("/personal/check.txt", "persisted"); saved.Preferences.Employee = "Test User"; saved.Preferences.Bins[0] = 99; saved.Save(); var loaded = new SystemState(temp); Require(loaded.Find("/personal/check.txt")?.Content == "persisted"); Require(loaded.Preferences.Employee == "Test User" && loaded.Preferences.Bins[0] == 99); });
    Check("Calendar entries and Sudoku progress survive restart", () => { var saved = new SystemState(temp); var index = Array.FindIndex(saved.Sudoku.Givens, x => x == 0); saved.Sudoku.Enter(index, saved.Sudoku.Solution[index]); saved.Calendar.Add(new CalendarEntry { Id = "test", Title = "Design review", When = DateTime.Today.AddHours(10) }); saved.Save(); var loaded = new SystemState(temp); Require(loaded.Sudoku.Cells[index] == saved.Sudoku.Solution[index] && loaded.Calendar.Single().Title == "Design review"); });
    Check("Wallpaper scene and animation preference survive restart", () => { var saved = new SystemState(temp); saved.Preferences.WallpaperScene = "Earth"; saved.Preferences.WallpaperAnimation = false; saved.Save(); var loaded = new SystemState(temp); Require(loaded.Preferences.WallpaperScene == "Earth" && !loaded.Preferences.WallpaperAnimation); });
    Check("Corrupt state recovers to a usable desktop", () => { File.WriteAllText(temp, "not json"); var loaded = new SystemState(temp); Require(loaded.StorageWarning is not null && loaded.Find("/personal/notes.txt") is not null); });
}
finally { if (File.Exists(temp)) File.Delete(temp); }
var shell = new ShellWindow(state, true); shell.Show(); Pump(); shell.StartDesktop(); Pump();
void Pump() { Dispatcher.UIThread.RunJobs(); shell.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void WaitFor(Func<bool> condition)
{
    var loop = new DispatcherFrame(); var clock = System.Diagnostics.Stopwatch.StartNew();
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    timer.Tick += (_, _) => { if (condition() || clock.ElapsedMilliseconds > 2000) loop.Continue = false; };
    timer.Start(); Dispatcher.UIThread.PushFrame(loop); timer.Stop();
}
Check("Default desktop opens refinement and terminal", () => Require(shell.Frames.Select(x => x.AppId).Order().SequenceEqual(new[] { "refinement", "terminal" })));
Check("All applications instantiate and lay out", () => { foreach (var app in AppCatalog.All.Where(x => !shell.Frames.Any(f => f.AppId == x.Id))) { shell.OpenApp(app.Id); Pump(); } Require(shell.Frames.Count == AppCatalog.All.Length); });
Check("Every app supports independent window instances", () => {
    foreach (var app in AppCatalog.All) {
        var original = shell.Frames.Single(x => x.AppId == app.Id); shell.OpenApp(app.Id); Pump(); var second = shell.Frames.Last();
        Require(second != original && second.AppId == app.Id && second.AppContent != original.AppContent && second.DisplayName != original.DisplayName);
        second.Minimize(); original.Restore(); Require(second.IsMinimized && !original.IsMinimized);
        shell.RemoveFrame(second); Require(shell.Frames.Contains(original));
    }
});
Check("Terminal tabs rename, switch, and close independently", () => {
    var view = (TerminalView)shell.Frames.Single(x => x.AppId == "terminal").AppContent;
    Require(view.Tabs.Count == 2 && ((HostTerminalView)view.Tabs[1].Content).Session is null);
    var original = view.Tabs[0]; var tab = view.AddDepartment(); view.RenameTab(tab, "Research"); Pump();
    Require(view.ActiveTab == tab && tab.Title == "Research"); view.Activate(original); view.CloseTab(tab); Pump();
    Require(view.ActiveTab == original && view.Tabs.Count == 2 && ((HostTerminalView)view.Tabs[1].Content).Session is null);
    var input = ((DepartmentTerminalView)original.Content).Input; Require(input.Bounds.Height >= 36, "Command entry clipped");
});
var localRoot = Path.Combine(Path.GetTempPath(), "HammerOS-files-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(localRoot);
try
{
    Check("Real files create, rename, copy, and move on disk", () => {
        LocalFiles.Create(localRoot, "first.txt", false); File.WriteAllText(Path.Combine(localRoot, "first.txt"), "actual content");
        LocalFiles.Rename(LocalFiles.List(localRoot).Single(), "renamed.txt"); LocalFiles.Create(localRoot, "destination", true);
        var source = LocalFiles.List(localRoot).Single(x => !x.IsDirectory); var destination = Path.Combine(localRoot, "destination");
        LocalFiles.Transfer(source, destination, false); Require(File.ReadAllText(Path.Combine(destination, "renamed.txt")) == "actual content");
        LocalFiles.Rename(source, "moved.txt"); LocalFiles.Transfer(LocalFiles.List(localRoot).Single(x => !x.IsDirectory), destination, true);
        Require(!File.Exists(Path.Combine(localRoot, "moved.txt")) && File.Exists(Path.Combine(destination, "moved.txt")));
    });
    Check("File operations reject overwrite and recursive self-copy", () => {
        var folder = LocalFiles.List(localRoot).Single(); var prevented = 0;
        try { LocalFiles.Transfer(folder, folder.Path, false); } catch (IOException) { prevented++; }
        var file = LocalFiles.List(folder.Path).First(); try { LocalFiles.Transfer(file, folder.Path, false); } catch (IOException) { prevented++; }
        try { LocalFiles.Child(localRoot, "../escaped.txt"); } catch (IOException) { prevented++; }
        Require(prevented == 3);
    });
    Check("Real explorer navigates and filters native file entries", () => {
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; files.Select(true); files.Local.Navigate(Path.Combine(localRoot, "destination"));
        WaitFor(() => files.Local.Loading.IsCompleted); Pump(); Require(files.Local.CurrentDirectory == Path.Combine(localRoot, "destination"));
        var list = files.GetVisualDescendants().OfType<ListBox>().Single(); Require(list.ItemCount == 2);
        var filter = files.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "FileFilter"); filter.Text = "moved"; Pump(); Require(list.ItemCount == 1 && filter.Bounds.Height >= 34); filter.Text = "";
    });
    Check("File column headers sort ascending and descending", () => {
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; var list = files.GetVisualDescendants().OfType<ListBox>().Single();
        var header = files.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "SortName"); Click(header); Pump(); Require(((LocalEntry)list.Items[0]!).Name == "renamed.txt"); Click(header); Pump(); Require(((LocalEntry)list.Items[0]!).Name == "moved.txt");
        File.WriteAllText(Path.Combine(localRoot, "destination", "moved.txt"), "x"); files.Local.Navigate(files.Local.CurrentDirectory, false); WaitFor(() => files.Local.Loading.IsCompleted); files.Local.SortBy("Size"); Pump(); Require(((LocalEntry)list.Items[0]!).Size == 1); files.Local.SortBy("Size"); Pump(); Require(((LocalEntry)list.Items[0]!).Size > 1);
    });
    Check("Explorer opens Unicode media paths inside HammerOS", () => {
        var path = Path.Combine(localRoot, "Cevapsız Sorular & notes.MP3"); File.WriteAllBytes(path, [0]);
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; files.Select(true); files.Local.Navigate(localRoot); WaitFor(() => files.Local.Loading.IsCompleted); Pump();
        var list = files.GetVisualDescendants().OfType<ListBox>().Single(); list.SelectedItem = list.Items.OfType<LocalEntry>().Single(x => x.Path == path);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump();
        var frame = shell.Frames.Last(); Require(frame.AppContent is MediaView view && view.CurrentFile == path, "Media was not routed to the built-in player"); shell.RemoveFrame(frame);
    });
    Check("Explorer opens text documents and unfamiliar text extensions internally", () => {
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent;
        foreach (var name in new[] { "Temp.txt", "Temp.txtt", "notes.unknown", "README", "Cevapsız Ω.TXT" })
        {
            var path = Path.Combine(localRoot, name); File.WriteAllText(path, "Real text · Ω\nSecond line");
            files.Local.Navigate(localRoot); WaitFor(() => files.Local.Loading.IsCompleted); Pump();
            var list = files.GetVisualDescendants().OfType<ListBox>().Single(); list.SelectedItem = list.Items.OfType<LocalEntry>().Single(x => x.Path == path);
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); WaitFor(() => shell.Frames.Last().AppContent is EditorView view && view.FilePath == path);
            var frame = shell.Frames.Last(); Require(frame.AppContent is EditorView opened && opened.Editor.Text == "Real text · Ω\nSecond line" && !opened.IsDirty, "Text file did not load in HammerOS: " + name); shell.RemoveFrame(frame);
        }
        var address = files.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "LocalAddress"); var document = Path.Combine(localRoot, "Temp.txt"); address.Text = '"' + document + '"';
        address.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); WaitFor(() => shell.Frames.Last().AppContent is EditorView view && view.FilePath == document); Require(shell.Frames.Last().AppContent is EditorView { FilePath: not null }); shell.RemoveFrame(shell.Frames.Last());
        var binary = Path.Combine(localRoot, "binary.unknown"); File.WriteAllBytes(binary, [0, 1, 255]); var probe = TextFiles.IsDocumentAsync(binary); WaitFor(() => probe.IsCompleted); Require(!probe.Result, "Binary file mistaken for text");
    });
    if (args is ["--document", var actualDocument]) Check("Requested local text document opens through Explorer", () => {
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; var path = Path.GetFullPath(actualDocument); var opening = files.Local.OpenPathAsync(path);
        WaitFor(() => opening.IsCompleted && shell.Frames.Last().AppContent is EditorView view && view.FilePath == path);
        var frame = shell.Frames.Last(); Require(frame.AppContent is EditorView view && view.FilePath == path && view.Editor.Text == File.ReadAllText(path) && !view.IsDirty); shell.RemoveFrame(frame);
    });
    Check("Explorer context menu retains its inline editor", () => {
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; files.Select(true); files.Local.Navigate(localRoot); WaitFor(() => files.Local.Loading.IsCompleted); Pump();
        var list = files.GetVisualDescendants().OfType<ListBox>().Single(); var path = Path.Combine(localRoot, "Temp.txt"); list.SelectedItem = list.Items.OfType<LocalEntry>().Single(x => x.Path == path);
        var actions = ButtonWith(files, "Actions ▾"); Click(actions); Pump(); var menu = actions.ContextMenu!; var option = menu.Items.OfType<MenuItem>().Single(x => x.Header as string == "Edit here (inline editor)");
        var count = shell.Frames.Count; option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); WaitFor(() => files.GetVisualDescendants().OfType<TextBox>().Any(x => x.AcceptsReturn)); Pump();
        var inline = files.GetVisualDescendants().OfType<TextBox>().Single(x => x.AcceptsReturn); Require(shell.Frames.Count == count && inline.Text == File.ReadAllText(path) && inline.ContextMenu is not null);
        inline.Text = "Saved inline"; Click(ButtonWith(files, "Save file")); WaitFor(() => File.ReadAllText(path) == "Saved inline"); Click(ButtonWith(files, "Back")); WaitFor(() => files.Local.Loading.IsCompleted);
    });
    Check("Text Editor saves real text and protects unsaved changes", () => {
        var frame = shell.Frames.Single(x => x.AppId == "editor"); var editor = (EditorView)frame.AppContent; var path = Path.Combine(localRoot, "document.txt"); editor.Editor.Text = "A real document. Ω"; var save = editor.SaveToAsync(path); WaitFor(() => save.IsCompleted); Require(save.Result && File.ReadAllText(path) == "A real document. Ω");
        editor.Editor.Text = "Unsaved revision"; frame.Close(); Pump(); Require(shell.Frames.Contains(frame) && editor.IsDirty); Click(ButtonWith(editor, "Cancel")); Pump(); Require(editor.Editor.Text == "Unsaved revision"); editor.Editor.Text = "A real document. Ω";
    });
    Check("File drops copy safely and respect unsaved editor content", () => {
        var path = Path.Combine(localRoot, "drop Ω.txt"); File.WriteAllText(path, "Dropped document");
        var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; files.Select(true); files.Local.Navigate(Path.Combine(localRoot, "destination")); WaitFor(() => files.Local.Loading.IsCompleted); Pump();
        var list = files.GetVisualDescendants().OfType<ListBox>().Single(); var data = new DataObject(); data.Set("HammerOS.FilePaths", new[] { path });
        list.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, list, new Point(list.Bounds.Width - 8, list.Bounds.Height - 8), KeyModifiers.None));
        var copy = Path.Combine(localRoot, "destination", "drop Ω.txt"); WaitFor(() => File.Exists(copy) && files.Local.IsEnabled); Require(File.ReadAllText(copy) == "Dropped document" && File.Exists(path));
        var editor = (EditorView)shell.Frames.Single(x => x.AppId == "editor").AppContent; editor.Editor.Text = "Keep this draft";
        editor.Editor.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, editor.Editor, new Point(30, 30), KeyModifiers.None)); Pump(); Require(editor.Editor.Text == "Keep this draft"); Click(ButtonWith(editor, "Cancel"));
        editor.Editor.Text = "A real document. Ω"; editor.Editor.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, editor.Editor, new Point(30, 30), KeyModifiers.None)); WaitFor(() => editor.FilePath == path); Require(editor.Editor.Text == "Dropped document");
        var mediaFrame = shell.Frames.Single(x => x.AppId == "media"); var button = shell.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "TaskbarApp" && x.Tag == mediaFrame);
        var audio = Path.Combine(localRoot, "Cevapsız Sorular & notes.MP3"); data.Set("HammerOS.FilePaths", new[] { audio }); mediaFrame.Minimize(); Pump(); button = shell.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "TaskbarApp" && x.Tag == mediaFrame);
        button.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, button, new Point(8, 8), KeyModifiers.None)); Pump(); Require(!mediaFrame.IsMinimized && ((MediaView)mediaFrame.AppContent).CurrentFile == audio);
    });
}
finally { Directory.Delete(localRoot, true); }
Check("Top bar provides working window layouts", () => {
    shell.TileWindows(); Pump(); Require(shell.GetVisualDescendants().OfType<Menu>().Any());
    foreach (var frame in shell.Frames) Require(Canvas.GetLeft(frame) >= 0 && Canvas.GetTop(frame) >= 0 && Canvas.GetLeft(frame) + frame.Width <= shell.Workspace.Bounds.Width + 1);
    shell.CascadeWindows(); Pump();
});
Check("Long notifications wrap within their panel and can be dismissed", () => {
    shell.Toast("Unable to open C:\\Music\\Cevapsız Sorular - maNga.mp3. " + new string('x', 160) + " Choose another application and try again."); Pump();
    var panel = shell.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Notification"); var text = panel.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "NotificationText");
    Require(text.Bounds.Height > 30 && text.Bounds.Width < panel.Bounds.Width - 40, "Notification text is not constrained for wrapping");
    Click(panel.GetVisualDescendants().OfType<Button>().Single()); Pump(); Require(!shell.GetVisualDescendants().Contains(panel));
});
Check("Taskbar menus control the selected window and offer workspace actions", () => {
    shell.OpenApp("editor"); var frame = shell.Frames.Last(); Pump();
    MenuItem Item(ContextMenu menu, string title) => menu.Items.OfType<MenuItem>().Single(x => x.Header as string == title);
    void Choose(string title) { var button = shell.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "TaskbarApp" && x.Tag == frame); var menu = button.ContextMenu!; menu.Open(button); Pump(); Item(menu, title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Pump(); }
    Choose("Minimize"); Require(frame.IsMinimized); Choose("Restore"); Require(!frame.IsMinimized);
    Choose("Maximize"); Require(frame.IsMaximized); Choose("Restore size"); Require(!frame.IsMaximized);
    Choose("New window"); var second = shell.Frames.Last(); Require(second != frame && second.AppId == frame.AppId); shell.RemoveFrame(second);
    ((EditorView)frame.AppContent).Editor.Text = "Keep this document"; Choose("Close window"); Require(shell.Frames.Contains(frame)); Click(ButtonWith(frame, "Cancel")); ((EditorView)frame.AppContent).Editor.Text = ""; Choose("Close window"); Require(!shell.Frames.Contains(frame));
    var bar = shell.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Taskbar"); var workspaceMenu = bar.ContextMenu!; workspaceMenu.Open(bar); Pump(); Require(Item(workspaceMenu, "Show desktop").IsEnabled && Item(workspaceMenu, "Tile windows").IsEnabled); workspaceMenu.Close();
});
Check("Network changes update multiple live applications", () => { state.Preferences.Network = false; state.Save(); Pump(); Require(shell.Frames.Single(x => x.AppId == "browser").GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "A temporary separation.")); state.Preferences.Network = true; state.Save(); Pump(); });
Check("File explorer navigates repeatedly without reparenting errors", () => { var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent; files.Navigate("/personal"); Pump(); files.Navigate("/department"); Pump(); files.Navigate("/system"); Pump(); files.Navigate("/personal"); Pump(); });
Check("File explorer edits a record and writes through to terminal", () => {
    var files = (FilesView)shell.Frames.Single(x => x.AppId == "files").AppContent;
    var record = files.GetVisualDescendants().OfType<Button>().First(x => x.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "notes.txt")); Click(record); Pump();
    var editor = files.GetVisualDescendants().OfType<TextBox>().Single(x => x.AcceptsReturn); editor.Text = "A checked memorandum.";
    Click(ButtonWith(files, "Save record")); Pump(); Require(terminal.Execute("cat /personal/notes.txt") == "A checked memorandum."); Click(ButtonWith(files, "Back")); Pump();
});
Check("Refinement collects selected data and persists quota", () => { var view = (RefinementView)shell.Frames.Single(x => x.AppId == "refinement").AppContent; var field = view.GetVisualDescendants().OfType<NumberField>().Single(); field.SelectAt(75); var before = state.Preferences.Bins[0]; var count = field.Selected.Count; view.Assign(0); Require(count == 9 && state.Preferences.Bins[0] == before + 9 && field.Selected.Count == 0); });
Check("Stopped refinement service blocks data assignment", () => { state.Services["Refinement engine"] = false; var view = (RefinementView)shell.Frames.Single(x => x.AppId == "refinement").AppContent; var field = view.GetVisualDescendants().OfType<NumberField>().Single(); field.SelectAt(80); var before = state.Preferences.Bins[1]; view.Assign(1); Require(state.Preferences.Bins[1] == before); state.Services["Refinement engine"] = true; });
Check("Intranet history and out-of-scope addresses", () => { var browser = (IntranetView)shell.Frames.Single(x => x.AppId == "browser").AppContent; browser.Navigate("departments"); Pump(); Require(browser.CurrentPage == "departments"); browser.Navigate("https://example.org"); Pump(); Require(browser.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Your world is here.")); browser.Navigate("home"); Pump(); });
Check("Minimize, restore, maximize, and restore geometry", () => { var frame = shell.Frames.Single(x => x.AppId == "terminal"); var width = frame.Width; frame.Minimize(); Require(frame.IsMinimized && !frame.IsVisible); frame.Restore(); Require(!frame.IsMinimized && frame.IsVisible); frame.ToggleMaximize(); Require(frame.IsMaximized && Math.Abs(frame.Width - shell.Workspace.Bounds.Width) < 1); frame.ToggleMaximize(); Require(!frame.IsMaximized && Math.Abs(width - frame.Width) < 1); });
Check("Title drag uses real pointer events", () => {
    var frame = shell.Frames.Single(x => x.AppId == "terminal"); shell.FocusFrame(frame); Pump();
    var label = frame.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "TERMINAL");
    var point = label.TranslatePoint(new Point(20, 7), shell)!.Value; var x = Canvas.GetLeft(frame);
    shell.MouseDown(point, MouseButton.Left); shell.MouseMove(point + new Vector(-60, -20)); shell.MouseUp(point + new Vector(-60, -20), MouseButton.Left); Pump();
    Require(Canvas.GetLeft(frame) < x - 40, $"Window did not follow pointer drag: {x} -> {Canvas.GetLeft(frame)}");
});
Check("Every edge and corner resizes with actual pointer input", () => {
    var frame = shell.Frames.Single(x => x.AppId == "terminal"); shell.FocusFrame(frame);
    foreach (var (edge, dx, dy) in new[] { ("Left", -40, 0), ("Right", 40, 0), ("Top", 0, -30), ("Bottom", 0, 30), ("TopLeft", -40, -30), ("TopRight", 40, -30), ("BottomLeft", -40, 30), ("BottomRight", 40, 30) }) {
        frame.ArrangeTo(new Rect(250, 170, 640, 400)); Pump(); var handle = frame.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Resize" + edge);
        var point = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(point, MouseButton.Left); shell.MouseMove(point + new Vector(dx, dy)); shell.MouseUp(point + new Vector(dx, dy), MouseButton.Left); Pump();
        Require(Math.Abs(frame.Width - (640 + Math.Abs(dx))) < 1 && Math.Abs(frame.Height - (400 + Math.Abs(dy))) < 1, edge + " failed to resize");
        Require(Math.Abs(Canvas.GetLeft(frame) - (edge.Contains("Left") ? 210 : 250)) < 1 && Math.Abs(Canvas.GetTop(frame) - (edge.Contains("Top") ? 140 : 170)) < 1, edge + " failed to preserve opposite edge");
    }
    state.Preferences.EdgeResize = false; state.Save(); Pump(); Require(frame.GetVisualDescendants().OfType<Border>().Where(x => x.Name?.StartsWith("Resize") == true).All(x => !x.IsVisible));
    state.Preferences.EdgeResize = true; state.Save(); frame.ToggleMaximize(); Pump(); Require(frame.GetVisualDescendants().OfType<Border>().Where(x => x.Name?.StartsWith("Resize") == true).All(x => !x.IsVisible)); frame.ToggleMaximize(); Pump();
});
Check("Application search filters and launches with Enter", () => {
    shell.ToggleLauncher(); Pump(); var search = shell.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "AppSearch"); search.Text = "Wellness"; Pump();
    search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump(); Require(shell.Frames.Last().AppId == "wellness" && shell.Frames.Last().ZIndex == shell.Frames.Max(x => x.ZIndex)); shell.RemoveFrame(shell.Frames.Last());
});
Check("Application directory dismisses on an outside click", () => { shell.ToggleLauncher(); Pump(); Require(shell.GetVisualDescendants().OfType<TextBox>().Any(x => x.Name == "AppSearch")); shell.MouseDown(new Point(1200, 120), MouseButton.Left); shell.MouseUp(new Point(1200, 120), MouseButton.Left); Pump(); Require(!shell.GetVisualDescendants().OfType<TextBox>().Any(x => x.Name == "AppSearch")); });
Check("Text inputs share the Explorer menu theme and preserve edit actions", () => {
    var editor = (EditorView)shell.Frames.Single(x => x.AppId == "editor").AppContent; var original = editor.Editor.Text; editor.Editor.Text = "Menu check"; editor.Editor.SelectionStart = 0; editor.Editor.SelectionEnd = 4;
    var menu = editor.Editor.ContextMenu ?? throw new InvalidOperationException("Shared editor menu missing"); Require(editor.Editor.ContextFlyout is null); menu.Open(editor.Editor); Pump();
    var delete = menu.Items.OfType<MenuItem>().Single(x => x.Header as string == "Delete"); Require(delete.IsEnabled); delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Require(editor.Editor.Text == " check");
    editor.Editor.IsReadOnly = true; menu.Open(editor.Editor); Pump(); Require(!menu.Items.OfType<MenuItem>().Single(x => x.Header as string == "Paste").IsEnabled); menu.Close(); editor.Editor.IsReadOnly = false; editor.Editor.Text = original;
    Require(shell.GetVisualDescendants().OfType<TextBox>().All(x => x.ContextMenu is not null && x.ContextFlyout is null), "A text input still uses the stock flyout");
});
Check("Terminal label and close button share one hover background", () => {
    var frame = shell.Frames.Single(x => x.AppId == "terminal"); frame.Restore(); Pump(); var view = (TerminalView)frame.AppContent;
    var strip = view.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "TerminalTabChrome" && x.Classes.Contains("selected"));
    foreach (var button in strip.GetVisualDescendants().OfType<Button>()) {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), shell)!.Value; shell.MouseMove(point); Pump();
        var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(x => x.Name == "PART_ContentPresenter");
        WaitFor(() => presenter.Background is Avalonia.Media.ISolidColorBrush brush && brush.Color.A == 0); Require(presenter.Background is Avalonia.Media.ISolidColorBrush bg && bg.Color.A == 0, "Nested button paints a second hover block");
        WaitFor(() => strip.Background?.ToString() == "#ff31575b"); Require(strip.Background?.ToString() == "#ff31575b", "Tab does not own the hover background");
    }
    shell.MouseMove(new Point(100, 60)); Pump();
});
Check("Wallpaper selection animates, pauses for reduced motion, and stops on lock", () => {
    var view = shell.Frames.Single(x => x.AppId == "settings").AppContent; var earth = view.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "WallpaperEarth"); Click(earth); Pump();
    var backdrop = shell.GetVisualDescendants().OfType<DesktopBackdrop>().First(x => x.Parent is Panel); Require(backdrop.Scene == "Earth" && !backdrop.AnimationRunning);
    state.Preferences.Motion = true; state.Save(); Require(backdrop.AnimationRunning); var before = backdrop.ElapsedSeconds; WaitFor(() => backdrop.ElapsedSeconds > before + .06); Require(backdrop.ElapsedSeconds > before);
    state.Preferences.WallpaperAnimation = false; state.Save(); Require(!backdrop.AnimationRunning); state.Preferences.WallpaperAnimation = true; state.Save(); shell.Lock(); Require(!backdrop.AnimationRunning); shell.Unlock(); WaitFor(() => backdrop.AnimationRunning); Require(backdrop.AnimationRunning);
    state.Preferences.Motion = false; state.Preferences.WallpaperScene = "Classic"; state.Save(); Pump(); Require(!backdrop.AnimationRunning);
});
Check("Alt+Space searches apps, switches windows, and toggles settings", () => {
    shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space, KeyModifiers = KeyModifiers.Alt }); Pump();
    var palette = shell.GetVisualDescendants().OfType<CommandPalette>().Single(); palette.Search.Text = "calendar"; Pump(); var before = shell.Frames.Count;
    palette.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump(); Require(shell.Frames.Count == before + 1 && shell.Frames.Last().AppId == "calendar"); shell.RemoveFrame(shell.Frames.Last());
    var editor = shell.Frames.Single(x => x.AppId == "editor"); editor.Minimize(); shell.ToggleSpotlight(); Pump(); palette = shell.GetVisualDescendants().OfType<CommandPalette>().Single(); palette.Search.Text = "switch text"; Pump(); Require(palette.Results.ItemCount == 1); palette.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Require(!editor.IsMinimized);
    var texture = state.Preferences.Scanlines; shell.SearchCommands("toggle CRT").First().Run(); Require(state.Preferences.Scanlines != texture); state.Preferences.Scanlines = texture;
    Require(shell.SearchCommands("> Write-Output 'hello'").Single().Title == "Run in PowerShell");
    shell.ToggleSpotlight(); Pump(); palette = shell.GetVisualDescendants().OfType<CommandPalette>().Single(); palette.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump(); Require(!shell.GetVisualDescendants().OfType<CommandPalette>().Any());
    shell.ToggleSpotlight(); Pump(); shell.MouseDown(new Point(50, 100), MouseButton.Left); shell.MouseUp(new Point(50, 100), MouseButton.Left); Pump(); Require(!shell.GetVisualDescendants().OfType<CommandPalette>().Any());
});
Check("Calendar creates local events on the selected day", () => { var view = (CalendarView)shell.Frames.Single(x => x.AppId == "calendar").AppContent; view.SelectDate(new DateTime(2026, 10, 3)); Pump(); view.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "CalendarEventTitle").Text = "Planning"; view.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "CalendarEventTime").Text = "14:30"; Click(ButtonWith(view, "Add event")); Pump(); Require(state.Calendar.Any(x => x.Title == "Planning" && x.When == new DateTime(2026, 10, 3, 14, 30, 0))); });
Check("Lock disables background input, prevents application launch, and resumes", () => { shell.Lock(); Require(shell.IsLocked && shell.Frames.All(x => !x.IsEffectivelyEnabled)); var frame = shell.Frames.Single(x => x.AppId == "settings"); shell.RemoveFrame(frame); shell.OpenApp("settings"); Require(shell.Frames.All(x => x.AppId != "settings")); shell.Unlock(); shell.OpenApp("settings"); Require(!shell.IsLocked && shell.Frames.Any(x => x.AppId == "settings") && shell.Frames.All(x => x.IsEffectivelyEnabled)); });
Check("Compact display keeps entire windows within desktop bounds", () => { shell.Width = 960; shell.Height = 680; Pump(); foreach (var frame in shell.Frames) Require(Canvas.GetLeft(frame) >= 0 && Canvas.GetLeft(frame) + frame.Width <= shell.Workspace.Bounds.Width + 1 && Canvas.GetTop(frame) >= 0 && Canvas.GetTop(frame) + frame.Height <= shell.Workspace.Bounds.Height + 1); });
Check("Application close disposes and removes the window", () => { var frame = shell.Frames.Single(x => x.AppId == "wellness"); frame.Close(); Require(!shell.Frames.Contains(frame)); });
Check("Rapid animated open, minimize, restore, and close settle cleanly", () => {
    Motion.Enabled = true; shell.OpenApp("wellness"); var frame = shell.Frames.Single(x => x.AppId == "wellness"); frame.Minimize();
    WaitFor(() => frame.IsMinimized);
    Require(frame.IsMinimized && !frame.IsVisible); frame.Restore(); frame.Close();
    WaitFor(() => !shell.Frames.Contains(frame));
    Require(!shell.Frames.Contains(frame), $"Close did not settle: minimized={frame.IsMinimized}, visible={frame.IsVisible}, opacity={frame.Opacity}, busy={typeof(AppFrame).GetField("_busy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(frame)}"); Motion.Enabled = false;
});
shell.Close();
Console.WriteLine($"\n{passed} checks passed.");
