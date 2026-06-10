using Backend.Geometry;

namespace Backend.Models;

/// <summary>
/// The single definition of how a <see cref="Part"/>'s transform maps file-local
/// geometry into table coordinates:
///
///   world = (X, Y) + R(RotationDeg) · (local − c) + c
///
/// where c is the file's local bounding-box center. Translation and rotation
/// are therefore independent — rotating spins the part in place, which is what
/// both manual dragging and the future auto-nester want. The frontend viewport
/// mirrors this math exactly; the CAM engine (Task 3) consumes it from here.
/// </summary>
public static class PartTransform
{
    /// <summary>Bounding-box center of a file's local geometry (the rotation pivot).</summary>
    public static Point2 LocalCenter(ImportedFile file)
    {
        if (file.Paths.Count == 0) return new Point2(0, 0);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var path in file.Paths)
        {
            var (min, max) = path.Polyline.Bounds();
            if (min.X < minX) minX = min.X;
            if (min.Y < minY) minY = min.Y;
            if (max.X > maxX) maxX = max.X;
            if (max.Y > maxY) maxY = max.Y;
        }
        return new Point2((minX + maxX) / 2, (minY + maxY) / 2);
    }

    public static Point2 Apply(Part part, Point2 pivot, Point2 local)
    {
        double r = part.RotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        double dx = local.X - pivot.X, dy = local.Y - pivot.Y;
        return new Point2(
            part.X + pivot.X + dx * cos - dy * sin,
            part.Y + pivot.Y + dx * sin + dy * cos);
    }

    /// <summary>All of a part's polylines in table coordinates.</summary>
    public static List<Polyline2> WorldPolylines(Part part, ImportedFile file)
    {
        var pivot = LocalCenter(file);
        var result = new List<Polyline2>(file.Paths.Count);
        foreach (var path in file.Paths)
        {
            result.Add(new Polyline2
            {
                IsClosed = path.Polyline.IsClosed,
                Points = path.Polyline.Points.Select(p => Apply(part, pivot, p)).ToList(),
            });
        }
        return result;
    }

    /// <summary>Axis-aligned world bounds of a placed part.</summary>
    public static (Point2 Min, Point2 Max) WorldBounds(Part part, ImportedFile file)
    {
        var pivot = LocalCenter(file);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var path in file.Paths)
        {
            foreach (var local in path.Polyline.Points)
            {
                var p = Apply(part, pivot, local);
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        if (minX > maxX) return (new Point2(0, 0), new Point2(0, 0));
        return (new Point2(minX, minY), new Point2(maxX, maxY));
    }
}
