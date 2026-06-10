namespace Backend.Models;

/// <summary>
/// A reusable cutting preset for one material + thickness. Holds the
/// material-dependent CAM numbers; lead geometry stays a per-project choice.
/// Profiles persist with the app (not the project), so they carry across jobs.
/// </summary>
public sealed class MaterialProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    /// <summary>Material family, e.g. "Mild steel", "Aluminium".</summary>
    public string Material { get; set; } = "";
    public double ThicknessMm { get; set; }

    public double KerfWidthMm { get; set; } = 1.5;
    public double FeedRateMmMin { get; set; } = 2000;
    public double PierceDelayS { get; set; } = 0.5;
    public double CutHeightMm { get; set; } = 1.5;
    public double PierceHeightMm { get; set; } = 3.8;
}
