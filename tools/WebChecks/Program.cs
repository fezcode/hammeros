using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HammerOS;
using HammerOS.Apps;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        Window? window = null; BrowserView? browser = null; MediaView? media = null;
        var file = Path.Combine(Path.GetTempPath(), "HammerOS Cevapsız & #audio-" + Guid.NewGuid().ToString("N") + ".wav");
        using var cancel = new CancellationTokenSource(); var listener = new TcpListener(IPAddress.Loopback, 0); var passed = 0;
        void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS  " + name); }
        try
        {
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            async Task Serve(TcpClient client)
            {
                using (client) try { using var stream = client.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true); var line = await reader.ReadLineAsync(cancel.Token); while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancel.Token))) { } var second = line?.Contains("/second") == true; var html = $"<html><head><title>{(second ? "Second" : "First")} test page</title></head><body style='background:#102c32;color:#b3e5da;font:20px monospace'><h1>HammerOS browser check</h1><input id='entry' value='retained'><p>Real HTML, JavaScript, and local HTTP navigation.</p></body></html>"; var body = Encoding.UTF8.GetBytes(html); var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(header); await stream.WriteAsync(body); } catch (Exception) when (cancel.IsCancellationRequested) { }
            }
            _ = Task.Run(async () => { while (!cancel.IsCancellationRequested) { try { var client = await listener.AcceptTcpClientAsync(cancel.Token); _ = Serve(client); } catch (Exception) when (cancel.IsCancellationRequested) { break; } } });
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
            browser = new BrowserView(); string? error = null; browser.Web.Failed += message => error = message;
            window = new Window { Width = 950, Height = 640, Title = "HammerOS web engine checks", Content = browser }; window.Show();
            Check("WebView2 native controller starts", () => { Until(() => browser.Web.Core is not null || error is not null); Require(error is null, error ?? "Controller missing"); });
            var core = browser.Web.Core!;
            Check("Browser loads real HTTP pages and executes JavaScript", () => { browser.Navigate($"http://127.0.0.1:{port}/first"); Until(() => core.DocumentTitle == "First test page"); Require(Await(core.ExecuteScriptAsync("document.getElementById('entry').value")) == "\"retained\"", "DOM was not loaded"); });
            Check("Back and forward navigate embedded history", () => { browser.Navigate($"http://127.0.0.1:{port}/second"); Until(() => core.DocumentTitle == "Second test page"); core.GoBack(); Until(() => core.DocumentTitle == "First test page"); core.GoForward(); Until(() => core.DocumentTitle == "Second test page"); });
            Check("Hiding and restoring the browser preserves its live page", () => { Await(core.ExecuteScriptAsync("document.getElementById('entry').value='kept alive'")); browser.SetWorkspaceActive(false); Pump(100); browser.SetWorkspaceActive(true); Pump(200); Require(Await(core.ExecuteScriptAsync("document.getElementById('entry').value")) == "\"kept alive\"", "Browser state was lost"); });
            Check("WebView right-click uses the HammerOS context menu", () => {
                Await(core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mousePressed\",\"x\":350,\"y\":230,\"button\":\"right\",\"clickCount\":1}"));
                Await(core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mouseReleased\",\"x\":350,\"y\":230,\"button\":\"right\",\"clickCount\":1}"));
                Until(() => browser.Web.ActiveContextMenu?.IsOpen == true); var menu = browser.Web.ActiveContextMenu!;
                Require(menu.Items.OfType<MenuItem>().Any() && menu.Background?.ToString() == "#ffecede0", "Browser context menu does not use the shared theme"); menu.Close(); Pump(100);
                Require(Await(core.ExecuteScriptAsync("document.getElementById('entry').value")) == "\"kept alive\"", "Closing the context menu disrupted the page");
            });
            using (var writer = new BinaryWriter(File.Create(file))) { writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 32000); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(32000); writer.Write(new byte[32000]); }
            media = new MediaView(); window.Content = media; media.OpenFile(file); Until(() => media.Web.Core is not null);
            var player = media.Web.Core!;
            Check("Windows storage file drops reach Media Player", () => {
                using var item = Await(window.StorageProvider.TryGetFileFromPathAsync(new Uri(file)))!; Require(item is not null, "Windows storage file not available");
                var data = new DataObject(); data.Set(DataFormats.Files, new[] { item });
                media.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, media, new Point(20, 20), KeyModifiers.None)); Require(media.CurrentFile == file, "Dropped file was not opened");
            });
            Check("Media player loads actual local audio", () => { var watch = Stopwatch.StartNew(); while (Await(player.ExecuteScriptAsync("!!document.getElementById('v') && document.getElementById('v').readyState>=2")) != "true") { Pump(100); if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Audio did not load"); } Require(Await(player.ExecuteScriptAsync("document.getElementById('v').duration")) == "2", "Wrong audio duration"); });
            Check("Player controls pause, play, seek, and change volume", () => { Await(player.ExecuteScriptAsync("v.pause();document.getElementById('play').click();")); Pump(150); Require(Await(player.ExecuteScriptAsync("v.paused")) == "false", "Play button did not play"); Await(player.ExecuteScriptAsync("document.getElementById('play').click();v.currentTime=1;v.volume=.3;")); Pump(100); Require(Await(player.ExecuteScriptAsync("v.paused && v.currentTime>=.9 && v.volume===.3")) == "true", "Media controls failed"); });
            Console.WriteLine($"\n{passed} browser and media checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { browser?.Dispose(); media?.Dispose(); window?.Close(); cancel.Cancel(); listener.Stop(); if (File.Exists(file)) File.Delete(file); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Until(Func<bool> ready) { var watch = Stopwatch.StartNew(); while (!ready()) { Pump(50); if (watch.Elapsed.TotalSeconds > 25) throw new TimeoutException("Native web view did not become ready"); } }
    private static T Await<T>(Task<T> task) { Until(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Pump(int milliseconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += (_, _) => frame.Continue = false; timer.Start(); Dispatcher.UIThread.PushFrame(frame); timer.Stop(); }
}
