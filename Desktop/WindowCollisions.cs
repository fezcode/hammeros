using Avalonia;

namespace HammerOS.Desktop;

// Geometry for the "Colliding windows" preference. Windows are solid rectangles: they slide
// against each other instead of overlapping, and maximize claims the largest free area.
// Obstacles that already overlap a window are ignored when it moves, so a window that was
// trapped (opened into a full desktop, or overlapping when the preference was enabled) can
// always be dragged free.
public static class WindowCollisions
{
    private const double Epsilon = 0.5;

    public static bool Overlaps(Rect a, Rect b) => SpansX(a, b) && SpansY(a, b);
    private static bool SpansX(Rect a, Rect b) => a.Left < b.Right - Epsilon && b.Left < a.Right - Epsilon;
    private static bool SpansY(Rect a, Rect b) => a.Top < b.Bottom - Epsilon && b.Top < a.Bottom - Epsilon;
    public static bool IsFree(Rect rect, IEnumerable<Rect> obstacles) => !obstacles.Any(o => Overlaps(rect, o));

    // Moves horizontally, then vertically, stopping flush against the first obstacle on each axis.
    public static Point Slide(Rect from, Point to, IReadOnlyList<Rect> obstacles)
    {
        var solid = obstacles.Where(o => !Overlaps(from, o)).ToArray();
        var x = from.X + Travel(to.X - from.X, solid.Where(o => SpansY(from, o)).Select(o => to.X > from.X ? o.Left - from.Right : from.Left - o.Right));
        var moved = from.WithX(x);
        var y = from.Y + Travel(to.Y - from.Y, solid.Where(o => SpansX(moved, o)).Select(o => to.Y > from.Y ? o.Top - from.Bottom : from.Top - o.Bottom));
        return new Point(x, y);
    }
    private static double Travel(double delta, IEnumerable<double> gaps)
    {
        var room = gaps.Where(g => g >= -Epsilon).Select(g => Math.Max(0, g)).DefaultIfEmpty(double.PositiveInfinity).Min();
        return Math.Sign(delta) * Math.Min(Math.Abs(delta), room);
    }

    // Limits a resize so no edge passes an obstacle it was clear of when the resize began.
    public static Rect Resize(Rect original, Rect desired, IReadOnlyList<Rect> obstacles)
    {
        var solid = obstacles.Where(o => !Overlaps(original, o)).ToArray();
        double left = desired.Left, right = desired.Right, top = desired.Top, bottom = desired.Bottom;
        foreach (var o in solid.Where(o => SpansY(original, o)))
        {
            if (o.Left >= original.Right - Epsilon) right = Math.Min(right, Math.Max(o.Left, original.Right));
            if (o.Right <= original.Left + Epsilon) left = Math.Max(left, Math.Min(o.Right, original.Left));
        }
        var span = new Rect(left, original.Top, Math.Max(0, right - left), original.Height);
        foreach (var o in solid.Where(o => SpansX(span, o)))
        {
            if (o.Top >= original.Bottom - Epsilon) bottom = Math.Min(bottom, Math.Max(o.Top, original.Bottom));
            if (o.Bottom <= original.Top + Epsilon) top = Math.Max(top, Math.Min(o.Bottom, original.Top));
        }
        return new Rect(left, top, right - left, bottom - top);
    }

    // The largest empty rectangle in bounds at least `minimum` in size. Rectangles touching
    // `prefer` win over larger ones elsewhere, so a window grows where it is rather than jumping.
    public static Rect? LargestFree(Rect bounds, IReadOnlyList<Rect> obstacles, Rect prefer, Size minimum)
    {
        var blocks = obstacles.Select(o => o.Intersect(bounds)).Where(o => o.Width > Epsilon && o.Height > Epsilon).ToArray();
        var xs = blocks.SelectMany(o => new[] { o.Left, o.Right }).Append(bounds.Left).Append(bounds.Right).Distinct().Order().ToArray();
        Rect? best = null; var bestTouches = false;
        void Consider(Rect candidate)
        {
            if (candidate.Width < minimum.Width || candidate.Height < minimum.Height) return;
            var touches = Overlaps(candidate, prefer);
            if (best is not { } current || (touches && !bestTouches) || (touches == bestTouches && candidate.Width * candidate.Height > current.Width * current.Height)) { best = candidate; bestTouches = touches; }
        }
        for (var i = 0; i < xs.Length; i++)
            for (var j = i + 1; j < xs.Length; j++)
            {
                double x1 = xs[i], x2 = xs[j], y = bounds.Top;
                if (x2 - x1 < minimum.Width) continue;
                foreach (var o in blocks.Where(o => o.Left < x2 - Epsilon && x1 < o.Right - Epsilon).OrderBy(o => o.Top))
                { if (o.Top > y) Consider(new Rect(x1, y, x2 - x1, o.Top - y)); y = Math.Max(y, o.Bottom); }
                if (bounds.Bottom > y) Consider(new Rect(x1, y, x2 - x1, bounds.Bottom - y));
            }
        return best;
    }

    // Finds the free spot nearest `desired` for a window of that size. When nothing that size fits,
    // the window shrinks into the largest free area; null means there is no usable room at all.
    public static Rect? Place(Rect bounds, Rect desired, IReadOnlyList<Rect> obstacles, Size minimum)
    {
        double w = Math.Min(desired.Width, bounds.Width), h = Math.Min(desired.Height, bounds.Height);
        var start = new Point(Math.Clamp(desired.X, bounds.Left, bounds.Right - w), Math.Clamp(desired.Y, bounds.Top, bounds.Bottom - h));
        var first = new Rect(start, new Size(w, h));
        if (IsFree(first, obstacles)) return first;
        var xs = obstacles.SelectMany(o => new[] { o.Right, o.Left - w }).Append(start.X).Append(bounds.Left).Append(bounds.Right - w).Where(x => x >= bounds.Left - Epsilon && x + w <= bounds.Right + Epsilon);
        var ys = obstacles.SelectMany(o => new[] { o.Bottom, o.Top - h }).Append(start.Y).Append(bounds.Top).Append(bounds.Bottom - h).Where(y => y >= bounds.Top - Epsilon && y + h <= bounds.Bottom + Epsilon).ToArray();
        var spot = xs.SelectMany(x => ys.Select(y => new Rect(x, y, w, h))).Where(r => IsFree(r, obstacles))
            .OrderBy(r => (r.X - start.X) * (r.X - start.X) + (r.Y - start.Y) * (r.Y - start.Y)).Cast<Rect?>().FirstOrDefault();
        if (spot is not null) return spot;
        if (LargestFree(bounds, obstacles, desired, minimum) is not { } area) return null;
        w = Math.Min(w, area.Width); h = Math.Min(h, area.Height);
        return new Rect(Math.Clamp(start.X, area.Left, area.Right - w), Math.Clamp(start.Y, area.Top, area.Bottom - h), w, h);
    }
}
