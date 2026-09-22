using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HammerOS.Desktop;
using Microsoft.Web.WebView2.Core;

namespace HammerOS.Apps;

public sealed class MediaView : UserControl, INativeWorkspace, IDisposable
{
    public static readonly string[] SupportedExtensions = [".mp4", ".webm", ".m4v", ".mp3", ".wav", ".ogg", ".m4a", ".flac"];
    public static bool Supports(string path) => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public string? CurrentFile => _pending;
    public WebSurface Web { get; } = new();
    private readonly TextBlock _title = Ui.Text("Media player", 16), _status = Ui.Text("Open a video or audio file.", 12, Ui.Muted);
    private string? _pending;
    public MediaView()
    {
        FileDrop.Attach(this, paths => paths.Length == 1 && Supports(paths[0]), paths => OpenFile(paths[0]));
        ToolTip.SetTip(_title, "Drop an audio or video file on this toolbar to play it.");
        _title.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var root = new Grid { RowDefinitions = new RowDefinitions("54,*,32") };
        root.Children.Add(new Border { Padding = new Thickness(18, 0), Child = Ui.Columns("*,Auto", _title, Ui.Button("Open media", async () => { var top = TopLevel.GetTopLevel(this); if (top is null) return; var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Play audio or video", FileTypeFilter = [new("Media files") { Patterns = ["*.mp4", "*.webm", "*.m4v", "*.mp3", "*.wav", "*.ogg", "*.m4a", "*.flac"] }] }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } file) OpenFile(file); }, "primary")) });
        var body = new Panel { Background = Ui.Dark, Children = { Ui.Pad(Ui.Stack(20, Ui.Text("Press play.", 34, Ui.Phosphor), Ui.Text("Video and audio, inside your workspace.", 13, Ui.Phosphor))), Web } }; Grid.SetRow(body, 1); root.Children.Add(body); var status = Ui.Pad(_status, 8); Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        Web.Failed += error => _status.Text = error;
        Web.Ready += core => { core.Settings.AreDefaultContextMenusEnabled = true; core.Settings.IsStatusBarEnabled = false; core.WebMessageReceived += (_, e) => { _status.Text = e.TryGetWebMessageAsString(); }; if (_pending is not null) OpenFile(_pending); else core.NavigateToString(EmptyPage); };
    }
    private const string EmptyPage = "<html><body style='margin:0;padding:60px;background:#102c32;color:#b3e5da;font:15px Segoe UI'><small>HAMMER MEDIA</small><h1 style='font:42px Georgia'>Press play.</h1><p>Open a video or audio file to begin.</p></body></html>";
    public void OpenFile(string path)
    {
        if (!File.Exists(path)) { _status.Text = "File not found."; return; } _pending = Path.GetFullPath(path); _title.Text = Path.GetFileName(path); _status.Text = "Loading media…";
        if (Web.Core is not { } core) return;
        core.SetVirtualHostNameToFolderMapping("media.hammer.local", Path.GetDirectoryName(_pending)!, CoreWebView2HostResourceAccessKind.DenyCors);
        var source = JsonSerializer.Serialize("https://media.hammer.local/" + Uri.EscapeDataString(Path.GetFileName(path)));
        core.NavigateToString("""
            <!doctype html><html><head><meta charset='utf-8'><style>
            *{box-sizing:border-box}body{margin:0;background:#102c32;color:#b3e5da;font:12px Consolas,monospace;height:100vh;display:flex;flex-direction:column}main{min-height:0;flex:1;display:grid;place-items:center;position:relative}video{width:100%;height:100%;position:absolute;object-fit:contain}#label{font:32px Georgia;color:#749d97}footer{background:#18363d;border-top:1px solid #35545a;padding:12px 18px;display:flex;gap:14px;align-items:center}button{background:transparent;color:#b3e5da;border:1px solid #537873;padding:8px 14px;cursor:pointer;font:12px Consolas}button:hover{background:#2a4b50}input{accent-color:#b3e5da}#seek{flex:1;min-width:30px}#volume{width:70px}#time{white-space:nowrap}</style></head>
            <body><main><div id='label'>HAMMER / MEDIA</div><video id='v' playsinline></video></main><footer><button id='play'>Play</button><input id='seek' type='range' min='0' max='1000' value='0' aria-label='Seek'><span id='time'>0:00 / 0:00</span><button id='mute'>Mute</button><input id='volume' type='range' min='0' max='1' step='.01' value='1' aria-label='Volume'></footer><script>
            const v=document.getElementById('v'),play=document.getElementById('play'),seek=document.getElementById('seek'),vol=document.getElementById('volume');
            const fmt=n=>Math.floor((n||0)/60)+':'+String(Math.floor((n||0)%60)).padStart(2,'0');
            play.onclick=()=>v.paused?v.play().catch(()=>{}):v.pause();v.onplay=()=>play.textContent='Pause';v.onpause=()=>play.textContent='Play';seek.oninput=()=>{if(isFinite(v.duration))v.currentTime=seek.value/1000*v.duration};vol.oninput=()=>v.volume=vol.value;document.getElementById('mute').onclick=e=>{v.muted=!v.muted;e.target.textContent=v.muted?'Unmute':'Mute'};v.ontimeupdate=()=>{seek.value=v.duration?v.currentTime/v.duration*1000:0;document.getElementById('time').textContent=fmt(v.currentTime)+' / '+fmt(v.duration)};v.onloadedmetadata=()=>{window.chrome.webview.postMessage('Ready · '+fmt(v.duration));v.play().catch(()=>{})};v.onerror=()=>window.chrome.webview.postMessage('This format or codec could not be played. Try MP4, WebM, MP3 or WAV.');
            """ + "v.src=" + source + ";</script></body></html>");
    }
    public void SetWorkspaceActive(bool active) => Web.SetActive(active);
    public void Dispose() => Web.Dispose();
}
