using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HammerOS.Desktop;
using Microsoft.Web.WebView2.Core;

namespace HammerOS.Apps;

public sealed class BrowserView : UserControl, INativeWorkspace, IDisposable
{
    public WebSurface Web { get; } = new();
    private readonly TextBox _address = new() { Watermark = "Search or enter a web address", FontSize = 13, Name = "WebAddress" };
    private readonly TextBlock _status = Ui.Text("Starting web engine…", 11, Ui.Muted);
    private readonly Button _back, _forward;
    private string? _pending;
    public BrowserView()
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
            core.NewWindowRequested += (_, e) => { e.Handled = true; Navigate(e.Uri); };
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
