using Backend.Geometry;
using Clipper2Lib;

namespace Backend.Cam;

/// <summary>
/// Kerf compensation via Clipper2 polygon offsetting (per CLAUDE.md, never
/// hand-rolled). Each closed contour is offset independently by ±kerf/2 —
/// independent offsetting keeps a stable mapping from source contour to
/// result, which ordering and lead placement rely on. (Interactions between
/// near-touching contours — e.g. two holes merging when the kerf overlaps —
/// are not modeled; the cut would fail physically anyway.)
/// </summary>
public static class KerfOffsetter
{
    /// <summary>Decimal digits Clipper keeps; 4 ⇒ 0.1 µm internal resolution.</summary>
    private const int Precision = 4;

    /// <summary>
    /// Offsets a closed contour by <paramref name="delta"/> mm (positive =
    /// outward). May return several contours (rare, e.g. a dumbbell shape
    /// pinched by an inward offset) or none (contour smaller than the offset),
    /// which callers must report as a warning.
    /// </summary>
    public static List<Polyline2> OffsetClosed(Polyline2 contour, double delta)
    {
        var path = new PathD(contour.Points.Count);
        foreach (var p in contour.Points) path.Add(new PointD(p.X, p.Y));

        // Round joins approximate the true equidistant curve — what a torch
        // physically cuts around a corner.
        var solution = Clipper.InflatePaths(
            [path], delta, JoinType.Round, EndType.Polygon, miterLimit: 2.0, precision: Precision);

        var result = new List<Polyline2>(solution.Count);
        foreach (var s in solution)
        {
            if (s.Count < 3) continue;
            result.Add(new Polyline2
            {
                IsClosed = true,
                Points = s.Select(pt => new Point2(pt.x, pt.y)).ToList(),
            });
        }
        return result;
    }

    /// <summary>Signed area (positive = counter-clockwise in our Y-up world).</summary>
    public static double SignedArea(Polyline2 contour)
    {
        double sum = 0;
        var pts = contour.Points;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            sum += (pts[j].X - pts[i].X) * (pts[j].Y + pts[i].Y);
        return sum / 2;
    }

    /// <summary>
    /// Forces traversal direction: CCW for outer profiles, CW for holes (the
    /// usual plasma "good side right of the kerf" convention).
    /// </summary>
    public static void NormalizeDirection(Polyline2 contour, CutSide side)
    {
        bool isCcw = SignedArea(contour) > 0;
        bool wantCcw = side == CutSide.Outside;
        if (isCcw != wantCcw) contour.Points.Reverse();
    }
}
