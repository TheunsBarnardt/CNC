using Backend.Geometry;
using Backend.Models;

namespace Backend.Import;

public static class GeometryNormalizer
{
    /// <summary>
    /// Translates a file's geometry so its bounding box's lower-left corner is
    /// at (0,0). Every imported file then lives in a predictable local space,
    /// and placement on the table is purely the part transform (Task 2).
    /// </summary>
    public static void NormalizeToOrigin(ImportedFile file)
    {
        if (file.Paths.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (var path in file.Paths)
        {
            foreach (var p in path.Polyline.Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
            }
        }

        if (Math.Abs(minX) < 1e-9 && Math.Abs(minY) < 1e-9) return;
        foreach (var path in file.Paths)
        {
            var pts = path.Polyline.Points;
            for (int i = 0; i < pts.Count; i++)
                pts[i] = new Point2(pts[i].X - minX, pts[i].Y - minY);
        }
    }
}
