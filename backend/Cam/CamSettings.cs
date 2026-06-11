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

    /// <summary>Carried through to the plasma post-processor; not used by geometry.</summary>
    public double CutHeightMm { get; set; } = 1.5;

    /// <summary>Carried through to the plasma post-processor; not used by geometry.</summary>
    public double PierceHeightMm { get; set; } = 3.8;

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
}
