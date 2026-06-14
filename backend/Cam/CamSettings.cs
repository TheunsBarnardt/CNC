namespace Backend.Cam;

public enum LeadType
{
    None,
    Line,
    Arc,
}

/// <summary>
/// Operation mode selected by the user. Determines which CAM steps and
/// post-processor are appropriate for the attached machine.
/// </summary>
public enum MachineType
{
    /// <summary>Plasma torch: kerf compensation, lead-in/out, pierce delay.</summary>
    Plasma,
    /// <summary>Laser: no kerf/pierce/leads; beam on/off + power %.</summary>
    Laser,
    /// <summary>Drag knife (vinyl cutter): blade-offset arc compensation, overcut, knife Z up/down.</summary>
    VinylKnife,
}

/// <summary>
/// CAM parameters persisted on the project. Plasma-specific fields are ignored
/// (but preserved) when <see cref="OperationMode"/> is Laser.
/// </summary>
public sealed class CamSettings
{
    /// <summary>Current operation mode. Defaults to Plasma for backwards compatibility.</summary>
    public MachineType OperationMode { get; set; } = MachineType.Plasma;

    // ── Shared ─────────────────────────────────────────────────────────

    public double FeedRateMmMin { get; set; } = 2000;

    // ── Plasma-only ────────────────────────────────────────────────────

    /// <summary>Total kerf width; the offset applied per side is half this.</summary>
    public double KerfWidthMm { get; set; } = 1.5;

    public double PierceDelayS { get; set; } = 0.5;

    /// <summary>Z height during cut (THC setpoint, mm above material surface).</summary>
    public double CutHeightMm { get; set; } = 1.5;

    /// <summary>Z height when torch fires to pierce the material (higher than cut height).</summary>
    public double PierceHeightMm { get; set; } = 3.8;

    /// <summary>Z height for rapid moves between cuts (clears clamps and slag).</summary>
    public double RapidHeightMm { get; set; } = 15.0;

    public LeadType LeadInType { get; set; } = LeadType.Arc;
    public double LeadInLengthMm { get; set; } = 3;

    public LeadType LeadOutType { get; set; } = LeadType.Line;
    public double LeadOutLengthMm { get; set; } = 2;

    // ── Laser-only ─────────────────────────────────────────────────────

    /// <summary>
    /// Laser power as a percentage (0–100). Maps to GRBL S-word on a 0–1000 scale
    /// (100 % = S1000). Default 80 % is a reasonable starting point for cutting.
    /// </summary>
    public double LaserPowerPercent { get; set; } = 80;

    // ── Vinyl / drag-knife only ─────────────────────────────────────────────

    /// <summary>
    /// Distance from the blade pivot (machine Z-axis) to the blade tip (mm).
    /// At each sharp corner the machine sweeps a small arc of this radius to
    /// re-align the blade before the next segment.
    /// </summary>
    public double VinylBladeOffsetMm { get; set; } = 1.0;

    /// <summary>
    /// How far to extend the cut past the start point on closed contours (mm).
    /// Ensures the cut closes cleanly; typically 1–3 mm.
    /// </summary>
    public double VinylOvercutMm { get; set; } = 1.0;

    /// <summary>Z height (mm) when the knife is lifted between cuts.</summary>
    public double VinylKnifeUpMm { get; set; } = 3.0;

    /// <summary>Z height (mm) when the knife is down and cutting (usually 0 = surface).</summary>
    public double VinylKnifeDownMm { get; set; } = 0.0;
}
