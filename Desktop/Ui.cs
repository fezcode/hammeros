using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;

namespace HammerOS.Desktop;

public static class Ui
{
    public static readonly IBrush Ink = Brush.Parse("#173B40"), Cream = Brush.Parse("#ECEDE0"), Muted = Brush.Parse("#688178"), Line = Brush.Parse("#BCC8BB"), Phosphor = Brush.Parse("#B3E5DA"), Dark = Brush.Parse("#102C32");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas"), Serif = new("Georgia, Times New Roman");
    public static TextBlock Text(string text, double size = 13, IBrush? color = null, bool mono = false) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, FontFamily = mono ? Mono : FontFamily.Default, VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Label(string text, IBrush? color = null) => new() { Text = text, FontSize = 10.5, LetterSpacing = 1.2, Foreground = color ?? Muted, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center };
    public static StackPanel Stack(double spacing = 12, params Control[] children) { var p = new StackPanel { Spacing = spacing }; foreach (var child in children) p.Children.Add(child); return p; }
    public static StackPanel Row(double spacing = 10, params Control[] children) { var p = Stack(spacing, children); p.Orientation = Orientation.Horizontal; return p; }
    public static Border Rule(IBrush? brush = null) => new() { Height = 1, Background = brush ?? Line, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static Border Pad(Control child, double padding = 24) => new() { Padding = new Thickness(padding), Child = child };
    public static Button Button(string text, Action action, string? cls = null)
    {
        var b = new Button { Content = text }; if (cls is not null) b.Classes.Add(cls); b.Click += (_, _) => action();
        b.Transitions = new Transitions { new BrushTransition { Property = Avalonia.Controls.Button.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(140) }, new DoubleTransition { Property = Control.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };
        return b;
    }
    public static Button IconButton(string icon, string tip, Action action)
    { var b = Button("", action); b.Content = new Glyph(icon) { Width = 15, Height = 15 }; ToolTip.SetTip(b, tip); return b; }
    public static Control Heading(string eyebrow, string title, string subtitle) => Stack(10, Label(eyebrow), new TextBlock { Text = title, FontFamily = Serif, FontSize = 32, Foreground = Ink }, new TextBlock { Text = subtitle, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
    public static Grid Columns(string definitions, params Control[] controls)
    { var g = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions) }; for (var i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); g.Children.Add(controls[i]); } return g; }
    public static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}

public static class Motion
{
    public static bool Enabled { get; set; } = true;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, AnimationSlot> Slots = new();
    private sealed class AnimationSlot { public Action? Cancel; }
    public static void Cancel(Control control) { if (Slots.TryGetValue(control, out var slot)) { slot.Cancel?.Invoke(); slot.Cancel = null; } }
    public static void For(Control control, double milliseconds, Action<double> frame, Action? done = null)
    {
        Cancel(control);
        var slot = Slots.GetOrCreateValue(control);
        slot.Cancel = Animate(milliseconds, frame, done);
    }
    public static Action Animate(double milliseconds, Action<double> frame, Action? done = null)
    {
        if (!Enabled) { frame(1); done?.Invoke(); return () => { }; }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) => { var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / milliseconds); frame(1 - Math.Pow(1 - t, 3)); if (t >= 1) { timer.Stop(); done?.Invoke(); } };
        frame(0); timer.Start(); return timer.Stop;
    }
    public static void Enter(Control control, double distance = 16)
    {
        var transform = new TranslateTransform(0, distance); control.RenderTransform = transform;
        For(control, 300, t => { control.Opacity = t; transform.Y = distance * (1 - t); });
    }
}

public sealed class Glyph(string kind) : Control
{
    public IBrush Color { get; set; } = Ui.Ink;
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["folder"] = "M2,6 L9,6 L11,9 L22,9 L22,20 L2,20 Z M2,9 L22,9",
        ["terminal"] = "M2,4 L22,4 L22,20 L2,20 Z M6,9 L10,12 L6,15 M12,16 L18,16",
        ["refinement"] = "M3,3 L9,3 L9,9 L3,9 Z M15,3 L21,3 L21,9 L15,9 Z M3,15 L9,15 L9,21 L3,21 Z M15,15 L21,15 L21,21 L15,21 Z",
        ["globe"] = "M12,2 A10,10 0 1 1 11.99,2 M2,12 L22,12 M12,2 C5,8 5,16 12,22 C19,16 19,8 12,2",
        ["notes"] = "M5,2 L16,2 L21,7 L21,22 L5,22 Z M16,2 L16,7 L21,7 M9,11 L17,11 M9,15 L17,15 M9,19 L14,19",
        ["network"] = "M8,2 L16,2 L16,8 L8,8 Z M2,16 L10,16 L10,22 L2,22 Z M14,16 L22,16 L22,22 L14,22 Z M12,8 L12,12 M6,16 L6,12 L18,12 L18,16",
        ["control"] = "M4,2 L4,7 M4,13 L4,22 M12,2 L12,13 M12,19 L12,22 M20,2 L20,6 M20,12 L20,22 M1,7 L7,7 L7,13 L1,13 Z M9,13 L15,13 L15,19 L9,19 Z M17,6 L23,6 L23,12 L17,12 Z",
        ["settings"] = "M12,2 L15,5 L19,5 L19,9 L22,12 L19,15 L19,19 L15,19 L12,22 L9,19 L5,19 L5,15 L2,12 L5,9 L5,5 L9,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
        ["wellness"] = "M12,21 C-4,13 2,2 9,7 L12,10 L15,7 C22,2 28,13 12,21 M12,10 L12,20",
        ["book"] = "M12,5 C8,2 4,2 2,3 L2,20 C6,19 9,20 12,22 C15,20 18,19 22,20 L22,3 C18,2 15,2 12,5 L12,22",
        ["search"] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22",
        ["lock"] = "M5,10 L19,10 L19,22 L5,22 Z M8,10 L8,6 A4,4 0 0 1 16,6 L16,10 M12,14 L12,18",
        ["power"] = "M12,2 L12,12 M6,5 A9,9 0 1 0 18,5",
        ["arrow"] = "M4,12 L20,12 M14,6 L20,12 L14,18",
        ["back"] = "M20,12 L4,12 M10,6 L4,12 L10,18",
        ["close"] = "M5,5 L19,19 M19,5 L5,19",
        ["maximize"] = "M4,4 L20,4 L20,20 L4,20 Z",
        ["minimize"] = "M5,16 L19,16",
        ["check"] = "M3,12 L9,18 L21,5",
        ["refresh"] = "M20,9 A9,9 0 1 0 20,17 M20,3 L20,9 L14,9"
    };
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24)))
            context.DrawGeometry(null, new Pen(Color, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geometry.Parse(Paths.GetValueOrDefault(kind, Paths["refinement"])));
    }
}

public sealed class CorporateSeal : Control
{
    public IBrush Color { get; set; } = Ui.Phosphor;
    public override void Render(DrawingContext c)
    {
        using (c.PushTransform(Matrix.CreateScale(Bounds.Width / 100, Bounds.Height / 100)))
        {
            var p = new Pen(Color, 1.2);
            c.DrawEllipse(null, p, new Point(50, 50), 46, 46);
            c.DrawEllipse(null, p, new Point(50, 50), 27, 46);
            c.DrawEllipse(null, p, new Point(50, 50), 46, 18);
            c.DrawLine(p, new Point(4, 50), new Point(96, 50));
            c.DrawLine(p, new Point(50, 4), new Point(50, 96));
            c.DrawRectangle(Ui.Dark, null, new Rect(10, 34, 80, 32));
            var text = new FormattedText("H", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Serif, FontStyle.Normal, FontWeight.Bold), 42, Color);
            c.DrawText(text, new Point(32, 24));
        }
    }
}

public sealed class DesktopBackdrop : Control
{
    public string Palette { get; set; } = "Petrol";
    public bool Scanlines { get; set; } = true;
    public override void Render(DrawingContext c)
    {
        var bg = Palette == "Evergreen" ? "#1B3430" : Palette == "Midnight" ? "#152633" : "#16343B";
        c.FillRectangle(Brush.Parse(bg), Bounds);
        var pen = new Pen(Brush.Parse("#194FC0B1"), 1);
        for (var x = 30; x < Bounds.Width; x += 52)
            for (var y = 28; y < Bounds.Height; y += 52) c.DrawEllipse(pen.Brush, null, new Point(x, y), .7, .7);
        var center = new Point(Bounds.Width * .85, Bounds.Height * .54);
        var globePen = new Pen(Brush.Parse("#144AB9AD"), 1);
        c.DrawEllipse(null, globePen, center, 330, 330); c.DrawEllipse(null, globePen, center, 205, 330); c.DrawEllipse(null, globePen, center, 78, 330);
        c.DrawEllipse(null, globePen, center, 330, 95); c.DrawEllipse(null, globePen, center, 330, 240);
        if (Scanlines) for (var y = 0; y < Bounds.Height; y += 4) c.DrawLine(new Pen(Brush.Parse("#07000000"), 1), new Point(0, y), new Point(Bounds.Width, y));
    }
}
