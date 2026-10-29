using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class NotificationView : UserControl, IDisposable
{
    private readonly ShellWindow _shell; private readonly StackPanel _list = Ui.Stack(12);
    public NotificationView(ShellWindow shell)
    {
        _shell = shell;
        var root = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(24) };
        root.Children.Add(Ui.Stack(18, Ui.Heading("NOTIFICATION CENTER", "Your workspace, in view.", "Recent activity and file transfers."), Ui.Row(8, Ui.Button("Mark all read", () => { foreach (var n in shell.State.Workspace.Notifications) n.Read = true; shell.State.Save(); }), Ui.Button("Clear completed", () => { shell.State.Workspace.Notifications.Clear(); shell.Transfers.Jobs.RemoveAll(x => x.Finished); shell.State.Save(); }), Ui.Button("Undo transfer", async () => { try { await shell.Transfers.UndoLast(); Refresh(); } catch (Exception ex) { shell.Toast(ex.Message); } })), Ui.Rule()));
        var scroll = Ui.Scroll(_list); scroll.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
        shell.State.Changed += Refresh; shell.Transfers.Changed += Refresh; Refresh();
    }
    private void Refresh()
    {
        _list.Children.Clear(); foreach (var job in _shell.Transfers.Jobs.Take(20)) { var text = Ui.Text($"{job.Label}\n{job.Status} · {job.Done}/{job.Total}", 12); text.TextWrapping = TextWrapping.Wrap; _list.Children.Add(Ui.Columns("*,Auto", text, Ui.Button(job.Finished ? "Done" : "Cancel after item", () => job.Cancellation.Cancel()))); }
        foreach (var n in _shell.State.Workspace.Notifications.Take(100)) { var text = Ui.Text(n.Message, 13); text.TextWrapping = TextWrapping.Wrap; var b = Ui.Button("", () => { n.Read = true; _shell.State.Save(); if (n.Path is { } path) _ = _shell.OpenResource(path); else if (n.App is { } app) _shell.OpenApp(app); }); b.Content = Ui.Stack(6, Ui.Label(n.Time.ToString("dd MMM HH:mm") + (n.Read ? "" : " · NEW")), text); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Stretch; _list.Children.Add(b); }
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Text("All caught up.", 20));
    }
    public void Dispose() { _shell.State.Changed -= Refresh; _shell.Transfers.Changed -= Refresh; }
}
public sealed class TaskManagerView : UserControl, IDisposable
{
    private readonly ShellWindow _shell; private readonly StackPanel _list = Ui.Stack(12); private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) }; private readonly TextBlock _total = Ui.Label("");
    public TaskManagerView(ShellWindow shell)
    {
        _shell = shell; var root = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(24) };
        root.Children.Add(Ui.Stack(18, Ui.Heading("TASK MANAGER", "Everything running.", "App windows, owned terminal sessions, and borrowed Windows apps."), _total, Ui.Rule()));
        var scroll = Ui.Scroll(_list); scroll.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
        _timer.Tick += (_, _) => Refresh(); _timer.Start(); Refresh(); AttachedToVisualTree += (_, _) => Refresh();
    }
    private static string Usage(int pid) { try { using var p = Process.GetProcessById(pid); return $"PID {pid} · {p.WorkingSet64 / 1048576d:0.#} MB · CPU {p.TotalProcessorTime.TotalSeconds:0.#} s"; } catch { return "Process ended"; } }
    private void Refresh()
    {
        _total.Text = Usage(Environment.ProcessId) + " · HammerOS process total"; _list.Children.Clear();
        foreach (var frame in _shell.Frames.ToArray())
        {
            _list.Children.Add(Ui.Columns("*,Auto", Ui.Stack(4, Ui.Text(frame.DisplayName, 14), Ui.Label($"{_shell.State.Workspace.Desktops[Math.Clamp(frame.DesktopId, 0, _shell.State.Workspace.Desktops.Count - 1)]} · {(frame.IsMinimized ? "Minimized" : "Open")}")), Ui.Row(4, Ui.Button("Switch", frame.Restore), Ui.Button("Close", frame.Close))));
            if (frame.AppContent is TerminalView terminal) foreach (var tab in terminal.Tabs.ToArray()) if (tab.Content is HostTerminalView host && host.Session is { } session) _list.Children.Add(Ui.Columns("*,Auto", Ui.Text(tab.Title + " · " + Usage(session.ProcessId), 11), Ui.Button("End session", () => { terminal.CloseTab(tab); Refresh(); })));
            if (frame.AppContent is TiledView tiled) foreach (var item in tiled.Attachments.ToArray()) _list.Children.Add(Ui.Columns("*,Auto", Ui.Text(item.Window.Title + " · " + Usage(item.Window.ProcessId), 11), Ui.Button("Detach apps", () => { tiled.DetachAll(); Refresh(); })));
            _list.Children.Add(Ui.Rule());
        }
    }
    public void Dispose() => _timer.Stop();
}
public sealed class SystemCenterView : UserControl, IDisposable
{
    private readonly ShellWindow _shell; private readonly StackPanel _clips = Ui.Stack(8), _media = Ui.Stack(8); private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) }; private readonly TextBlock _power = Ui.Text("", 13);
    public SystemCenterView(ShellWindow shell)
    {
        _shell = shell; var volume = new Slider { Minimum = 0, Maximum = 100, Width = 280 }; var audioStatus = Ui.Text("Default Windows output", 12, Ui.Muted);
        try { volume.Value = SystemDevices.Volume * 100; } catch (Exception ex) { volume.IsEnabled = false; audioStatus.Text = ex.Message; }
        volume.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) try { SystemDevices.Volume = (float)(volume.Value / 100); } catch (Exception ex) { audioStatus.Text = ex.Message; } };
        var clipboard = new ToggleSwitch { IsChecked = shell.State.Workspace.ClipboardHistory, OnContent = "History on", OffContent = "History off" }; clipboard.IsCheckedChanged += (_, _) => { shell.State.Workspace.ClipboardHistory = clipboard.IsChecked == true; if (clipboard.IsChecked != true) shell.ClipboardItems.Clear(); shell.State.Save(); Refresh(); };
        var body = Ui.Stack(20, Ui.Heading("SYSTEM CENTER", "Connected to Windows.", "Audio, power, media, and clipboard controls."), _power, audioStatus, Ui.Row(8, volume, Ui.Button("Mute / unmute", () => { try { SystemDevices.Muted = !SystemDevices.Muted; } catch (Exception ex) { audioStatus.Text = ex.Message; } })), Ui.Row(8, Ui.Button("Audio devices", () => LocalFiles.Open("ms-settings:sound")), Ui.Button("Network", () => shell.OpenApp("network"))), Ui.Rule(), Ui.Label("MEDIA PLAYERS"));
        body.Children.Add(_media);
        body.Children.Add(Ui.Rule()); body.Children.Add(Ui.Label("CLIPBOARD · SESSION ONLY")); body.Children.Add(Ui.Text("Opt in to remember copied text. Clear it at any time; history is never saved to disk.", 11)); body.Children.Add(Ui.Row(8, clipboard, Ui.Button("Clear", () => { shell.ClipboardItems.Clear(); Refresh(); }))); body.Children.Add(_clips);
        Content = Ui.Scroll(Ui.Pad(body)); _timer.Tick += (_, _) => Refresh(); _timer.Start(); Refresh();
    }
    private void Refresh() {
        _power.Text = SystemDevices.Battery();
        _media.Children.Clear();
        foreach (var frame in _shell.Frames.Where(x => x.AppContent is MediaView)) { var player = (MediaView)frame.AppContent; var name = Path.GetFileName(player.CurrentFile); _media.Children.Add(Ui.Columns("*,Auto", Ui.Button(string.IsNullOrEmpty(name) ? "Media Player" : name, frame.Restore), Ui.Button("Play / pause", async () => { try { if (player.Web.Core is { } core) await core.ExecuteScriptAsync("document.getElementById('play')?.click()"); } catch (Exception ex) { _shell.Toast(ex.Message); } }))); }
        if (_media.Children.Count == 0) _media.Children.Add(Ui.Text("No media players open.", 12, Ui.Muted));
        _clips.Children.Clear(); foreach (var text in _shell.ClipboardItems.Take(20)) { var b = Ui.Button(text.Length > 90 ? text[..90] + "…" : text, async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(text); }); _clips.Children.Add(b); } }
    public void Dispose() => _timer.Stop();
}
public sealed class WorkspaceSettingsView : UserControl
{
    public WorkspaceSettingsView(ShellWindow shell)
    {
        var state = shell.State; var body = Ui.Stack(18, Ui.Heading("WORKSPACE", "Make it yours.", "Defaults, shortcuts, saved sessions, and profiles."));
        var restore = new ToggleSwitch { IsChecked = state.Workspace.RestoreSession, OnContent = "Restore session", OffContent = "Start fresh" }; restore.IsCheckedChanged += (_, _) => { state.Workspace.RestoreSession = restore.IsChecked == true; state.Save(); }; body.Children.Add(restore);
        body.Children.Add(Ui.Label("DEFAULT APPS")); var extension = new TextBox { Watermark = ".txt or .pdf", Width = 140 }; var choices = new Dictionary<string, string> { ["Text Editor"] = "editor", ["Media Player"] = "media", ["Image Viewer"] = "image", ["PDF Reader"] = "pdf", ["Archives"] = "archive", ["Windows default"] = "windows" }; var apps = new ComboBox { ItemsSource = choices.Keys.ToArray(), SelectedIndex = 0, Width = 180 }; var status = Ui.Text("Defaults apply inside HammerOS.", 12);
        var defaults = Ui.Stack(6);
        void RefreshDefaults() { defaults.Children.Clear(); foreach (var pair in state.Workspace.Associations.OrderBy(x => x.Key).ToArray()) defaults.Children.Add(Ui.Columns("*,Auto", Ui.Text(pair.Key + " → " + (choices.FirstOrDefault(x => x.Value == pair.Value).Key ?? pair.Value), 12), Ui.Button("Remove", () => { state.Workspace.Associations.Remove(pair.Key); state.Save(); RefreshDefaults(); }))); }
        body.Children.Add(Ui.Row(8, extension, apps, Ui.Button("Set default", () => { var ext = (extension.Text ?? "").Trim().ToLowerInvariant(); if (!ext.StartsWith('.') || ext.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { status.Text = "Use a file extension such as .txt"; return; } state.Workspace.Associations[ext] = choices[apps.SelectedItem as string ?? "Text Editor"]; state.Save(); status.Text = ext + " → " + apps.SelectedItem; RefreshDefaults(); }), Ui.Button("Reset", () => { state.Workspace.Associations.Clear(); state.Save(); status.Text = "Built-in defaults restored."; RefreshDefaults(); }))); body.Children.Add(status); body.Children.Add(defaults); RefreshDefaults();
        body.Children.Add(Ui.Rule()); body.Children.Add(Ui.Label("KEYBOARD SHORTCUTS"));
        foreach (var action in new[] { "Search", "Applications", "Next desktop", "Previous desktop", "Overview" }) { var input = new TextBox { Text = state.Workspace.Shortcuts.GetValueOrDefault(action), Width = 190 }; body.Children.Add(Ui.Columns("*,Auto", Ui.Text(action, 13), Ui.Row(6, input, Ui.Button("Apply", () => { try { var key = KeyGesture.Parse(input.Text ?? ""); var normalized = key.ToString(); if (state.Workspace.Shortcuts.Any(x => x.Key != action && KeyGesture.Parse(x.Value).ToString() == normalized)) throw new InvalidOperationException("Shortcut is already assigned."); state.Workspace.Shortcuts[action] = normalized; state.Save(); status.Text = "Shortcut saved."; } catch (Exception ex) { status.Text = ex.Message; } })))); }
        body.Children.Add(Ui.Rule()); body.Children.Add(Ui.Label("WORKSPACE PROFILES")); var name = new TextBox { Watermark = "Profile name", Width = 190 }; var profiles = Ui.Stack(8);
        void Refresh() { profiles.Children.Clear(); foreach (var p in state.Workspace.Profiles.ToArray()) profiles.Children.Add(Ui.Columns("*,Auto", Ui.Text(p.Name + " · " + p.Windows.Count + " windows", 13), Ui.Row(6, Ui.Button("Launch", () => shell.RestoreWindows(p.Windows, true)), Ui.Button("Delete", () => { state.Workspace.Profiles.Remove(p); state.Save(); Refresh(); })))); }
        body.Children.Add(Ui.Row(8, name, Ui.Button("Save current layout", () => { var value = name.Text?.Trim(); if (string.IsNullOrEmpty(value)) return; state.Workspace.Profiles.RemoveAll(x => x.Name == value); state.Workspace.Profiles.Add(new() { Name = value, Windows = shell.CaptureWindows(false) }); state.Save(); Refresh(); }))); body.Children.Add(profiles); Refresh(); Content = Ui.Scroll(Ui.Pad(body));
    }
}
