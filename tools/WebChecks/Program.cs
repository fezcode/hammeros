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
        Window? window = null; BrowserView? browser = null; MediaView? media = null; PdfView? pdf = null;
        var file = Path.Combine(Path.GetTempPath(), "HammerOS Cevapsız & #audio-" + Guid.NewGuid().ToString("N") + ".wav");
        var pdfFile = Path.Combine(Path.GetTempPath(), "HammerOS PDF & test-" + Guid.NewGuid().ToString("N") + ".pdf");
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
            Check("Browser tabs keep independent native pages alive", () => {
                browser.AddTab($"http://127.0.0.1:{port}/first"); Until(() => browser.Web.Core?.DocumentTitle == "First test page"); var other = browser.Web.Core!;
                Await(other.ExecuteScriptAsync("document.getElementById('entry').value='second tab'")); browser.Select(0); Pump(150);
                Require(ReferenceEquals(browser.Web.Core, core) && Await(core.ExecuteScriptAsync("document.getElementById('entry').value")) == "\"kept alive\"", "First tab lost its page");
                browser.Select(1); Pump(150); Require(Await(other.ExecuteScriptAsync("document.getElementById('entry').value")) == "\"second tab\"", "Second tab lost its page"); browser.CloseTab(1); Require(browser.TabUris.Count == 1, "Tab did not close");
            });
            Check("Native browser shortcut routing creates, switches and closes tabs", () => {
                Require(browser.Web.DispatchShortcut(Key.T, KeyModifiers.Control), "New-tab shortcut was not routed"); Until(() => browser.TabUris.Count == 2 && browser.Web.Core is not null);
                Require(browser.Web.DispatchShortcut(Key.Tab, KeyModifiers.Control), "Switch-tab shortcut was not routed"); Until(() => browser.ActiveIndex == 0);
                Require(browser.Web.DispatchShortcut(Key.W, KeyModifiers.Control), "Close-tab shortcut was not routed"); Until(() => browser.TabUris.Count == 1);
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
            Check("PDF Reader serves a real local PDF to the embedded browser", () => {
                var contents = "%PDF-1.4\n"; var offsets = new List<int> { 0 };
                var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>" };
                for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(contents)); contents += $"{i + 1} 0 obj\n{objects[i]}\nendobj\n"; }
                var xref = Encoding.ASCII.GetByteCount(contents); contents += "xref\n0 4\n0000000000 65535 f \n" + string.Join("", offsets.Skip(1).Select(x => x.ToString("D10") + " 00000 n \n")) + $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF";
                File.WriteAllText(pdfFile, contents, Encoding.ASCII); pdf = new PdfView(); window.Content = pdf; Until(() => pdf.Web.Core is not null); bool? complete = null; pdf.Web.Core!.NavigationCompleted += (_, e) => complete = e.IsSuccess; pdf.Load(pdfFile); Until(() => complete is not null); Require(complete == true && pdf.Web.Core.Source.Contains("pdf.hammer.local/") && pdf.FilePath == pdfFile, "PDF navigation failed");
            });
            Console.WriteLine($"\n{passed} browser and media checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { browser?.Dispose(); media?.Dispose(); pdf?.Dispose(); window?.Close(); cancel.Cancel(); listener.Stop(); if (File.Exists(file)) File.Delete(file); if (File.Exists(pdfFile)) File.Delete(pdfFile); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Until(Func<bool> ready) { var watch = Stopwatch.StartNew(); while (!ready()) { Pump(50); if (watch.Elapsed.TotalSeconds > 25) throw new TimeoutException("Native web view did not become ready"); } }
    private static T Await<T>(Task<T> task) { Until(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Pump(int milliseconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += (_, _) => frame.Continue = false; timer.Start(); Dispatcher.UIThread.PushFrame(frame); timer.Stop(); }
}
