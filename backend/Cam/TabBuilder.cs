using Backend.Geometry;

namespace Backend.Cam;

/// <summary>
/// Inserts holding-tab (bridge) gaps evenly along a cut path so the part stays
/// attached to surrounding material during the cut.  Each tab is a short span
/// where the torch turns off; the post-processor emits M5 + rapid + re-pierce.
/// </summary>
public static class TabBuilder
{
    /// <summary>
    /// Distribute <paramref name="tabCount"/> tabs of width
    /// <paramref name="tabWidthMm"/> evenly along <paramref name="points"/>.
    /// Returns a new, potentially-longer points list (tab boundary positions
    /// interpolated in) and the index ranges [Start, End] for each tab.
    /// Returns the original list unchanged when tabs cannot be placed.
    /// </summary>
    public static (List<Point2> Points, List<(int Start, int End)> Tabs) Apply(
        IReadOnlyList<Point2> points, int tabCount, double tabWidthMm)
    {
        if (tabCount <= 0 || tabWidthMm <= 0 || points.Count < 2)
            return (new List<Point2>(points), []);

        int n = points.Count;

        // Cumulative arc-length per vertex.
        var cum = new double[n];
        for (int i = 1; i < n; i++)
            cum[i] = cum[i - 1] + points[i - 1].DistanceTo(points[i]);
        double total = cum[n - 1];

        // Need at least 2× tab width of cut material per tab.
        if (total < tabCount * tabWidthMm * 2)
            return (new List<Point2>(points), []);

        double spacing = total / tabCount;

        // Collect sorted boundary distances: (distance, isStart).
        var bounds = new List<(double Dist, bool IsStart)>(tabCount * 2);
        for (int t = 0; t < tabCount; t++)
        {
            double center = (t + 0.5) * spacing;
            double s = center - tabWidthMm / 2;
            double e = center + tabWidthMm / 2;
            if (s >= 0 && e <= total)
            {
                bounds.Add((s, true));
                bounds.Add((e, false));
            }
        }
        bounds.Sort((a, b) => a.Dist.CompareTo(b.Dist));

        // Walk path, inserting interpolated points at each boundary.
        var result = new List<Point2>(n + bounds.Count * 2) { points[0] };
        var tabs = new List<(int Start, int End)>(tabCount);
        int bi = 0;
        double acc = 0;

        for (int i = 0; i < n - 1; i++)
        {
            double segLen = points[i].DistanceTo(points[i + 1]);
            double segEnd = acc + segLen;

            // Insert any boundaries that fall within this segment.
            while (bi < bounds.Count && bounds[bi].Dist <= segEnd + 1e-9)
            {
                double t = segLen < 1e-9 ? 0.0
                         : Math.Clamp((bounds[bi].Dist - acc) / segLen, 0.0, 1.0);
                result.Add(Lerp(points[i], points[i + 1], t));
                int idx = result.Count - 1;

                if (bounds[bi].IsStart)
                    tabs.Add((idx, -1));
                else if (tabs.Count > 0 && tabs[^1].End == -1)
                    tabs[tabs.Count - 1] = (tabs[^1].Start, idx);

                bi++;
            }

            acc = segEnd;
            result.Add(points[i + 1]);
        }

        return (result, tabs.Where(t => t.End >= 0).ToList());
    }

    private static Point2 Lerp(Point2 a, Point2 b, double t) =>
        new(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
}
