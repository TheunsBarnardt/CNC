using Backend.Geometry;

namespace Backend.Cam;

/// <summary>A source path placed in world coordinates, ready for CAM.</summary>
public sealed class WorldPath
{
    public required Guid PartId { get; init; }
    public required Guid SourcePathId { get; init; }
    public string? Layer { get; init; }
    public required Polyline2 Polyline { get; init; }

    // Filled by the classifier:
    public CutSide Side { get; set; }
    /// <summary>How many other closed contours contain this one.</summary>
    public int Depth { get; set; }
    /// <summary>Indices (into the classified list) of contours directly inside this one.</summary>
    public List<int> Children { get; } = [];
}

/// <summary>
/// Decides each contour's cut side from containment: even nesting depth =
/// outer profile (cut Outside), odd = hole (cut Inside). Open paths are always
/// OnLine. Also records the containment tree for inner-before-outer ordering.
/// </summary>
public static class ContourClassifier
{
    public static void Classify(List<WorldPath> paths)
    {
        int n = paths.Count;
        // contains[a][b] = closed contour a strictly contains path b.
        var contains = new bool[n, n];
        for (int a = 0; a < n; a++)
        {
            if (!paths[a].Polyline.IsClosed) continue;
            for (int b = 0; b < n; b++)
            {
                if (a == b) continue;
                contains[a, b] = ContainsPoint(paths[a].Polyline, Representative(paths[b].Polyline));
            }
        }

        for (int b = 0; b < n; b++)
        {
            int depth = 0;
            for (int a = 0; a < n; a++)
                if (contains[a, b])
                    depth++;
            paths[b].Depth = depth;
            paths[b].Side = !paths[b].Polyline.IsClosed
                ? CutSide.OnLine
                : depth % 2 == 0 ? CutSide.Outside : CutSide.Inside;
        }

        // Direct children: contained by `a` and exactly one level deeper.
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
            {
                if (contains[a, b] && paths[b].Depth == paths[a].Depth + 1)
                    paths[a].Children.Add(b);
            }
        }
    }

    /// <summary>A point that is on (and thus representative of) the path.</summary>
    private static Point2 Representative(Polyline2 path) => path.Points[0];

    public static bool ContainsPoint(Polyline2 polygon, Point2 p)
    {
        bool inside = false;
        var pts = polygon.Points;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            if (pts[i].Y > p.Y != pts[j].Y > p.Y &&
                p.X < (pts[j].X - pts[i].X) * (p.Y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                inside = !inside;
        }
        return inside;
    }
}
