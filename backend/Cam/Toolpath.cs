using Backend.Geometry;
using Backend.Models;

namespace Backend.Cam;

public enum CutSide
{
    /// <summary>Offset outward — keeps the enclosed piece (outer profiles).</summary>
    Outside,
    /// <summary>Offset inward — keeps the surrounding material (holes).</summary>
    Inside,
    /// <summary>No kerf compensation (open paths / scribe lines).</summary>
    OnLine,
}

/// <summary>
/// One continuous torch-on motion: pierce at <c>Points[0]</c>, cut along the
/// points, torch off at the end. Lead-in/out points are part of
/// <see cref="Points"/>; the prefix/suffix counts let simulation and posts
/// distinguish them without re-deriving geometry.
/// </summary>
public sealed class Cut
{
    public required Guid PartId { get; init; }
    public required Guid SourcePathId { get; init; }
    public string? Layer { get; init; }
    public CutSide Side { get; init; }

    /// <summary>Motion polyline in table mm, leads included. Never empty.</summary>
    public required List<Point2> Points { get; init; }

    /// <summary>Points at the start of <see cref="Points"/> that are lead-in.</summary>
    public int LeadInPointCount { get; init; }

    /// <summary>Points at the end of <see cref="Points"/> that are lead-out.</summary>
    public int LeadOutPointCount { get; init; }

    public bool IsClosedContour { get; init; }

    public double PierceDelayS { get; init; }
    public double FeedRateMmMin { get; init; }
    /// <summary>Resolved layer operation mode for this cut.</summary>
    public LayerOperationMode OperationMode { get; init; } = LayerOperationMode.Cut;
    /// <summary>Resolved laser S-word value (0–1000). Laser post-processor uses this directly.</summary>
    public int LaserPowerS { get; init; }

    /// <summary>
    /// Index pairs [Start, End] where the torch turns off and rapids across.
    /// Points at Start and End are the exact tab boundary positions (already
    /// inserted by TabBuilder). Between them the machine does M5 + G0 + re-pierce.
    /// </summary>
    public List<(int Start, int End)> TabSpans { get; init; } = [];

    public Point2 PierceAt => Points[0];
    public Point2 EndsAt => Points[^1];

    public double CutLengthMm()
    {
        double sum = 0;
        for (int i = 1; i < Points.Count; i++) sum += Points[i - 1].DistanceTo(Points[i]);
        return sum;
    }
}

/// <summary>
/// The neutral, controller-agnostic toolpath: an ordered list of cuts. Rapids
/// are implicit (end of one cut → pierce of the next); post-processors turn
/// this into a dialect, simulation plays it back directly.
/// </summary>
public sealed class Toolpath
{
    public List<Cut> Cuts { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    public double TotalCutLengthMm => Cuts.Sum(c => c.CutLengthMm());

    /// <summary>Rapid distance starting from <paramref name="origin"/> through all pierces.</summary>
    public double TotalRapidLengthMm(Point2 origin)
    {
        double sum = 0;
        var pos = origin;
        foreach (var cut in Cuts)
        {
            sum += pos.DistanceTo(cut.PierceAt);
            pos = cut.EndsAt;
        }
        return sum;
    }
}
