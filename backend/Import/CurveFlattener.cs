using Backend.Geometry;

namespace Backend.Import;

/// <summary>
/// Adaptive cubic-bézier flattening (recursive De Casteljau subdivision with a
/// flatness test). Shared by all importers so every curved entity ends up as a
/// polyline at the same chord tolerance.
/// </summary>
public static class CurveFlattener
{
    /// <summary>Default chord tolerance in mm — comfortably below plasma kerf widths.</summary>
    public const double DefaultToleranceMm = 0.05;

    private const int MaxDepth = 24;

    /// <summary>
    /// Appends points approximating the cubic (p0,c1,c2,p3) to
    /// <paramref name="sink"/>, excluding p0, including p3.
    /// </summary>
    public static void FlattenCubic(
        List<Point2> sink, Point2 p0, Point2 c1, Point2 c2, Point2 p3, double tolerance)
    {
        Subdivide(sink, p0, c1, c2, p3, tolerance, 0);
        sink.Add(p3);
    }

    private static void Subdivide(
        List<Point2> sink, Point2 p0, Point2 c1, Point2 c2, Point2 p3, double tol, int depth)
    {
        if (depth >= MaxDepth || IsFlat(p0, c1, c2, p3, tol)) return;

        // De Casteljau split at t = 0.5.
        var p01 = Mid(p0, c1);
        var p12 = Mid(c1, c2);
        var p23 = Mid(c2, p3);
        var p012 = Mid(p01, p12);
        var p123 = Mid(p12, p23);
        var mid = Mid(p012, p123);

        Subdivide(sink, p0, p01, p012, mid, tol, depth + 1);
        sink.Add(mid);
        Subdivide(sink, mid, p123, p23, p3, tol, depth + 1);
    }

    private static Point2 Mid(Point2 a, Point2 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    /// <summary>
    /// Flat when both control points are within tolerance of the chord p0→p3
    /// (max control-point deviation bounds the curve's deviation).
    /// </summary>
    private static bool IsFlat(Point2 p0, Point2 c1, Point2 c2, Point2 p3, double tol)
    {
        double dx = p3.X - p0.X, dy = p3.Y - p0.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-18)
        {
            // Degenerate chord — fall back to control-point distance from p0.
            return c1.DistanceTo(p0) <= tol && c2.DistanceTo(p0) <= tol;
        }

        double d1 = Math.Abs(dx * (p0.Y - c1.Y) - dy * (p0.X - c1.X));
        double d2 = Math.Abs(dx * (p0.Y - c2.Y) - dy * (p0.X - c2.X));
        double maxDistSq = Math.Max(d1, d2);
        return maxDistSq * maxDistSq <= tol * tol * lenSq;
    }
}
