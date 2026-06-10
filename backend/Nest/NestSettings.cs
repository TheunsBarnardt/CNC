namespace Backend.Nest;

/// <summary>Settings for one auto-nest run. All lengths in mm.</summary>
public sealed class NestSettings
{
    /// <summary>Minimum distance from any part to the sheet edges.</summary>
    public double MarginMm { get; set; } = 10;

    /// <summary>Minimum distance between any two parts.</summary>
    public double SpacingMm { get; set; } = 5;

    /// <summary>
    /// Rotation candidates tried per part: 0°, step, 2·step, … below 360°.
    /// 0 disables rotation (parts keep their current angle's footprint upright).
    /// </summary>
    public int RotationStepDeg { get; set; } = 90;

    public void Validate()
    {
        if (MarginMm < 0) throw new ArgumentException("Margin must be ≥ 0.");
        if (SpacingMm < 0) throw new ArgumentException("Spacing must be ≥ 0.");
        if (RotationStepDeg is < 0 or >= 360)
            throw new ArgumentException("Rotation step must be 0–359°.");
    }
}
