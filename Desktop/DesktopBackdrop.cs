using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace HammerOS.Desktop;

public sealed class DesktopBackdrop : Control
{
    public static readonly string[] Scenes = ["Classic", "Earth", "Mountains", "Waves"];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private bool _attached;
    public string Palette { get; set; } = "Petrol";
    public string Scene { get; private set; } = "Classic";
    public bool Scanlines { get; set; } = true;
    public bool AnimationRunning => _timer.IsEnabled;
    public double ElapsedSeconds => _clock.Elapsed.TotalSeconds;
    public double? PreviewTime { get; set; }
    private bool _animate;
    public DesktopBackdrop()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        _timer.Tick += (_, _) => { if (IsEffectivelyVisible) InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => { _attached = true; UpdateTimer(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; UpdateTimer(); };
    }
    public void Configure(string scene, bool animate)
    { Scene = Scenes.Contains(scene) ? scene : "Classic"; _animate = animate; UpdateTimer(); InvalidateVisual(); }
    private void UpdateTimer()
    { if (_attached && _animate && Scene != "Classic") { _clock.Start(); _timer.Start(); } else { _timer.Stop(); _clock.Stop(); } }
    public override void Render(DrawingContext c)
    {
        var bg = Palette == "Evergreen" ? "#1B3430" : Palette == "Midnight" ? "#152633" : "#16343B";
        c.FillRectangle(Brush.Parse(bg), Bounds);
        var dots = Brush.Parse("#194FC0B1");
        for (var x = 30; x < Bounds.Width; x += 52) for (var y = 28; y < Bounds.Height; y += 52) c.DrawEllipse(dots, null, new Point(x, y), .7, .7);
        var time = PreviewTime ?? _clock.Elapsed.TotalSeconds;
        using (c.PushClip(Bounds))
        {
            if (Scene == "Earth") Earth(c, time);
            else if (Scene is "Mountains" or "Waves") Terrain(c, time, Scene == "Mountains");
            else Classic(c);
        }
        if (Scanlines) { var scan = new Pen(Brush.Parse("#07000000"), 1); for (var y = 0; y < Bounds.Height; y += 4) c.DrawLine(scan, new Point(0, y), new Point(Bounds.Width, y)); }
    }
    private void Classic(DrawingContext c)
    {
        var center = new Point(Bounds.Width * .85, Bounds.Height * .54); var radius = Math.Min(Bounds.Width * .4, Bounds.Height * .46); var pen = new Pen(Brush.Parse("#144AB9AD"), 1);
        c.DrawEllipse(null, pen, center, radius, radius); c.DrawEllipse(null, pen, center, radius * .62, radius); c.DrawEllipse(null, pen, center, radius * .24, radius);
        c.DrawEllipse(null, pen, center, radius, radius * .29); c.DrawEllipse(null, pen, center, radius, radius * .73);
    }
    private void Earth(DrawingContext c, double time)
    {
        var radius = Math.Min(Bounds.Width * .38, Bounds.Height * .44); var center = new Point(Bounds.Width * .68, Bounds.Height * .50);
        var front = new Pen(Brush.Parse("#4779B6A8"), 1); var back = new Pen(Brush.Parse("#1379B6A8"), 1); var coast = new Pen(Brush.Parse("#967FBFAE"), 1.35);
        (Point Point, double Depth) Project(double longitude, double latitude)
        {
            var lon = longitude * Math.PI / 180 + time * .065; var lat = latitude * Math.PI / 180;
            var x = Math.Cos(lat) * Math.Sin(lon); var y = -Math.Sin(lat); var z = Math.Cos(lat) * Math.Cos(lon);
            const double tilt = .22;
            return (new Point(center.X + radius * (x * Math.Cos(tilt) - y * Math.Sin(tilt)), center.Y + radius * (x * Math.Sin(tilt) + y * Math.Cos(tilt))), z);
        }
        void Segment(double lon1, double lat1, double lon2, double lat2, bool land = false)
        {
            var a = Project(lon1, lat1); var b = Project(lon2, lat2); if (land && (a.Depth < 0 || b.Depth < 0)) return;
            c.DrawLine(land ? coast : (a.Depth + b.Depth > 0 ? front : back), a.Point, b.Point);
        }
        for (var lat = -75; lat <= 75; lat += 15) for (var lon = -180; lon < 180; lon += 5) Segment(lon, lat, lon + 5, lat);
        for (var lon = -180; lon < 180; lon += 15) for (var lat = -90; lat < 90; lat += 5) Segment(lon, lat, lon, lat + 5);
        foreach (var outline in Continents)
            for (var i = 1; i < outline.Length; i++)
            {
                var a = outline[i - 1]; var b = outline[i]; const int steps = 5;
                for (var s = 0; s < steps; s++) Segment(a.X + (b.X - a.X) * s / steps, a.Y + (b.Y - a.Y) * s / steps, a.X + (b.X - a.X) * (s + 1) / steps, a.Y + (b.Y - a.Y) * (s + 1) / steps, true);
            }
        c.DrawEllipse(null, front, center, radius, radius);
    }
    // Stylized continent contours, intentionally low-detail to match the wireframe desktop.
    private static readonly Point[][] Continents = [
        [new(-168,65),new(-140,70),new(-110,72),new(-82,55),new(-60,48),new(-82,25),new(-98,18),new(-110,30),new(-125,49),new(-168,65)],
        [new(-81,12),new(-62,8),new(-48,-3),new(-35,-10),new(-45,-24),new(-54,-35),new(-69,-55),new(-76,-29),new(-81,12)],
        [new(-17,36),new(10,37),new(34,30),new(43,12),new(51,11),new(40,-12),new(32,-30),new(18,-35),new(10,-5),new(-16,12),new(-17,36)],
        [new(-10,36),new(-9,44),new(5,49),new(9,59),new(28,71),new(60,68),new(100,76),new(145,60),new(170,65),new(145,42),new(123,30),new(107,5),new(100,20),new(78,8),new(68,25),new(45,12),new(34,30),new(26,41),new(12,45),new(-10,36)],
        [new(113,-22),new(130,-12),new(142,-11),new(153,-25),new(146,-39),new(128,-34),new(115,-35),new(113,-22)],
        [new(-54,60),new(-20,70),new(-27,83),new(-52,80),new(-54,60)],
        [new(47,-13),new(51,-16),new(47,-25),new(44,-23),new(47,-13)]
    ];
    private void Terrain(DrawingContext c, double time, bool mountains)
    {
        const int columns = 40, rows = 27; var points = new Point[rows + 1, columns + 1];
        var horizon = Bounds.Height * .31; var center = Bounds.Width * .56;
        for (var row = 0; row <= rows; row++) for (var col = 0; col <= columns; col++)
        {
            var depth = 1.7 + row * .28; var x = (col - columns / 2d) * .65; var z = depth + time * .16;
            var height = mountains ? 1.9 * Math.Pow((Math.Sin(x * .62 + Math.Sin(z * .8)) + Math.Cos(z * .72) + 2) / 4, 3) + .22 * Math.Sin(x * 2 + z * 1.7) : .38 * Math.Sin(x * .75 + z * .9 - time * .45) + .22 * Math.Cos(x * .5 - z * .8 + time * .3);
            points[row, col] = new Point(center + x * Bounds.Width * .19 / depth, horizon + Bounds.Height * (1.35 - height * .62) / depth);
        }
        for (var row = rows; row >= 0; row--)
        {
            var alpha = (byte)(28 + 50 * (1 - row / (double)rows)); var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 126, 190, 169)), 1);
            for (var col = 0; col <= columns; col++)
            {
                if (col < columns) c.DrawLine(pen, points[row, col], points[row, col + 1]);
                if (row < rows) { c.DrawLine(pen, points[row, col], points[row + 1, col]); if (mountains && col < columns) c.DrawLine(pen, points[row, col], points[row + 1, col + 1]); }
            }
        }
    }
}
