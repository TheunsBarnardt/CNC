using Backend.Cam;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Simulation;

/// <summary>A single segment in the simulation timeline (torch on or rapid).</summary>
public sealed record SimSegment(
    Point2  Start,
    Point2  End,
    bool    IsCut,
    double  StartTimeSec,
    double  EndTimeSec);

/// <summary>Precomputed simulation data. Build once with SimulationBuilder, play back via SimulationRunner.</summary>
public sealed class Simulation
{
    public required IReadOnlyList<SimSegment> Segments { get; init; }
    public double TotalTimeSec  { get; init; }
    public double TotalCutMm    { get; init; }
    public double TotalRapidMm  { get; init; }
    public double TableWidthMm  { get; init; }
    public double TableHeightMm { get; init; }
}

/// <summary>Live torch state at a point in time.</summary>
public sealed record SimState(
    Point2 Position,
    bool   TorchOn,
    double ProgressFraction,
    int    SegmentIndex);

public static class SimulationBuilder
{
    private const double RapidFeedMmMin = 5000;

    public static Simulation Build(Toolpath tp, Project proj)
    {
        var segs    = new List<SimSegment>();
        var pos     = new Point2(0, 0);
        double t    = 0;
        double cutMm  = 0;
        double rapMm  = 0;

        foreach (var cut in tp.Cuts)
        {
            // rapid to pierce
            if (!pos.Equals(cut.PierceAt))
            {
                double dist = pos.DistanceTo(cut.PierceAt);
                double dt   = dist / RapidFeedMmMin * 60;
                segs.Add(new SimSegment(pos, cut.PierceAt, false, t, t + dt));
                t      += dt;
                rapMm  += dist;
                pos     = cut.PierceAt;
            }

            // pierce delay
            t += cut.PierceDelayS;

            // cut segments
            double feedMmSec = cut.FeedRateMmMin / 60.0;
            if (feedMmSec <= 0) feedMmSec = 1;
            for (int i = 1; i < cut.Points.Count; i++)
            {
                var from = cut.Points[i - 1];
                var to   = cut.Points[i];
                double d = from.DistanceTo(to);
                double dt = d / feedMmSec;
                segs.Add(new SimSegment(from, to, true, t, t + dt));
                t      += dt;
                cutMm  += d;
                pos     = to;
            }
        }

        return new Simulation
        {
            Segments        = segs,
            TotalTimeSec    = t,
            TotalCutMm      = cutMm,
            TotalRapidMm    = rapMm,
            TableWidthMm    = proj.TableWidthMm,
            TableHeightMm   = proj.TableHeightMm,
        };
    }
}

public static class SimulationRunner
{
    public static SimState StateAt(Simulation sim, double timeSec)
    {
        if (sim.Segments.Count == 0)
            return new SimState(new Point2(0, 0), false, 0, 0);

        // Binary search for the segment containing timeSec
        int lo = 0, hi = sim.Segments.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sim.Segments[mid].EndTimeSec < timeSec) lo = mid + 1;
            else hi = mid;
        }

        var seg = sim.Segments[lo];
        double frac = seg.EndTimeSec > seg.StartTimeSec
            ? (timeSec - seg.StartTimeSec) / (seg.EndTimeSec - seg.StartTimeSec)
            : 1.0;
        frac = Math.Clamp(frac, 0, 1);

        var pos = new Point2(
            seg.Start.X + (seg.End.X - seg.Start.X) * frac,
            seg.Start.Y + (seg.End.Y - seg.Start.Y) * frac);

        return new SimState(pos, seg.IsCut,
            sim.TotalTimeSec > 0 ? timeSec / sim.TotalTimeSec : 0, lo);
    }
}
