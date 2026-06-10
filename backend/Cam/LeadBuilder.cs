using Backend.Geometry;

namespace Backend.Cam;

/// <summary>
/// Pierce-point selection and lead-in/out construction for closed contours.
///
/// With our direction convention (outer profiles CCW, holes CW) the waste
/// material is always to the RIGHT of travel, so leads always approach from /
/// exit to the right side — the pierce divot lands in scrap, not the part.
/// </summary>
public static class LeadBuilder
{
    /// <summary>Points used to approximate a 90° arc lead.</summary>
    private const int ArcSegments = 8;

    /// <summary>
    /// Builds the full motion polyline for a closed contour: lead-in, the loop
    /// (re-rotated to start at the pierce), back to the pierce point, lead-out.
    /// </summary>
    public static (List<Point2> Points, int LeadIn, int LeadOut) Build(
        Polyline2 contour, CamSettings settings)
    {
        // Pierce on the longest segment's midpoint — farthest from corners,
        // where the divot and heat distortion do the least damage.
        var pts = contour.Points;
        int seg = LongestSegmentIndex(pts);
        var a = pts[seg];
        var b = pts[(seg + 1) % pts.Count];
        var pierce = new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        // Tangent (travel direction) and right-of-travel normal at the pierce.
        double len = a.DistanceTo(b);
        var t = len > 1e-12 ? new Point2((b.X - a.X) / len, (b.Y - a.Y) / len) : new Point2(1, 0);
        var right = new Point2(t.Y, -t.X);

        var points = new List<Point2>();

        int leadIn = AppendLeadIn(points, pierce, t, right, settings);

        // The loop itself: pierce → b → … wrap … → a → pierce.
        points.Add(pierce);
        for (int i = 1; i <= pts.Count; i++)
            points.Add(pts[(seg + i) % pts.Count]);
        points.Add(pierce);

        int leadOut = AppendLeadOut(points, pierce, t, right, settings);

        return (points, leadIn, leadOut);
    }

    private static int LongestSegmentIndex(List<Point2> pts)
    {
        int best = 0;
        double bestLen = -1;
        for (int i = 0; i < pts.Count; i++)
        {
            double len = pts[i].DistanceTo(pts[(i + 1) % pts.Count]);
            if (len > bestLen)
            {
                bestLen = len;
                best = i;
            }
        }
        return best;
    }

    private static int AppendLeadIn(
        List<Point2> sink, Point2 pierce, Point2 t, Point2 right, CamSettings s)
    {
        switch (s.LeadInType)
        {
            case LeadType.Line when s.LeadInLengthMm > 0:
                // Straight approach from the waste side, arriving along -right.
                sink.Add(pierce + right * s.LeadInLengthMm);
                return 1;

            case LeadType.Arc when s.LeadInLengthMm > 0:
            {
                // Quarter arc ending at the pierce, tangent to the travel
                // direction — no direction change at the moment cutting starts.
                double r = s.LeadInLengthMm;
                var center = pierce + right * r;
                for (int i = 0; i < ArcSegments; i++)
                {
                    // u = -right (center→pierce), v = t; end (θ=0) = pierce,
                    // which the loop body adds — so stop one step short here.
                    double theta = -Math.PI / 2 + (Math.PI / 2) * i / ArcSegments;
                    sink.Add(new Point2(
                        center.X - right.X * r * Math.Cos(theta) + t.X * r * Math.Sin(theta),
                        center.Y - right.Y * r * Math.Cos(theta) + t.Y * r * Math.Sin(theta)));
                }
                return ArcSegments;
            }

            default:
                return 0;
        }
    }

    private static int AppendLeadOut(
        List<Point2> sink, Point2 pierce, Point2 t, Point2 right, CamSettings s)
    {
        switch (s.LeadOutType)
        {
            case LeadType.Line when s.LeadOutLengthMm > 0:
                // Veer off into the waste at 45° past the closure point.
                sink.Add(pierce + (t + right) * (s.LeadOutLengthMm / Math.Sqrt(2)));
                return 1;

            case LeadType.Arc when s.LeadOutLengthMm > 0:
            {
                double r = s.LeadOutLengthMm;
                var center = pierce + right * r;
                for (int i = 1; i <= ArcSegments; i++)
                {
                    double theta = (Math.PI / 2) * i / ArcSegments;
                    var p = new Point2(
                        center.X - right.X * r * Math.Cos(theta) + t.X * r * Math.Sin(theta),
                        center.Y - right.Y * r * Math.Cos(theta) + t.Y * r * Math.Sin(theta));
                    sink.Add(p);
                }
                return ArcSegments;
            }

            default:
                return 0;
        }
    }
}
