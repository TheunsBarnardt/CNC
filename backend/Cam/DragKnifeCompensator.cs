using Backend.Geometry;

namespace Backend.Cam;

/// <summary>
/// Transforms a design path into the machine pivot path that makes a trailing
/// drag-knife blade trace the original design.
///
/// Physical model: the machine positions the blade pivot; the blade tip trails
/// behind by <c>bladeOffsetMm</c> in the direction of motion. At corners the
/// pivot sweeps a small arc around the corner vertex so the blade re-aligns
/// before the next segment — this leaves a tiny arc mark on the material, which
/// is normal drag-knife behaviour. For closed paths an overcut segment extends
/// the cut past the start point to ensure the contour closes cleanly.
///
/// Returns a new open <see cref="Polyline2"/> representing the machine path.
/// </summary>
public static class DragKnifeCompensator
{
    private const double MinTurnAngleDeg = 5.0;
    private const double ArcStepDeg = 15.0;

    public static Polyline2 Compensate(Polyline2 path, double bladeOffsetMm, double overcutMm)
    {
        var pts = path.Points;
        if (pts.Count < 2)
            return new Polyline2 { IsClosed = false, Points = pts.ToList() };

        bool closed = path.IsClosed;
        int n = pts.Count;
        int numSegments = closed ? n : n - 1;

        // Unit direction for each segment.
        var dirs = new Point2[numSegments];
        for (int i = 0; i < numSegments; i++)
            dirs[i] = Normalize(pts[(i + 1) % n] - pts[i]);

        var result = new List<Point2>();

        // Initial pivot: blade tip at P[0], pivot trails behind by bladeOffset.
        result.Add(pts[0] - dirs[0] * bladeOffsetMm);

        for (int i = 0; i < numSegments; i++)
        {
            var vertex = pts[(i + 1) % n];

            // End of segment i: pivot arrives at vertex still trailing d[i].
            var segEnd = vertex - dirs[i] * bladeOffsetMm;
            result.Add(segEnd);

            // Re-align arc to the next segment's start position (if there is one).
            bool hasNext = i < numSegments - 1 || closed;
            if (!hasNext) continue;

            var dNext = dirs[(i + 1) % numSegments];
            double angleDeg = SignedAngleDeg(dirs[i], dNext);

            if (Math.Abs(angleDeg) > MinTurnAngleDeg && bladeOffsetMm > 1e-6)
            {
                // Pivot arcs around the corner vertex so the blade tip re-aligns.
                AppendArc(result, vertex, segEnd, angleDeg, bladeOffsetMm);
            }
        }

        // Overcut: extend past the closing point along the first segment.
        if (closed && overcutMm > 1e-6 && numSegments > 0)
            result.Add(result[^1] + dirs[0] * overcutMm);

        return new Polyline2 { IsClosed = false, Points = result };
    }

    // Signed angle in degrees from v1 to v2 (positive = CCW in Y-up coordinates).
    private static double SignedAngleDeg(Point2 v1, Point2 v2)
    {
        double cross = v1.X * v2.Y - v1.Y * v2.X;
        double dot   = v1.X * v2.X + v1.Y * v2.Y;
        return Math.Atan2(cross, dot) * 180.0 / Math.PI;
    }

    // Appends arc points from arcStart around center, sweeping by sweepDeg degrees.
    // sweepDeg is signed: positive = CCW, negative = CW (matching Y-up convention).
    private static void AppendArc(
        List<Point2> result, Point2 center, Point2 arcStart, double sweepDeg, double radius)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepDeg) / ArcStepDeg));
        double startAngle = Math.Atan2(arcStart.Y - center.Y, arcStart.X - center.X);
        double sweepRad   = sweepDeg * Math.PI / 180.0;

        for (int i = 1; i <= steps; i++)
        {
            double a = startAngle + sweepRad * i / steps;
            result.Add(new Point2(
                center.X + radius * Math.Cos(a),
                center.Y + radius * Math.Sin(a)));
        }
    }

    private static Point2 Normalize(Point2 v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return len < 1e-9 ? new Point2(1, 0) : new Point2(v.X / len, v.Y / len);
    }
}
