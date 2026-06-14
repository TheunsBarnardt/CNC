using Backend.Geometry;
using Backend.Models;

namespace Backend.Cam;

public enum PreflightStatus { Pass, Warn, Fail }

public sealed record PreflightItem(string Name, PreflightStatus Status, string Detail);

/// <summary>
/// Validates a project before the operator sends it to the machine.
/// All checks run offline — no machine connection needed.
/// Returns a list of items with Pass/Warn/Fail status; callers should block
/// Run Job when any Fail is present.
/// </summary>
public static class PreflightChecker
{
    public static List<PreflightItem> Check(Project project, CamSettings settings)
    {
        var items = new List<PreflightItem>();

        items.Add(CheckHasParts(project));
        items.Add(CheckBounds(project));
        items.Add(CheckLayerAssignments(project));
        items.Add(CheckLeadInClearance(project, settings));
        items.Add(CheckPierceCount(project, settings));
        items.Add(CheckNoGoZones(project, settings));

        return items;
    }

    private static PreflightItem CheckHasParts(Project project)
    {
        int n = project.Parts.Count(p => project.Files.Any(f => f.Id == p.FileId && f.Visible));
        if (n == 0)
            return new("Parts", PreflightStatus.Fail, "No visible parts on the table.");
        return new("Parts", PreflightStatus.Pass, $"{n} part(s) ready to cut.");
    }

    private static PreflightItem CheckBounds(Project project)
    {
        double tw = project.Table.WidthMm, th = project.Table.HeightMm;
        var outOfBounds = new List<string>();
        foreach (var part in project.Parts)
        {
            var file = project.Files.FirstOrDefault(f => f.Id == part.FileId);
            if (file is null || !file.Visible) continue;
            var (min, max) = PartTransform.WorldBounds(part, file);
            if (min.X < -0.1 || min.Y < -0.1 || max.X > tw + 0.1 || max.Y > th + 0.1)
                outOfBounds.Add(file.DisplayName);
        }
        if (outOfBounds.Count > 0)
            return new("Bounds", PreflightStatus.Fail,
                $"{outOfBounds.Count} part(s) outside table: {string.Join(", ", outOfBounds)}.");
        return new("Bounds", PreflightStatus.Pass, "All parts within table bounds.");
    }

    private static PreflightItem CheckLayerAssignments(Project project)
    {
        var layerIds = new HashSet<Guid>(project.Layers.Select(l => l.Id));
        var unassigned = project.Parts
            .Where(p => p.LayerId.HasValue && !layerIds.Contains(p.LayerId.Value))
            .Select(p => project.Files.FirstOrDefault(f => f.Id == p.FileId)?.DisplayName ?? "?")
            .ToList();
        if (unassigned.Count > 0)
            return new("Layer assignments", PreflightStatus.Warn,
                $"{unassigned.Count} part(s) reference deleted layers.");
        return new("Layer assignments", PreflightStatus.Pass, "All layer assignments valid.");
    }

    private static PreflightItem CheckLeadInClearance(Project project, CamSettings settings)
    {
        if (settings.OperationMode == MachineType.Laser || settings.OperationMode == MachineType.VinylKnife)
            return new("Lead-in clearance", PreflightStatus.Pass, "Not applicable for this mode.");

        double leadLen = settings.LeadInLengthMm;
        if (leadLen <= 0)
            return new("Lead-in clearance", PreflightStatus.Pass, "No lead-in configured.");

        // Check that lead-in length is not larger than the smallest part dimension —
        // a rough clearance heuristic (full arc intersection is CamEngine's job).
        int tooSmall = 0;
        foreach (var part in project.Parts)
        {
            var file = project.Files.FirstOrDefault(f => f.Id == part.FileId);
            if (file is null) continue;
            var bb = file.BoundingBox;
            double minDim = Math.Min(bb.Width, bb.Height) * Math.Min(Math.Abs(part.ScaleX), Math.Abs(part.ScaleY));
            if (minDim < leadLen * 2)
                tooSmall++;
        }
        if (tooSmall > 0)
            return new("Lead-in clearance", PreflightStatus.Warn,
                $"{tooSmall} part(s) may be too small for the {leadLen:F1} mm lead-in.");
        return new("Lead-in clearance", PreflightStatus.Pass,
            $"Lead-in {leadLen:F1} mm fits all parts.");
    }

    private static PreflightItem CheckPierceCount(Project project, CamSettings settings)
    {
        bool isPlasma = settings.OperationMode == MachineType.Plasma;
        if (!isPlasma)
            return new("Pierce count", PreflightStatus.Pass, "Not applicable for this mode.");

        // Count closed contours (each needs a pierce).
        int pierces = project.Files
            .Where(f => f.Visible)
            .SelectMany(f => f.Paths)
            .Count(p => p.Polyline.IsClosed);

        if (pierces == 0)
            return new("Pierce count", PreflightStatus.Warn, "No closed contours — nothing to pierce.");
        if (pierces > 200)
            return new("Pierce count", PreflightStatus.Warn,
                $"{pierces} pierces — verify consumable wear before cutting.");
        return new("Pierce count", PreflightStatus.Pass, $"{pierces} pierce(s).");
    }

    private static PreflightItem CheckNoGoZones(Project project, CamSettings settings)
    {
        if (project.NoGoZones.Count == 0)
            return new("No-go zones", PreflightStatus.Pass, "No zones defined.");

        var toolpath = CamEngine.Generate(project, settings);
        var zoneWarnings = toolpath.Warnings
            .Where(w => w.Contains("no-go zone"))
            .ToList();

        if (zoneWarnings.Count > 0)
            return new("No-go zones", PreflightStatus.Fail,
                $"{zoneWarnings.Count} violation(s): {string.Join("; ", zoneWarnings.Take(3))}" +
                (zoneWarnings.Count > 3 ? $" (+{zoneWarnings.Count - 3} more)" : ""));
        return new("No-go zones", PreflightStatus.Pass,
            $"Toolpath clears all {project.NoGoZones.Count} zone(s).");
    }
}
