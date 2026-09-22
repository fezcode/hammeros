using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HammerOS;
using HammerOS.Apps;
using HammerOS.Core;
using HammerOS.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--fixture")) return Fixture();
        var fixtures = new List<(Process Process, DesktopWindow Window)>();
        Window? shell = null; TiledView? tiled = null; var passed = 0;
        void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS  " + name); }
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting(); Motion.Enabled = false;
            for (var i = 0; i < 2; i++)
            {
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("--fixture");
                var process = Process.Start(start)!;
                var line = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!;
                var handle = new IntPtr(long.Parse(line)); fixtures.Add((process, new(handle, process.Id, "HammerOS test app", "TiledChecks")));
            }
            var first = fixtures[0].Window; var second = fixtures[1].Window;
            var originalStyle = NativeWindows.GetWindowLongPtr(first.Handle, -16);
            NativeWindows.GetWindowRect(first.Handle, out var originalRect);
            var errors = new List<string>(); tiled = new TiledView(errors.Add);
            shell = new Window { Width = 1050, Height = 680, Content = tiled, Title = "HammerOS native workspace checks" }; shell.Show(); Pump(100);
            Check("Exact external windows are discoverable", () => Require(NativeWindows.Enumerate().Any(x => x.Handle == first.Handle), "Fixture missing from window chooser"));
            Check("External window embeds in Avalonia native host", () => { tiled.Attach(first); Pump(300); Require(tiled.TileCount == 1 && tiled.Attachments[0].IsAttached, string.Join("; ", errors)); Require(NativeWindows.GetParent(first.Handle) == tiled.Attachments[0].Container, "Window parent not changed"); });
            Check("Two independent apps occupy distinct tiles", () => { tiled.Attach(second); Pump(500); Require(tiled.TileCount == 2 && tiled.Attachments.All(x => x.IsAttached), string.Join("; ", errors)); NativeWindows.GetWindowRect(first.Handle, out var a); NativeWindows.GetWindowRect(second.Handle, out var b); Require(a.Right <= b.Left || b.Right <= a.Left || a.Bottom <= b.Top || b.Bottom <= a.Top, $"Native windows overlap: {a.Left},{a.Top},{a.Right},{a.Bottom} / {b.Left},{b.Top},{b.Right},{b.Bottom}"); });
            Check("Native child resizes with its workspace", () => { NativeWindows.GetWindowRect(first.Handle, out var before); shell.Width = 850; Pump(300); NativeWindows.GetWindowRect(first.Handle, out var after); Require(after.Right - after.Left < before.Right - before.Left, "Child did not resize"); });
            Check("Moving tiles reorders real application windows", () => { tiled.MoveTile(0, 1); Pump(250); NativeWindows.GetWindowRect(first.Handle, out var a); NativeWindows.GetWindowRect(second.Handle, out var b); Require(b.Right <= a.Left, "Window order did not change"); tiled.MoveTile(1, 0); Pump(150); });
            Check("Tile divider changes native application size", () => { var splitter = tiled.GetVisualDescendants().OfType<GridSplitter>().Single(x => x.Name == "TiledColumnDivider"); NativeWindows.GetWindowRect(first.Handle, out var before); splitter.Focus(); splitter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right }); Pump(250); NativeWindows.GetWindowRect(first.Handle, out var after); Require(after.Right - after.Left != before.Right - before.Left, "Divider did not change native size"); });
            Check("Focus tabs show only the selected live app", () => { tiled.SetLayout("Focus"); tiled.FocusTile(0); Pump(250); Require(IsWindowVisible(first.Handle) && !IsWindowVisible(second.Handle), "Focus mode shows multiple apps"); var tabs = tiled.GetVisualDescendants().OfType<Button>().Where(x => x.Name == "TiledFocusTab").ToArray(); Require(tabs.Length == 2, "Focus tabs missing"); tabs[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(250); Require(!IsWindowVisible(first.Handle) && IsWindowVisible(second.Handle), "Focus tab did not switch apps"); tiled.SetLayout("Grid"); Pump(150); });
            Check("Closing requires confirmation and cancellation keeps apps attached", () => { var confirmed = false; tiled.ConfirmClose(() => confirmed = true); Pump(100); Require(!confirmed && tiled.Attachments.All(x => x.IsAttached), "Apps detached before confirmation"); tiled.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Keep workspace open").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100); Require(!confirmed && IsWindowVisible(first.Handle), "Cancel did not restore workspace"); tiled.ConfirmClose(() => confirmed = true); Pump(100); tiled.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Close tiled").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(confirmed, "Close confirmation did not proceed"); });
            Check("Inactive workspace hides native app surfaces", () => { tiled.SetWorkspaceActive(false); Pump(150); Require(!IsWindowVisible(first.Handle), "Native child is still visible"); tiled.SetWorkspaceActive(true); Pump(150); Require(IsWindowVisible(first.Handle), "Native child did not return"); });
            Check("Detach restores original parent, styles and position", () => { tiled.DetachAll(); Pump(150); Require(NativeWindows.GetParent(first.Handle) == IntPtr.Zero, "Parent was not restored"); Require(NativeWindows.GetWindowLongPtr(first.Handle, -16) == originalStyle, "Window styles changed"); NativeWindows.GetWindowRect(first.Handle, out var restored); Require(Math.Abs(restored.Left - originalRect.Left) <= 1 && Math.Abs(restored.Top - originalRect.Top) <= 1 && Math.Abs((restored.Right - restored.Left) - (originalRect.Right - originalRect.Left)) <= 1, "Position was not restored"); Require(!fixtures[0].Process.HasExited && !fixtures[1].Process.HasExited, "Detach ended an application"); });
            Check("Disposing tiled releases live apps safely", () => { tiled.Attach(first); tiled.Attach(second); Pump(200); tiled.Dispose(); Pump(150); Require(NativeWindows.GetParent(first.Handle) == IntPtr.Zero && NativeWindows.GetParent(second.Handle) == IntPtr.Zero && fixtures.All(x => !x.Process.HasExited), "Closing workspace did not restore apps"); });
            Console.WriteLine($"\n{passed} native workspace checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            tiled?.Dispose(); shell?.Close();
            foreach (var fixture in fixtures) { NativeWindows.RequestClose(fixture.Window.Handle); if (!fixture.Process.WaitForExit(3000)) fixture.Process.Kill(true); fixture.Process.Dispose(); }
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false; timer.Start(); Dispatcher.UIThread.PushFrame(frame); timer.Stop();
    }
    private static int Fixture()
    {
        var hwnd = CreateWindowEx(0, "STATIC", "HammerOS test app", 0x10CF0000, 100, 100, 500, 350, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine(hwnd.ToInt64()); Console.Out.Flush();
        while (NativeWindows.Exists(hwnd)) { while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); } Thread.Sleep(5); }
        return 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Hwnd; public uint Msg; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
