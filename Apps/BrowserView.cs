using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HammerOS.Desktop;
using Microsoft.Web.WebView2.Core;

namespace HammerOS.Apps;

public sealed class BrowserPage : UserControl, INativeWorkspace, IDisposable
{
    public WebSurface Web { get; } = new();
    private readonly TextBox _address = new() { Watermark = "Search or enter a web address", FontSize = 13, Name = "WebAddress" };
    private readonly TextBlock _status = Ui.Text("Starting web engine…", 11, Ui.Muted);
    private readonly Button _back, _forward;
    private string? _pending;
    public string Address => Web.Core?.Source is { } source && source != "about:blank" ? source : _pending ?? "";
    public event Action<string>? NewTab;
    public void FocusAddress() { _address.Focus(); _address.SelectAll(); }
    public BrowserPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("56,*,29") };
        _back = Ui.IconButton("back", "Back", () => { if (Web.Core?.CanGoBack == true) Web.Core.GoBack(); }); _forward = Ui.IconButton("arrow", "Forward", () => { if (Web.Core?.CanGoForward == true) Web.Core.GoForward(); });
        var bar = Ui.Columns("Auto,*,Auto", Ui.Row(0, _back, _forward, Ui.IconButton("refresh", "Reload", () => Web.Core?.Reload())), _address, Ui.Button("Go", () => Navigate(_address.Text ?? ""), "outline")); bar.Margin = new Thickness(10, 8); root.Children.Add(bar);
        var body = new Panel { Background = Ui.Dark, Children = { Ui.Pad(Ui.Stack(20, Ui.Text("A window to the web.", 32, Ui.Phosphor), Ui.Text("Enter an address above to browse with Microsoft Edge WebView2.", 13, Ui.Phosphor))), Web } }; Grid.SetRow(body, 1); root.Children.Add(body);
        var footer = Ui.Pad(_status, 8); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Navigate(_address.Text ?? ""); e.Handled = true; } };
        Web.Failed += error => _status.Text = error + " · Install Microsoft Edge WebView2 Runtime if missing.";
        Web.Ready += core =>
        {
            core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultContextMenusEnabled = true;
            core.NavigationStarting += (_, e) => { _status.Text = "Loading…"; if (e.Uri != "about:blank") _address.Text = e.Uri; };
            core.NavigationCompleted += (_, e) => { _status.Text = e.IsSuccess ? "WEBVIEW2  /  " + core.DocumentTitle : "Navigation failed: " + e.WebErrorStatus; _back.IsEnabled = core.CanGoBack; _forward.IsEnabled = core.CanGoForward; };
            core.SourceChanged += (_, _) => { if (core.Source != "about:blank") _address.Text = core.Source; };
            core.NewWindowRequested += (_, e) => { e.Handled = true; NewTab?.Invoke(e.Uri); };
            if (_pending is not null) core.Navigate(_pending); else core.NavigateToString("<html><body style='margin:0;background:#102c32;color:#b3e5da;font:15px Segoe UI;padding:60px'><small style='letter-spacing:3px'>HAMMER BROWSER</small><h1 style='font:42px Georgia;margin-top:22px'>Follow your curiosity.</h1><p>Enter an address or search above.</p></body></html>");
        };
    }
    public void Navigate(string value)
    {
        value = value.Trim(); if (value.Length == 0) return;
        if (!value.Contains("://")) value = value.Contains(' ') || !value.Contains('.') && !value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "https://www.bing.com/search?q=" + Uri.EscapeDataString(value) : "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http")) { _status.Text = "Enter an HTTP or HTTPS address."; return; }
        _pending = url.AbsoluteUri; _address.Text = _pending; Web.Core?.Navigate(_pending);
    }
    public void SetWorkspaceActive(bool active) => Web.SetActive(active);
    public void Dispose() => Web.Dispose();
}


public sealed class BrowserView : UserControl, INativeWorkspace, IDisposable
{
    private readonly List<BrowserPage> _tabs = [];
    private readonly StackPanel _strip = Ui.Row(3);
    private readonly ContentControl _body = new();
    private bool _active;
    public int ActiveIndex { get; private set; }
    public WebSurface Web => _tabs[ActiveIndex].Web;
    public IReadOnlyList<string> TabUris => _tabs.Select(x => x.Address).ToArray();
    public BrowserView()
    {
        var root = new Grid { RowDefinitions = new("42,*") };
        root.Children.Add(Ui.Columns("*,Auto", new ScrollViewer { Content = _strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, Ui.Button("+", () => AddTab())));
        Grid.SetRow(_body, 1); root.Children.Add(_body); Content = root; AddTab();
        AddHandler(KeyDownEvent, (_, e) => { if (ResolveShortcut(e.Key, e.KeyModifiers) is { } action) { action(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    public void AddTab(string? uri = null) { var page = new BrowserPage(); page.NewTab += value => AddTab(value); page.Web.Shortcut = ResolveShortcut; _tabs.Add(page); if (!string.IsNullOrEmpty(uri)) page.Navigate(uri); page.Web.Ready += core => { core.DocumentTitleChanged += (_, _) => RenderTabs(); }; Select(_tabs.Count - 1); }
    private Action? ResolveShortcut(Key key, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt)) return null;
        return key switch { Key.T => () => AddTab(), Key.W => () => CloseTab(ActiveIndex), Key.L => () => _tabs[ActiveIndex].FocusAddress(), Key.Tab => () => Select((ActiveIndex + (modifiers.HasFlag(KeyModifiers.Shift) ? _tabs.Count - 1 : 1)) % _tabs.Count), _ => null };
    }
    public void Navigate(string value) => _tabs[ActiveIndex].Navigate(value);
    public void Select(int index) { ActiveIndex = Math.Clamp(index, 0, _tabs.Count - 1); _body.Content = _tabs[ActiveIndex]; SetWorkspaceActive(_active); RenderTabs(); }
    public void CloseTab(int index) { if (index < 0 || index >= _tabs.Count) return; _body.Content = null; _tabs[index].Dispose(); _tabs.RemoveAt(index); if (_tabs.Count == 0) AddTab(); else Select(Math.Min(ActiveIndex, _tabs.Count - 1)); }
    public void RestoreTabs(IEnumerable<string> uris, int selected)
    { _body.Content = null; foreach (var page in _tabs) page.Dispose(); _tabs.Clear(); foreach (var uri in uris.Take(20)) AddTab(uri); if (_tabs.Count == 0) AddTab(); Select(selected); }
    private void RenderTabs()
    {
        _strip.Children.Clear(); for (var i = 0; i < _tabs.Count; i++) { var index = i; var text = _tabs[i].Web.Core?.DocumentTitle; if (string.IsNullOrEmpty(text)) text = string.IsNullOrEmpty(_tabs[i].Address) ? "New tab" : _tabs[i].Address; _strip.Children.Add(WorkspaceTab.Create(text, i == ActiveIndex, () => Select(index), () => CloseTab(index))); }
    }
    public void SetWorkspaceActive(bool active) { _active = active; for (var i = 0; i < _tabs.Count; i++) _tabs[i].SetWorkspaceActive(active && i == ActiveIndex); }
    public void Dispose() { foreach (var page in _tabs) page.Dispose(); _tabs.Clear(); }
}
