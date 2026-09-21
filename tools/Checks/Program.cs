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
Check("Intranet checks respond to adapter and relay", () => { Require(terminal.Execute("ping").Contains("reply")); state.Preferences.Network = false; Require(terminal.Execute("ping").Contains("unavailable")); state.Preferences.Network = true; state.Services["Intranet relay"] = false; Require(terminal.Execute("ping").Contains("unavailable")); state.Services["Intranet relay"] = true; });
var temp = Path.Combine(Path.GetTempPath(), "HammerOS-check-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    Check("Session files, identity, and refinement survive restart", () => { var saved = new SystemState(temp); saved.Write("/personal/check.txt", "persisted"); saved.Preferences.Employee = "Helly R."; saved.Preferences.Bins[0] = 99; saved.Save(); var loaded = new SystemState(temp); Require(loaded.Find("/personal/check.txt")?.Content == "persisted"); Require(loaded.Preferences.Employee == "Helly R." && loaded.Preferences.Bins[0] == 99); });
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
Check("Opening an existing application restores a single instance", () => { shell.OpenApp("terminal"); Require(shell.Frames.Count(x => x.AppId == "terminal") == 1); });
Check("All ten applications instantiate and lay out", () => { foreach (var app in AppCatalog.All) { shell.OpenApp(app.Id); Pump(); } Require(shell.Frames.Count == 10); });
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
Check("Application search filters and launches with Enter", () => {
    shell.ToggleLauncher(); Pump(); var search = shell.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "AppSearch"); search.Text = "Wellness"; Pump();
    search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump(); Require(shell.Frames.Single(x => x.AppId == "wellness").ZIndex == shell.Frames.Max(x => x.ZIndex));
});
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

