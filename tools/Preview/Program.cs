using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HammerOS;
using HammerOS.Core;
using HammerOS.Desktop;
using HammerOS.Apps;
using Avalonia.VisualTree;
using Avalonia.Input;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var output = Path.GetFullPath(args.FirstOrDefault() ?? "dist/preview"); Directory.CreateDirectory(output);
var state = new SystemState(persistent: false); state.Preferences.Motion = false;
// --palette <Name> renders the same set in another desktop palette.
if (Array.IndexOf(args, "--palette") is var paletteArg and >= 0 && paletteArg + 1 < args.Length) state.Preferences.Wallpaper = args[paletteArg + 1];
var shell = new ShellWindow(state, true); shell.Show(); Pump(); shell.StartDesktop(); Pump();
Save("desktop");
if (args.Contains("--wallpapers"))
{
    foreach (var frame in shell.Frames.ToArray()) shell.RemoveFrame(frame);
    var backdrop = shell.GetVisualDescendants().OfType<DesktopBackdrop>().Single();
    foreach (var scene in DesktopBackdrop.Scenes) { state.Preferences.WallpaperScene = scene; state.Save(); backdrop.PreviewTime = 8; Pump(); Save("wallpaper-" + scene.ToLowerInvariant()); }
    backdrop.PreviewTime = 18; Pump(); Save("wallpaper-waves-later");
    shell.OpenApp("settings"); Pump(); var settings = shell.Frames.Single().AppContent; settings.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Vector(0, 420); Pump(); Save("wallpaper-settings"); shell.Close(); return;
}
if (args.Contains("--spotlight"))
{
    shell.ToggleSpotlight(); Pump(); Save("spotlight");
    var palette = shell.GetVisualDescendants().OfType<CommandPalette>().Single(); palette.Search.Text = "toggle"; Pump(); Save("spotlight-actions");
    palette.Search.Text = "> Get-Date"; Pump(); Save("spotlight-command");
    palette.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump();
    var task = shell.GetVisualDescendants().OfType<Button>().First(x => x.Name == "TaskbarApp"); task.ContextMenu!.Open(task); Pump(); Save("taskbar-menu"); task.ContextMenu.Close(); shell.Close(); return;
}
if (args.Contains("--editor"))
{
    foreach (var frame in shell.Frames.ToArray()) shell.RemoveFrame(frame);
    shell.OpenApp("editor"); var editor = (EditorView)shell.Frames.Single().AppContent;
    editor.Editor.Text = "A place for your thoughts.\n\nEvery idea begins with a few words.\n\nWrite. Refine. Make something yours.\n\nCevapsız Sorular — notes"; editor.Editor.Focus(); editor.Editor.SelectionStart = 0; editor.Editor.SelectionEnd = 25; Pump(); Save("editor-focused");
    var menuPoint = editor.Editor.TranslatePoint(new Point(210, 90), shell)!.Value; shell.MouseMove(menuPoint); editor.Editor.ContextMenu!.Open(editor.Editor); Pump(); Save("editor-context-menu"); editor.Editor.ContextMenu.Close();
    shell.Toast("Unable to open C:\\Users\\Operator\\Music\\Cevapsız Sorular - maNga.mp3. Windows has no application associated with this file type."); Pump(); Save("notification");
    editor.Editor.Text = ""; shell.Close(); return;
}
if (args.Contains("--chrome"))
{
    foreach (var other in shell.Frames.Where(x => x.AppId != "terminal").ToArray()) shell.RemoveFrame(other);
    var frame = shell.Frames.Single(); frame.SetPosition(160, 85); Pump();
    var terminal = (TerminalView)frame.AppContent;
    var button = terminal.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "TerminalMenu");
    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
    var menu = (ContextMenu)typeof(TerminalView).GetField("_sessionMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(terminal)!;
    Pump(); Save("terminal-controls");
    menu.Close(); var close = terminal.GetVisualDescendants().OfType<Button>().First(x => x.Classes.Contains("terminal-close")); var at = close.TranslatePoint(new Point(11, 11), shell)!.Value; shell.MouseMove(at); Pump(); Save("terminal-hover");
    shell.RemoveFrame(frame); shell.OpenApp("tiled"); Pump(); Save("tiled-toolbar");
    var combo = shell.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "TiledLayout"); combo.IsDropDownOpen = true; Pump();
    var comboPopup = combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First();
    Save("tiled-menu");
    combo.IsDropDownOpen = false;
    shell.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "ClockMenu").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); Save("calendar-popup");
    shell.Close(); Console.WriteLine("Rendered terminal, tiled, and calendar chrome previews."); return;
}
if (args.Contains("--host"))
{
    foreach (var frame in shell.Frames.Where(x => x.AppId != "terminal").ToArray()) shell.RemoveFrame(frame);
    var terminalFrame = shell.Frames.Single(); terminalFrame.Width = 1000; terminalFrame.Height = 595; terminalFrame.SetPosition(190, 90); Pump();
    ((TerminalView)terminalFrame.AppContent).Select(true);
    WaitUntil(() => terminalFrame.GetVisualDescendants().OfType<TerminalSurface>().FirstOrDefault()?.Session?.Emulator.DumpScreen().Contains('>') == true);
    var surface = terminalFrame.GetVisualDescendants().OfType<TerminalSurface>().Single();
    surface.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Write-Output ('HOST_' + 'UI_OK')" });
    surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    WaitUntil(() => surface.Session!.Emulator.DumpScreen().Contains("HOST_UI_OK"));
    surface.Session!.Send("Get-Process -Id $PID | Select-Object Id, ProcessName; Write-Output ''; Write-Output 'One workspace. Room to explore.'\r");
    WaitUntil(() => surface.Session.Emulator.DumpScreen().Contains("ProcessName"));
    for (var i = 0; i < 20; i++) { Thread.Sleep(16); Pump(); }
    Save("host-terminal"); var completion = surface.Session.Completion; shell.Close(); completion.Wait(TimeSpan.FromSeconds(10));
    Console.WriteLine("Real terminal UI input, rendering, and shutdown verified."); return;
}
foreach (var id in new[] { "files", "tiled", "terminal", "browser", "sudoku", "hacker", "editor", "web", "media", "calendar", "settings", "network", "control", "notes", "wellness", "handbook" })
{
    foreach (var frame in shell.Frames.ToArray()) shell.RemoveFrame(frame);
    shell.OpenApp(id); Pump();
    if (id == "files") { var files = (FilesView)shell.Frames.Single().AppContent; files.Local.Navigate(Environment.CurrentDirectory); WaitUntil(() => files.Local.Loading.IsCompleted); }
    Save(id);
}
foreach (var frame in shell.Frames.ToArray()) shell.RemoveFrame(frame);
shell.OpenApp("refinement"); shell.ToggleLauncher(); Pump(); Save("launcher"); shell.ToggleLauncher(); shell.Lock(); Pump(); Save("lock"); shell.Unlock();
shell.Width = 960; shell.Height = 680; Pump(); Save("compact"); shell.Close();
Console.WriteLine("Rendered native Avalonia previews to " + output);
void Pump() { Dispatcher.UIThread.RunJobs(); shell.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
void Save(string name) { Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)shell.Width, (int)shell.Height), new Vector(96, 96)); bitmap.Render(shell); bitmap.Save(Path.Combine(output, name + ".png")); }
void WaitUntil(Func<bool> condition) { var started = System.Diagnostics.Stopwatch.StartNew(); while (!condition()) { Thread.Sleep(16); Pump(); if (started.Elapsed.TotalSeconds > 20) { shell.Close(); throw new TimeoutException("Native terminal UI did not reach the expected state."); } } }
