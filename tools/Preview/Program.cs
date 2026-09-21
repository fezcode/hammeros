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
var shell = new ShellWindow(state, true); shell.Show(); Pump(); shell.StartDesktop(); Pump();
Save("desktop");
if (args.Contains("--host"))
{
    foreach (var frame in shell.Frames.Where(x => x.AppId != "terminal").ToArray()) shell.RemoveFrame(frame);
    shell.OpenApp("terminal"); var terminalFrame = shell.Frames.Single(); terminalFrame.Width = 1000; terminalFrame.Height = 595; terminalFrame.SetPosition(190, 90); Pump();
    ((TerminalView)terminalFrame.AppContent).Select(true);
    WaitUntil(() => terminalFrame.GetVisualDescendants().OfType<TerminalSurface>().FirstOrDefault()?.Session?.Emulator.DumpScreen().Contains('>') == true);
    var surface = terminalFrame.GetVisualDescendants().OfType<TerminalSurface>().Single();
    surface.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Write-Output ('HOST_' + 'UI_OK')" });
    surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    WaitUntil(() => surface.Session!.Emulator.DumpScreen().Contains("HOST_UI_OK"));
    surface.Session!.Send("Get-Process -Id $PID | Select-Object Id, ProcessName; Write-Output ''; Write-Output 'The work is mysterious and important.'\r");
    WaitUntil(() => surface.Session.Emulator.DumpScreen().Contains("ProcessName"));
    for (var i = 0; i < 20; i++) { Thread.Sleep(16); Pump(); }
    Save("host-terminal"); var completion = surface.Session.Completion; shell.Close(); completion.Wait(TimeSpan.FromSeconds(10));
    Console.WriteLine("Real terminal UI input, rendering, and shutdown verified."); return;
}
foreach (var id in new[] { "files", "browser", "settings", "network", "control", "notes", "wellness", "handbook" })
{
    foreach (var frame in shell.Frames.ToArray()) shell.RemoveFrame(frame);
    shell.OpenApp(id); Pump(); Save(id);
}
foreach (var frame in shell.Frames.ToArray()) shell.RemoveFrame(frame);
shell.OpenApp("refinement"); shell.ToggleLauncher(); Pump(); Save("launcher"); shell.ToggleLauncher(); shell.Lock(); Pump(); Save("lock"); shell.Unlock();
shell.Width = 960; shell.Height = 680; Pump(); Save("compact"); shell.Close();
Console.WriteLine("Rendered native Avalonia previews to " + output);
void Pump() { Dispatcher.UIThread.RunJobs(); shell.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
void Save(string name) { Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)shell.Width, (int)shell.Height), new Vector(96, 96)); bitmap.Render(shell); bitmap.Save(Path.Combine(output, name + ".png")); }
void WaitUntil(Func<bool> condition) { var started = System.Diagnostics.Stopwatch.StartNew(); while (!condition()) { Thread.Sleep(16); Pump(); if (started.Elapsed.TotalSeconds > 20) { shell.Close(); throw new TimeoutException("Native terminal UI did not reach the expected state."); } } }
