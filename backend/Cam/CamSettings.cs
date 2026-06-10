namespace Backend.Cam;

public enum LeadType
{
    None,
    Line,
    Arc,
}

/// <summary>
/// Plasma CAM parameters. Lengths in mm, feed in mm/min, delays in seconds.
/// Stored on the project (so it persists in the project file); Task 6 layers
/// material profiles on top of these.
/// </summary>
public sealed class CamSettings
{
    /// <summary>Total kerf width; the offset applied per side is half this.</summary>
    public double KerfWidthMm { get; set; } = 1.5;

    public double FeedRateMmMin { get; set; } = 2000;

    public double PierceDelayS { get; set; } = 0.5;

    /// <summary>Carried through to the post-processor (Task 4); not used by geometry.</summary>
    public double CutHeightMm { get; set; } = 1.5;

    /// <summary>Carried through to the post-processor (Task 4); not used by geometry.</summary>
    public double PierceHeightMm { get; set; } = 3.8;

    public LeadType LeadInType { get; set; } = LeadType.Arc;
    public double LeadInLengthMm { get; set; } = 3;

    public LeadType LeadOutType { get; set; } = LeadType.Line;
    public double LeadOutLengthMm { get; set; } = 2;
}
