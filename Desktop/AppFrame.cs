using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Animation;
using Avalonia.VisualTree;
using HammerOS.Core;
using HammerOS.Apps;

namespace HammerOS.Desktop;

public sealed class AppFrame : Border
{
    public string AppId { get; }
    public string DisplayName { get; }
    public bool IsMinimized { get; private set; }
    public bool IsMaximized { get; private set; }
    public Control AppContent { get; }
    private readonly ShellWindow _shell;
    private readonly Border _title;
    private Rect _restore;
    private Point? _drag;
    private Point _origin;
    private readonly List<Border> _resizeHandles = [];
    private Border? _cornerGrip;
    private bool _busy, _leaving;
    private Rect? _pendingTarget;
    private static int _maximizeCounter;
    public int MaximizedAt { get; private set; }
    // On screen and taking up room. Windows fading out to minimize or close already count as gone.
    public bool IsSolid => IsVisible && !IsMinimized && !_leaving;
    // Where the window is, or where its running animation will leave it.
    public Rect Footprint => _pendingTarget ?? new Rect(Coordinate(Canvas.GetLeft(this)), Coordinate(Canvas.GetTop(this)), Width, Height);
    private static double Coordinate(double value) => double.IsNaN(value) ? 0 : value;

    public AppFrame(ShellWindow shell, AppDefinition app, Control content, double width, double height, int instance = 1)
    {
        _shell = shell; AppId = app.Id; AppContent = content;
        DisplayName = app.Name + (instance > 1 ? $" · {instance:00}" : "");
        Width = width; Height = height; Background = Ui.Cream; BorderBrush = Brush.Parse("#9CAEA0"); BorderThickness = new Thickness(1);
        BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 14, Blur = 38, Color = Color.Parse("#45091619") });
        var root = new Grid { RowDefinitions = new RowDefinitions("43,*") };
        var titleText = Ui.Label(DisplayName.ToUpperInvariant(), Ui.Ink); titleText.LetterSpacing = 1.2; titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleLeft = Ui.Row(12, new Glyph(app.Symbol) { Width = 16, Height = 16 }, titleText); titleLeft.Margin = new Thickness(16, 0); titleLeft.Background = Brushes.Transparent;
        var buttons = Ui.Row(0, Ui.IconButton("minimize", "Minimize", Minimize), Ui.IconButton("maximize", "Maximize / restore", ToggleMaximize), Ui.IconButton("close", "Close application", Close));
        _title = new Border { Background = Tint.TitleActive, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Columns("*,Auto", titleLeft, buttons) };
        _title.Transitions = new Transitions { new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(180) } };
        root.Children.Add(_title);
        var area = new Grid { ClipToBounds = true, Children = { content } }; Grid.SetRow(area, 1); root.Children.Add(area);
        var grip = new Border { Width = 19, Height = 19, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.BottomRightCorner), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "◢", FontSize = 10, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom } };
        area.Children.Add(grip); Child = root;
        _cornerGrip = grip; ConnectResize(grip, 1, 1);
        foreach (var (name, horizontal, vertical, cursor) in new[] {
            ("Left", -1, 0, StandardCursorType.LeftSide), ("Right", 1, 0, StandardCursorType.RightSide),
            ("Top", 0, -1, StandardCursorType.TopSide), ("Bottom", 0, 1, StandardCursorType.BottomSide),
            ("TopLeft", -1, -1, StandardCursorType.TopLeftCorner), ("TopRight", 1, -1, StandardCursorType.TopRightCorner),
            ("BottomLeft", -1, 1, StandardCursorType.BottomLeftCorner), ("BottomRight", 1, 1, StandardCursorType.BottomRightCorner) })
        {
            var corner = horizontal != 0 && vertical != 0;
            var handle = new Border { Name = "Resize" + name, Background = Brushes.Transparent, Cursor = new Cursor(cursor),
                HorizontalAlignment = horizontal < 0 ? HorizontalAlignment.Left : horizontal > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = vertical < 0 ? VerticalAlignment.Top : vertical > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Margin = new Thickness(vertical == 0 ? 0 : corner ? 0 : 12, horizontal == 0 ? 0 : corner ? 0 : 12) };
            if (horizontal != 0) handle.Width = corner ? 12 : 5;
            if (vertical != 0) handle.Height = corner ? 12 : 5;
            Grid.SetRowSpan(handle, 2); root.Children.Add(handle); _resizeHandles.Add(handle); ConnectResize(handle, horizontal, vertical);
        }
        UpdateResizeHandles();
        AddHandler(PointerPressedEvent, (_, _) => shell.FocusFrame(this), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        bool IsTitleButton(object? source) => source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<Button>().Any();
        _title.PointerPressed += (_, e) => { if (_busy || IsTitleButton(e.Source) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsMaximized) return; Motion.Cancel(this); _pendingTarget = null; Opacity = 1; RenderTransform = null; _drag = e.GetPosition(shell.Workspace); _origin = new Point(Canvas.GetLeft(this), Canvas.GetTop(this)); e.Pointer.Capture(_title); };
        _title.PointerMoved += (_, e) => { if (_drag is not { } start) return; var delta = e.GetPosition(shell.Workspace) - start; MoveTo(_origin.X + delta.X, _origin.Y + delta.Y); };
        _title.PointerReleased += (_, e) => { if (_drag is null) return; _drag = null; e.Pointer.Capture(null); _shell.Reflow(); };
        _title.DoubleTapped += (_, e) => { if (!IsTitleButton(e.Source)) ToggleMaximize(); };
    }
    public void UpdateResizeHandles()
    { foreach (var handle in _resizeHandles) handle.IsVisible = _shell.State.Preferences.EdgeResize && !IsMaximized; if (_cornerGrip is not null) _cornerGrip.IsVisible = !IsMaximized; }
    private void ConnectResize(Border handle, int horizontal, int vertical)
    {
        Point? start = null; Rect original = default;
        handle.PointerPressed += (_, e) =>
        {
            if (_busy || IsMaximized || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Motion.Cancel(this); _pendingTarget = null; Opacity = 1; RenderTransform = null; start = e.GetPosition(_shell.Workspace);
            original = new Rect(Canvas.GetLeft(this), Canvas.GetTop(this), Width, Height); e.Pointer.Capture(handle); e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (start is not { } point || IsMaximized) return;
            var delta = e.GetPosition(_shell.Workspace) - point;
            var left = original.Left; var right = original.Right; var top = original.Top; var bottom = original.Bottom;
            var workspace = _shell.Workspace.Bounds;
            var minWidth = Math.Min(440, horizontal < 0 ? right : workspace.Width - left);
            var minHeight = Math.Min(300, vertical < 0 ? bottom : workspace.Height - top);
            if (horizontal < 0) left = Math.Clamp(original.Left + delta.X, 0, right - minWidth);
            if (horizontal > 0) right = Math.Clamp(original.Right + delta.X, left + minWidth, workspace.Width);
            if (vertical < 0) top = Math.Clamp(original.Top + delta.Y, 0, bottom - minHeight);
            if (vertical > 0) bottom = Math.Clamp(original.Bottom + delta.Y, top + minHeight, workspace.Height);
            if (_shell.Collisions) { var fit = WindowCollisions.Resize(original, new Rect(left, top, right - left, bottom - top), _shell.Obstacles(this)); left = fit.Left; right = fit.Right; top = fit.Top; bottom = fit.Bottom; }
            Width = right - left; Height = bottom - top; Canvas.SetLeft(this, left); Canvas.SetTop(this, top); e.Handled = true;
        };
        handle.PointerReleased += (_, e) => { if (start is null) return; start = null; e.Pointer.Capture(null); e.Handled = true; _shell.Reflow(); };
        handle.PointerCaptureLost += (_, _) => start = null;
    }
    public void SetPosition(double x, double y)
    { Canvas.SetLeft(this, Math.Clamp(x, 0, Math.Max(0, _shell.Workspace.Bounds.Width - Width))); Canvas.SetTop(this, Math.Clamp(y, 0, Math.Max(0, _shell.Workspace.Bounds.Height - Height))); }
    private void MoveTo(double x, double y)
    {
        if (!_shell.Collisions) { SetPosition(x, y); return; }
        var bounds = _shell.Workspace.Bounds;
        var to = new Point(Math.Clamp(x, 0, Math.Max(0, bounds.Width - Width)), Math.Clamp(y, 0, Math.Max(0, bounds.Height - Height)));
        var at = WindowCollisions.Slide(Footprint, to, _shell.Obstacles(this)); Canvas.SetLeft(this, at.X); Canvas.SetTop(this, at.Y);
    }
    public void Constrain()
    {
        if (IsMaximized && !_shell.Collisions) { Width = _shell.Workspace.Bounds.Width; Height = _shell.Workspace.Bounds.Height; Canvas.SetLeft(this, 0); Canvas.SetTop(this, 0); }
        else { Width = Math.Min(Width, _shell.Workspace.Bounds.Width); Height = Math.Min(Height, _shell.Workspace.Bounds.Height); SetPosition(Canvas.GetLeft(this), Canvas.GetTop(this)); }
    }
    public void SetActive(bool active) { _title.Background = active ? Tint.TitleActive : Tint.TitleInactive; BorderBrush = active ? Tint.BorderActive : Tint.BorderInactive; }
    public void Minimize()
    {
        if (_busy) return; _busy = true; _leaving = true; _pendingTarget = null;
        var move = new TranslateTransform(); RenderTransform = move;
        var opacity = Opacity;
        Motion.For(this, 210, t => { Opacity = opacity * (1 - t); move.Y = t * 34; }, () => { IsMinimized = true; IsVisible = false; _busy = false; _leaving = false; _shell.UpdateTaskbar(); });
        _shell.Reflow();
    }
    public void Restore()
    { if (_busy) return; if (!IsMinimized) { _shell.FocusFrame(this); return; } IsMinimized = false; IsVisible = true; _shell.Settle(this); Motion.Enter(this, 24); _shell.FocusFrame(this); _shell.UpdateTaskbar(); }
    public void Close()
    {
        if (_busy) return;
        if (AppContent is EditorView { IsDirty: true } editor) { Restore(); _shell.FocusFrame(this); editor.ConfirmClose(CloseCore); return; }
        if (AppContent is TiledView { TileCount: > 0 } tiled) { Restore(); _shell.FocusFrame(this); tiled.ConfirmClose(CloseCore); return; }
        CloseCore();
    }
    private void CloseCore()
    {
        if (_busy) return; _busy = true; _leaving = true; _pendingTarget = null;
        var opacity = Opacity;
        Motion.For(this, 160, t => Opacity = opacity * (1 - t), () => _shell.RemoveFrame(this));
        _shell.Reflow();
    }
    public void ToggleMaximize()
    {
        if (_busy) return;
        var from = new Rect(Canvas.GetLeft(this), Canvas.GetTop(this), Width, Height);
        if (!IsMaximized) _restore = from;
        var target = !_shell.Collisions ? IsMaximized ? _restore : new Rect(0, 0, _shell.Workspace.Bounds.Width, _shell.Workspace.Bounds.Height)
            : IsMaximized ? _shell.Clearing(this, _restore) : _shell.MaximumArea(this);
        IsMaximized = !IsMaximized; UpdateResizeHandles(); _busy = true;
        if (IsMaximized) MaximizedAt = ++_maximizeCounter;
        Motion.Cancel(this); Opacity = 1; RenderTransform = null; _pendingTarget = target;
        Motion.For(this, 240, t => { Canvas.SetLeft(this, from.X + (target.X - from.X) * t); Canvas.SetTop(this, from.Y + (target.Y - from.Y) * t); Width = from.Width + (target.Width - from.Width) * t; Height = from.Height + (target.Height - from.Height) * t; }, () => { _pendingTarget = null; _busy = false; Constrain(); _shell.Reflow(); });
    }
    public void ArrangeTo(Rect target)
    {
        Motion.Cancel(this); _busy = false; _leaving = false; IsMaximized = false; UpdateResizeHandles(); IsMinimized = false; IsVisible = true; Opacity = 1; RenderTransform = null;
        var from = new Rect(Canvas.GetLeft(this), Canvas.GetTop(this), Width, Height); _pendingTarget = target;
        Motion.For(this, 240, t => { Width = from.Width + (target.Width - from.Width) * t; Height = from.Height + (target.Height - from.Height) * t; SetPosition(from.X + (target.X - from.X) * t, from.Y + (target.Y - from.Y) * t); }, () => { _pendingTarget = null; Constrain(); _shell.UpdateTaskbar(); });
    }
    // Moves and resizes without changing window state; used when collisions push windows around.
    // A window mid-animation (maximizing, minimizing, closing) is left alone: its completion reflows.
    public void FitTo(Rect target, bool animate = true)
    {
        if (_busy) return;
        var from = Footprint;
        if (Math.Abs(from.X - target.X) < .5 && Math.Abs(from.Y - target.Y) < .5 && Math.Abs(from.Width - target.Width) < .5 && Math.Abs(from.Height - target.Height) < .5) return;
        void Apply(Rect r) { Canvas.SetLeft(this, r.X); Canvas.SetTop(this, r.Y); Width = r.Width; Height = r.Height; }
        if (!animate) { _pendingTarget = null; Apply(target); return; }
        from = new Rect(Coordinate(Canvas.GetLeft(this)), Coordinate(Canvas.GetTop(this)), Width, Height); _pendingTarget = target; Opacity = 1; RenderTransform = null;
        Motion.For(this, 240, t => Apply(new Rect(from.X + (target.X - from.X) * t, from.Y + (target.Y - from.Y) * t, from.Width + (target.Width - from.Width) * t, from.Height + (target.Height - from.Height) * t)), () => _pendingTarget = null);
    }
}
