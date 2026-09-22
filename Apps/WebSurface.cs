using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;

namespace HammerOS.Apps;

public interface INativeWorkspace { void SetWorkspaceActive(bool active); }

public sealed class WebSurface : NativeControlHost, IDisposable
{
    private CoreWebView2Controller? _controller;
    private IntPtr _container, _parking;
    private bool _starting, _disposed, _active = true;
    private readonly DispatcherTimer _bounds = new() { Interval = TimeSpan.FromMilliseconds(16) };
    public ContextMenu? ActiveContextMenu { get; private set; }
    public CoreWebView2? Core => _controller?.CoreWebView2;
    public event Action<CoreWebView2>? Ready;
    public event Action<string>? Failed;
    public WebSurface() { _bounds.Tick += (_, _) => SyncBounds(); }
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent); _container = handle.Handle; _parking = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? parent.Handle;
        if (_controller is not null) { _controller.ParentWindow = _container; _controller.IsVisible = _active; SyncBounds(); }
        else if (!_starting && !_disposed) { _starting = true; _ = InitializeAsync(); }
        _bounds.Start(); return handle;
    }
    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HammerOS", "WebView2"));
            if (_disposed) return;
            var controller = await environment.CreateCoreWebView2ControllerAsync(_container == IntPtr.Zero ? _parking : _container);
            if (_disposed) { controller.Close(); return; }
            _controller = controller; controller.ParentWindow = _container == IntPtr.Zero ? _parking : _container; controller.IsVisible = _active && _container != IntPtr.Zero; controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(16, 44, 50); SyncBounds();
            controller.CoreWebView2.ContextMenuRequested += ShowContextMenu; Ready?.Invoke(controller.CoreWebView2);
        }
        catch (Exception ex) { if (!_disposed) Failed?.Invoke("Web engine unavailable: " + ex.Message); }
        finally { _starting = false; }
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _bounds.Stop(); _container = IntPtr.Zero;
        if (_controller is { } controller)
        {
            try { controller.IsVisible = false; if (HammerOS.Core.NativeWindows.Exists(_parking)) controller.ParentWindow = _parking; else { controller.Close(); _controller = null; } }
            catch (System.Runtime.InteropServices.COMException) { try { controller.Close(); } catch (System.Runtime.InteropServices.COMException) { } _controller = null; }
        }
        base.DestroyNativeControlCore(control);
    }
    private void SyncBounds()
    {
        if (_controller is null || _container == IntPtr.Zero || _disposed) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var rect = new System.Drawing.Rectangle(0, 0, Math.Max(1, (int)(Bounds.Width * scale)), Math.Max(1, (int)(Bounds.Height * scale)));
        if (_controller.Bounds != rect) _controller.Bounds = rect;
    }
    private void ShowContextMenu(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        ActiveContextMenu?.Close(); e.Handled = true; e.SelectedCommandId = -1;
        var deferral = e.GetDeferral(); var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var menu = new ContextMenu { MinWidth = 240, Placement = PlacementMode.BottomEdgeAlignedLeft, PlacementRect = new Rect(e.Location.X / scale, e.Location.Y / scale, 1, 1) };
        Control ConvertItem(CoreWebView2ContextMenuItem source)
        {
            if (source.Kind == CoreWebView2ContextMenuItemKind.Separator) return new Separator();
            var item = new MenuItem { Header = source.Label.Replace("&", ""), IsEnabled = source.IsEnabled };
            if (source.Kind == CoreWebView2ContextMenuItemKind.Submenu) foreach (var child in source.Children) item.Items.Add(ConvertItem(child));
            else { if (source.Kind is CoreWebView2ContextMenuItemKind.CheckBox or CoreWebView2ContextMenuItemKind.Radio && source.IsChecked) item.Icon = new HammerOS.Desktop.Glyph("check") { Width = 15, Height = 15 }; item.Click += (_, _) => { e.SelectedCommandId = source.CommandId; menu.Close(); }; }
            return item;
        }
        foreach (var item in e.MenuItems) menu.Items.Add(ConvertItem(item));
        ActiveContextMenu = menu;
        var completed = false;
        void Complete() { if (completed) return; completed = true; if (ActiveContextMenu == menu) ActiveContextMenu = null; deferral.Dispose(); }
        menu.Closed += (_, _) => Complete();
        try { menu.Open(this); } catch { Complete(); throw; }
    }
    public void SetActive(bool active) { _active = active; if (!active) ActiveContextMenu?.Close(); if (_controller is not null) _controller.IsVisible = active && _container != IntPtr.Zero; IsVisible = active; }
    public void Dispose() { _disposed = true; ActiveContextMenu?.Close(); _bounds.Stop(); _controller?.Close(); _controller = null; }
}
