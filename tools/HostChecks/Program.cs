using System.Diagnostics;
using System.Text.RegularExpressions;
using HammerOS.Core;
using Clockt.Terminal;
using Clockt.Terminal.Input;

var passed = 0;
void Pass(string name) { passed++; Console.WriteLine("PASS  " + name); }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
async Task Until(Func<bool> condition, string message, int milliseconds = 15000)
{ var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException(message); await Task.Delay(50); } }
bool Alive(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
try
{
    var model = new TerminalEmulator(80, 24);
    model.Feed("\x1b[31mRED\x1b[0m\r\nUnicode: Ω 界\r\n");
    Require(model.Screen[0].Cells[0].Style.Fg.Index == 1, "ANSI foreground missing");
    Require(model.DumpScreen().Contains("Ω 界"), "Unicode did not round trip"); Pass("ANSI color and Unicode rendering model");
    model.Feed("\x1b[?1049hALTERNATE\x1b[?1049l"); Require(model.DumpScreen().Contains("RED"), "Primary screen lost"); Pass("Alternate-screen restoration");
    var paste = InputEncoder.EncodePaste("hello\x1b[201~world", true); Require(System.Text.Encoding.UTF8.GetString(paste) == "\x1b[200~helloworld\x1b[201~", "Bracketed paste escaped"); Pass("Bracketed paste control filtering");
    using var session = new HostTerminalSession(100, 26);
    using (var launched = new HostTerminalSession(initialCommand: "Write-Output ('PALETTE_' + 'Cevapsız & Ω'); $paletteValue = 42"))
    {
        await Until(() => launched.Emulator.DumpScreen().Contains("PALETTE_Cevapsız & Ω"), "Palette command did not execute intact");
        await Until(() => launched.Emulator.DumpScreen().Contains('>'), "Palette terminal did not remain interactive");
        launched.Send("Write-Output ('VALUE_' + $paletteValue)\r"); await Until(() => launched.Emulator.DumpScreen().Contains("VALUE_42"), "Palette shell state was lost");
        launched.Dispose(); await launched.Completion.WaitAsync(TimeSpan.FromSeconds(10)); Pass("Palette startup commands preserve Unicode and leave an interactive shell");
    }
    string Dump() => session.Emulator.DumpScreen(100);
    await Until(() => Dump().Contains('>'), "PowerShell did not show a prompt"); Pass("Real PowerShell starts inside ConPTY");
    using (var parallel = new HostTerminalSession(80, 24, Path.GetTempPath(), "cmd"))
    {
        await Until(() => parallel.Emulator.DumpScreen().Contains('>'), "Command Prompt did not start");
        parallel.Send("echo CMD_INDEPENDENT\r"); await Until(() => parallel.Emulator.DumpScreen().Contains("CMD_INDEPENDENT"), "CMD command missing");
        Require(!Dump().Contains("CMD_INDEPENDENT"), "Separate terminal outputs mixed");
        parallel.Send("cd\r"); await Until(() => parallel.Emulator.DumpScreen().Contains(Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), "Working directory not applied");
        parallel.Dispose(); await parallel.Completion.WaitAsync(TimeSpan.FromSeconds(10)); Require(Alive(session.ProcessId), "Closing one tab ended another shell");
        Pass("CMD and PowerShell run independently with a requested working directory");
    }
    session.Send("Write-Output ('HAMMER_' + 'READY')\r"); await Until(() => Dump().Contains("HAMMER_READY"), "Command was not executed"); Pass("Commands execute and return real output");
    session.Resize(110, 33); session.Send("Write-Output ('WIDTH:' + $Host.UI.RawUI.WindowSize.Width)\r"); await Until(() => Dump().Contains("WIDTH:110"), "Shell did not receive its resize"); Pass("ConPTY size reaches the child shell");
    session.Send("Write-Output ([char]0x03A9 + 'MEGA')\r"); await Until(() => Dump().Contains("ΩMEGA"), "UTF-8 output missing"); Pass("Real Unicode output survives the pipe");
    session.Send("Start-Sleep -Seconds 30\r"); await Task.Delay(500); session.Send([3]); await Task.Delay(150);
    session.Send("Write-Output ('INTERRUPT_' + 'OK')\r"); await Until(() => Dump().Contains("INTERRUPT_OK"), "Ctrl+C did not interrupt the command"); Pass("Ctrl+C interrupts a running command");
    session.Send("$child = Start-Process -FilePath powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile','-Command','Start-Sleep 90' -PassThru; Write-Output ('CHILD:' + $child.Id)\r");
    await Until(() => Regex.IsMatch(Dump(), @"CHILD:\d+"), "Child process did not start");
    var childPid = int.Parse(Regex.Match(Dump(), @"CHILD:(\d+)").Groups[1].Value); Require(Alive(childPid), "Test child already exited");
    session.Dispose(); await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
    await Until(() => !Alive(childPid) && !Alive(session.ProcessId), "Owned processes survived shutdown"); Pass("Closing a session terminates the shell and its child processes");
    using var second = new HostTerminalSession(80, 24);
    await Until(() => second.Emulator.DumpScreen().Contains('>'), "Second shell did not start"); second.Send("exit 7\r");
    var code = await second.Completion.WaitAsync(TimeSpan.FromSeconds(10)); Require(code == 7, $"Wrong exit code: {code}"); Pass("Natural shell exit drains output and reports its exit code");
    Console.WriteLine($"\n{passed} host terminal checks passed.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
