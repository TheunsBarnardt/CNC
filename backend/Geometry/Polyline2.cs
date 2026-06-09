namespace Backend.Geometry;

/// <summary>
/// A flattened 2D path: straight segments between consecutive points.
///
/// This is the app's neutral geometry primitive. Importers flatten curves
/// (béziers, arcs, splines) to polylines at import time using a small chord
/// tolerance — downstream code (placement, CAM offsetting via Clipper2,
/// simulation) then only ever deals with polylines.
/// </summary>
public sealed class Polyline2
{
    public List<Point2> Points { get; init; } = [];

    /// <summary>
    /// True when the path is a closed contour (last point connects back to the
    /// first). Closed contours are cuttable outlines; open ones are engraves /
    /// single lines.
    /// </summary>
    public bool IsClosed { get; init; }

    public double Length()
    {
        double sum = 0;
        for (int i = 1; i < Points.Count; i++) sum += Points[i - 1].DistanceTo(Points[i]);
        if (IsClosed && Points.Count > 1) sum += Points[^1].DistanceTo(Points[0]);
        return sum;
    }

    public (Point2 Min, Point2 Max) Bounds()
    {
        if (Points.Count == 0) return (new Point2(0, 0), new Point2(0, 0));
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return (new Point2(minX, minY), new Point2(maxX, maxY));
    }
}
