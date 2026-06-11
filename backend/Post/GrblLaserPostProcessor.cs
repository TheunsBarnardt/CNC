using System.Globalization;
using Backend.Cam;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Post;

/// <summary>
/// GRBL-dialect laser post-processor. Beam on/off via M3/M5 (constant-power
/// mode), S-word proportional to <see cref="CamSettings.LaserPowerPercent"/>
/// on a 0–1000 GRBL scale (configurable via $30). No pierce delay, no kerf
/// compensation (handled in CAM), no Z/height words.
///
/// Enable GRBL laser mode on the controller with $32=1 before running these
/// programs — without it the laser fires continuously even during G0 rapids.
/// </summary>
public sealed class GrblLaserPostProcessor : IPostProcessor
{
    public string Id => "grbl-laser";
    public string DisplayName => "GRBL Laser";
    public string Description =>
        "GRBL/grblHAL laser. Beam via M3/M5, power via S-word (0–1000). Set $32=1 on controller.";
    public string FileExtension => ".nc";

    public GcodeProgram Generate(Toolpath toolpath, Project project)
    {
        var g = new GcodeProgram();
        var cam = project.Cam;

        // S-word range: 0-1000 (GRBL default $30=1000). Map 0-100 % linearly.
        int sValue = (int)Math.Clamp(Math.Round(cam.LaserPowerPercent * 10), 0, 1000);

        var workOrigin = OriginPoint(project.Table);

        g.Lines.Add($"; {project.Name} — {DisplayName} post");
        g.Lines.Add($"; Generated {DateTime.Now:yyyy-MM-dd HH:mm} by DIY GRBL Cutting CAM");
        g.Lines.Add($"; Table {Num(project.Table.WidthMm)}x{Num(project.Table.HeightMm)}mm, work origin: {project.Table.Origin}");
        g.Lines.Add($"; Laser power {Num(cam.LaserPowerPercent)}% (S{sValue}) | Feed {Num(cam.FeedRateMmMin)}mm/min");
        g.Lines.Add("; IMPORTANT: set $32=1 (laser mode) on the controller before running");
        foreach (var warning in toolpath.Warnings)
            g.Lines.Add($"; WARNING: {warning}");
        g.Warnings.AddRange(toolpath.Warnings);

        if (toolpath.Cuts.Count == 0)
            g.Warnings.Add("Toolpath is empty — generated program contains no motion.");

        g.Lines.Add("G21 ; millimeters");
        g.Lines.Add("G90 ; absolute coordinates");
        g.Lines.Add("G17 ; XY plane");
        g.Lines.Add("G94 ; feed per minute");
        g.Lines.Add("M5 ; laser off (safety: known state before any motion)");

        int n = 0;
        foreach (var cut in toolpath.Cuts)
        {
            n++;
            g.Lines.Add($"; --- cut {n}/{toolpath.Cuts.Count}: {cut.Side}" +
                        (cut.Layer is null ? "" : $", layer {cut.Layer}") +
                        $", {Num(Math.Round(cut.CutLengthMm(), 1))}mm ---");

            g.Lines.Add($"G0 {Xy(cut.Points[0], workOrigin)}");
            g.Lines.Add($"M3 S{sValue} ; laser on");

            for (int i = 1; i < cut.Points.Count; i++)
            {
                string feed = i == 1 ? $" F{Num(cut.FeedRateMmMin)}" : "";
                g.Lines.Add($"G1 {Xy(cut.Points[i], workOrigin)}{feed}");
            }

            g.Lines.Add("M5 ; laser off");
        }

        g.Lines.Add("G0 X0 Y0 ; park at work origin");
        g.Lines.Add("M2 ; program end");
        return g;
    }

    private static Point2 OriginPoint(TableSettings table) =>
        GrblPlasmaPostProcessor.OriginPoint(table);

    private static string Xy(Point2 p, Point2 origin) =>
        $"X{Num(Math.Round(p.X - origin.X, 3))} Y{Num(Math.Round(p.Y - origin.Y, 3))}";

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
