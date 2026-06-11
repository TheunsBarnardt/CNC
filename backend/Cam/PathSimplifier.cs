using Backend.Geometry;

namespace Backend.Cam;

/// <summary>
/// Ramer-Douglas-Peucker path simplification. Reduces the number of vertices
/// in a polyline while keeping the visual shape within a tolerance (mm).
/// Used by the node-editing "Simplify" action to remove redundant imported
/// vertices without changing the overall outline.
/// </summary>
public static class PathSimplifier
{
    /// <summary>
    /// Simplify a polyline in-place. Closed polylines have their closure
    /// point-set split so the join is also evaluated.
    /// </summary>
    public static List<Point2> Simplify(List<Point2> points, bool closed, double epsilon)
    {
        if (points.Count < 3) return new List<Point2>(points);

        if (closed)
        {
            // Duplicate first point at end so the closing segment is evaluated.
            var ring = new List<Point2>(points) { points[0] };
            var result = RdpRecurse(ring, 0, ring.Count - 1, epsilon);
            result.RemoveAt(result.Count - 1); // remove duplicated closure
            return result.Count >= 3 ? result : new List<Point2>(points); // safety: don't collapse below 3
        }

        var simplified = RdpRecurse(points, 0, points.Count - 1, epsilon);
        return simplified.Count >= 2 ? simplified : new List<Point2>(points);
    }

    private static List<Point2> RdpRecurse(List<Point2> pts, int start, int end, double epsilon)
    {
        if (end - start < 2)
        {
            var trivial = new List<Point2>();
            for (int i = start; i <= end; i++) trivial.Add(pts[i]);
            return trivial;
        }

        double maxDist = 0;
        int maxIdx = start;
        for (int i = start + 1; i < end; i++)
        {
            double d = PointToSegmentDistance(pts[i], pts[start], pts[end]);
            if (d > maxDist) { maxDist = d; maxIdx = i; }
        }

        if (maxDist > epsilon)
        {
            var left = RdpRecurse(pts, start, maxIdx, epsilon);
            var right = RdpRecurse(pts, maxIdx, end, epsilon);
            // Merge: left already ends at maxIdx, right starts at maxIdx — skip duplicate.
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        return [pts[start], pts[end]];
    }

    private static double PointToSegmentDistance(Point2 p, Point2 a, Point2 b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double lenSq = abx * abx + aby * aby;
        if (lenSq < 1e-14) return p.DistanceTo(a);
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq, 0, 1);
        double projX = a.X + t * abx, projY = a.Y + t * aby;
        double dx = p.X - projX, dy = p.Y - projY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
