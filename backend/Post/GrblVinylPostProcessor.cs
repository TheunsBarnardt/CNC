using System.Globalization;
using Backend.Cam;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Post;

/// <summary>
/// GRBL-dialect vinyl / drag-knife post-processor. Knife up/down via Z axis;
/// no spindle (M3/M5) commands. Blade-offset arc compensation and overcut are
/// handled by <see cref="DragKnifeCompensator"/> in the CAM engine — the points
/// in each cut already represent the machine pivot path.
/// </summary>
public sealed class GrblVinylPostProcessor : IPostProcessor
{
    public string Id => "grbl-vinyl";
    public string DisplayName => "GRBL Vinyl / Drag-Knife";
    public string Description =>
        "GRBL/grblHAL drag-knife. Knife via Z axis, blade-offset arcs compensated in path, overcut on closed contours.";
    public string FileExtension => ".nc";

    public GcodeProgram Generate(Toolpath toolpath, Project project)
    {
        var g = new GcodeProgram();
        var cam = project.Cam;

        double up   = cam.VinylKnifeUpMm;
        double down = cam.VinylKnifeDownMm;

        var workOrigin = GrblPlasmaPostProcessor.OriginPoint(project.Table);

        g.Lines.Add($"; {project.Name} — {DisplayName} post");
        g.Lines.Add($"; Generated {DateTime.Now:yyyy-MM-dd HH:mm} by DIY GRBL Cutting CAM");
        g.Lines.Add($"; Table {Num(project.Table.WidthMm)}x{Num(project.Table.HeightMm)}mm, work origin: {project.Table.Origin}");
        g.Lines.Add($"; Blade offset {Num(cam.VinylBladeOffsetMm)}mm | Overcut {Num(cam.VinylOvercutMm)}mm | Feed {Num(cam.FeedRateMmMin)}mm/min");
        g.Lines.Add($"; Knife up Z{Num(up)}mm, knife down Z{Num(down)}mm");
        foreach (var warning in toolpath.Warnings)
            g.Lines.Add($"; WARNING: {warning}");
        g.Warnings.AddRange(toolpath.Warnings);

        if (toolpath.Cuts.Count == 0)
            g.Warnings.Add("Toolpath is empty — generated program contains no motion.");

        g.Lines.Add("G21 ; millimeters");
        g.Lines.Add("G90 ; absolute coordinates");
        g.Lines.Add("G17 ; XY plane");
        g.Lines.Add("G94 ; feed per minute");
        g.Lines.Add($"G0 Z{Num(up)} ; knife up (safety)");

        int n = 0;
        foreach (var cut in toolpath.Cuts)
        {
            n++;
            g.Lines.Add($"; --- cut {n}/{toolpath.Cuts.Count}: {cut.Side}" +
                        (cut.Layer is null ? "" : $", layer {cut.Layer}") +
                        $", {Num(Math.Round(cut.CutLengthMm(), 1))}mm ---");

            // Rapid to start with knife up, then lower.
            g.Lines.Add($"G0 {Xy(cut.Points[0], workOrigin)} ; rapid to cut start");
            g.Lines.Add($"G0 Z{Num(down)} ; knife down");

            // Cut along the (already compensated) pivot path.
            for (int i = 1; i < cut.Points.Count; i++)
            {
                string feed = i == 1 ? $" F{Num(cut.FeedRateMmMin)}" : "";
                g.Lines.Add($"G1 {Xy(cut.Points[i], workOrigin)}{feed}");
            }

            g.Lines.Add($"G0 Z{Num(up)} ; knife up");
        }

        g.Lines.Add("G0 X0 Y0 ; park at work origin");
        g.Lines.Add("M2 ; program end");
        return g;
    }

    private static string Xy(Point2 p, Point2 origin) =>
        $"X{Num(Math.Round(p.X - origin.X, 3))} Y{Num(Math.Round(p.Y - origin.Y, 3))}";

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
