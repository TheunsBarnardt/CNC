using Backend.Geometry;

namespace Backend.Cam;

/// <summary>
/// Orders cuts safely and cheaply:
///  1. HARD constraint — a contour may only be cut after every contour inside
///     it is done (cutting the outer first drops the slug, and anything inside
///     it is lost / no longer referenced).
///  2. Among the currently allowed cuts, nearest-neighbor on pierce points to
///     keep rapids reasonable (greedy, not optimal — fine for v1).
/// </summary>
public static class CutOrderer
{
    /// <summary>Returns indices into <paramref name="paths"/> in cutting order.</summary>
    public static List<int> Order(List<WorldPath> paths, Point2 startAt)
    {
        int n = paths.Count;
        // remainingChildren[i] = contours inside i not yet cut.
        var remainingChildren = new int[n];
        var parents = new List<int>[n];
        for (int i = 0; i < n; i++) parents[i] = [];
        for (int a = 0; a < n; a++)
        {
            remainingChildren[a] = paths[a].Children.Count;
            foreach (int child in paths[a].Children) parents[child].Add(a);
        }

        var order = new List<int>(n);
        var done = new bool[n];
        var pos = startAt;

        for (int step = 0; step < n; step++)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (done[i] || remainingChildren[i] > 0) continue;
                double d = pos.DistanceTo(paths[i].Polyline.Points[0]);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            if (best < 0) break; // cycle can't happen with strict containment, but stay safe

            done[best] = true;
            order.Add(best);
            pos = paths[best].Polyline.Points[0];
            foreach (int parent in parents[best]) remainingChildren[parent]--;
        }

        return order;
    }
}
