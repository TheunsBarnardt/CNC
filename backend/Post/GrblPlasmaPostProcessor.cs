using System.Globalization;
using Backend.Cam;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Post;

/// <summary>
/// GRBL-dialect plasma post: torch on/off via M3/M5 (spindle-enable pin drives
/// the torch relay — the standard DIY GRBL plasma wiring), G4 pierce dwell,
/// Z-axis height sequencing (rapid → pierce → cut height), absolute mm (G21 G90).
/// For machines without THC or manual Z: set RapidHeightMm=0, CutHeightMm=0,
/// PierceHeightMm=0 to omit all Z moves (the G0 Z0 lines still appear but are
/// no-ops if your machine has no Z axis wired).
/// </summary>
public sealed class GrblPlasmaPostProcessor : IPostProcessor
{
    public string Id => "grbl-plasma";
    public string DisplayName => "GRBL Plasma";
    public string Description =>
        "GRBL/grblHAL plasma. Torch via M3/M5, pierce dwell via G4, absolute mm (G21 G90).";
    public string FileExtension => ".nc";

    public GcodeProgram Generate(Toolpath toolpath, Project project)
    {
        var g = new GcodeProgram();
        var cam = project.Cam;

        // Geometry lives in table coordinates (origin bottom-left, Y-up, mm).
        // The machine's work zero is wherever the project says it is, so shift
        // every coordinate to make that point X0 Y0. Axis directions are kept
        // Y-up; flipping axes is a machine setup ($3) concern, not the post's.
        var workOrigin = OriginPoint(project.Table);

        g.Lines.Add($"; {project.Name} — {DisplayName} post");
        g.Lines.Add($"; Generated {DateTime.Now:yyyy-MM-dd HH:mm} by DIY GRBL Cutting CAM");
        g.Lines.Add($"; Table {Num(project.Table.WidthMm)}x{Num(project.Table.HeightMm)}mm, work origin: {project.Table.Origin}");
        g.Lines.Add($"; Kerf {Num(cam.KerfWidthMm)}mm (compensated in path) | Feed {Num(cam.FeedRateMmMin)}mm/min | Pierce delay {Num(cam.PierceDelayS)}s");
        g.Lines.Add($"; Heights — rapid Z{Num(cam.RapidHeightMm)}mm | pierce Z{Num(cam.PierceHeightMm)}mm | cut Z{Num(cam.CutHeightMm)}mm");
        foreach (var warning in toolpath.Warnings)
            g.Lines.Add($"; WARNING: {warning}");
        g.Warnings.AddRange(toolpath.Warnings);

        if (toolpath.Cuts.Count == 0)
            g.Warnings.Add("Toolpath is empty — generated program contains no motion.");

        g.Lines.Add("G21 ; millimeters");
        g.Lines.Add("G90 ; absolute coordinates");
        g.Lines.Add("G17 ; XY plane");
        g.Lines.Add("G94 ; feed per minute");
        g.Lines.Add("M5 ; torch off (safety)");
        g.Lines.Add($"G0 Z{Num(cam.RapidHeightMm)} ; lift to rapid height");

        int n = 0;
        foreach (var cut in toolpath.Cuts)
        {
            n++;
            g.Lines.Add($"; --- cut {n}/{toolpath.Cuts.Count}: {cut.Side}" +
                        (cut.Layer is null ? "" : $", layer {cut.Layer}") +
                        $", {Num(Math.Round(cut.CutLengthMm(), 1))}mm" +
                        (cut.LeadInPointCount > 0 ? $", lead-in {cut.LeadInPointCount}pts" : "") +
                        (cut.LeadOutPointCount > 0 ? $", lead-out {cut.LeadOutPointCount}pts" : "") +
                        " ---");

            // Rapid XY to pierce point (already at rapid height from previous lift)
            g.Lines.Add($"G0 {Xy(cut.Points[0], workOrigin)} ; rapid to pierce");
            // Lower to pierce height
            g.Lines.Add($"G0 Z{Num(cam.PierceHeightMm)} ; pierce height");
            // Fire torch and dwell
            g.Lines.Add("M3 S1000 ; torch on");
            if (cut.PierceDelayS > 0)
                g.Lines.Add($"G4 P{Num(cut.PierceDelayS)} ; pierce dwell");
            // Drop to cut height
            g.Lines.Add($"G0 Z{Num(cam.CutHeightMm)} ; cut height");

            // Feed through all cut points (lead-in → cut → lead-out are all in Points)
            for (int i = 1; i < cut.Points.Count; i++)
            {
                string feed = i == 1 ? $" F{Num(cut.FeedRateMmMin)}" : "";
                g.Lines.Add($"G1 {Xy(cut.Points[i], workOrigin)}{feed}");
            }

            // Torch off, lift to rapid height
            g.Lines.Add("M5 ; torch off");
            g.Lines.Add($"G0 Z{Num(cam.RapidHeightMm)} ; lift to rapid height");
        }

        g.Lines.Add("G0 X0 Y0 ; park at work origin");
        g.Lines.Add("M2 ; program end");
        return g;
    }

    /// <summary>The work origin's location in table coordinates.</summary>
    internal static Point2 OriginPoint(TableSettings table) => table.Origin switch
    {
        TableOrigin.BottomLeft => new Point2(0, 0),
        TableOrigin.BottomRight => new Point2(table.WidthMm, 0),
        TableOrigin.TopLeft => new Point2(0, table.HeightMm),
        TableOrigin.TopRight => new Point2(table.WidthMm, table.HeightMm),
        TableOrigin.Center => new Point2(table.WidthMm / 2, table.HeightMm / 2),
        _ => new Point2(0, 0),
    };

    private static string Xy(Point2 p, Point2 origin) =>
        $"X{Num(Math.Round(p.X - origin.X, 3))} Y{Num(Math.Round(p.Y - origin.Y, 3))}";

    /// <summary>Invariant-culture number (GRBL expects '.' decimals, never ',').</summary>
    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
