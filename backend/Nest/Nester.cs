using Backend.Geometry;
using Backend.Models;
using Clipper2Lib;

namespace Backend.Nest;

/// <summary>Result of one nest run (parts are repositioned in place).</summary>
public sealed record NestOutcome(int PlacedCount, int SkippedCount, List<string> Warnings, double UtilizationPct);

/// <summary>
/// Auto-nesting using the greedy core of SVGnest/DeepNest, simplified for v1:
/// parts are placed largest-first at the bottom-left-most feasible position,
/// trying a discrete set of rotations. Feasibility is tested with Clipper2
/// polygon booleans (never hand-rolled): a candidate may not intersect any
/// already-placed part inflated by the spacing, and must stay inside the sheet
/// inset by the margin. Positions are scanned on a coarse grid — full no-fit-
/// polygon packing is a possible later upgrade behind the same endpoint.
/// Drives the exact same Part.X/Y/RotationDeg transform as manual dragging.
/// </summary>
public static class Nester
{
    /// <summary>Coarse scan resolution — finds a feasible slot quickly.</summary>
    private const double GridStepMm = 5;

    /// <summary>Fine gravitation step — tightens the coarse position toward the origin.</summary>
    private const double FineStepMm = 1;

    private const double Eps = 1e-9;

    public static NestOutcome Nest(Project project, NestSettings settings)
    {
        settings.Validate();
        var warnings = new List<string>();
        var table = project.Table;
        double margin = settings.MarginMm, spacing = settings.SpacingMm;
        double innerW = table.WidthMm - 2 * margin;
        double innerH = table.HeightMm - 2 * margin;
        if (innerW <= 0 || innerH <= 0)
        {
            warnings.Add("Margin leaves no usable sheet area — nothing was nested.");
            return new NestOutcome(0, project.Parts.Count, warnings, 0);
        }

        var rotations = RotationCandidates(settings.RotationStepDeg);

        // Largest parts first (classic nesting order), id as deterministic tiebreak.
        var ordered = project.Parts
            .Select(part => (Part: part, File: project.Files.FirstOrDefault(f => f.Id == part.FileId)))
            .Where(x => x.File is not null && x.File.Paths.Count > 0)
            .OrderByDescending(x => LocalArea(x.File!))
            .ThenBy(x => x.Part.Id)
            .ToList();

        int skipped = project.Parts.Count - ordered.Count;

        // Already-placed footprints, inflated by the spacing, plus their bboxes
        // for cheap rejection before any Clipper call.
        var placedInflated = new PathsD();
        var placedBoxes = new List<(double MinX, double MinY, double MaxX, double MaxY)>();

        int placed = 0;
        foreach (var (part, file) in ordered)
        {
            // Best across rotations = lowest bbox Y, then lowest X (bottom-left
            // heuristic). PartX/PartY is the transform that puts the footprint
            // bbox min at (X, Y) — stored here so applying it is a plain copy.
            // Min0 is the footprint's bounding box origin at zero-rotation (for gravitation).
            (double X, double Y, double Rot, double PartX, double PartY, PathsD Poly, PathsD Fp, Point2 Min0, Point2 Max0)? best = null;

            foreach (var rot in rotations)
            {
                var footprint = FootprintAt(part, file!, rot, out var min0, out var max0);
                double w = max0.X - min0.X, h = max0.Y - min0.Y;
                if (w > innerW + Eps || h > innerH + Eps) continue;

                // Bottom-left scan: the first feasible position is the best
                // for this rotation, so stop there.
                bool found = false;
                for (double y = margin; y <= table.HeightMm - margin - h + Eps && !found; y += GridStepMm)
                {
                    // No candidate at or above the current best can win.
                    if (best is not null && y > best.Value.Y + Eps) break;
                    for (double x = margin; x <= table.WidthMm - margin - w + Eps; x += GridStepMm)
                    {
                        if (!Collides(footprint, min0, max0, x, y, placedInflated, placedBoxes))
                        {
                            if (best is null || y < best.Value.Y - Eps ||
                                (Math.Abs(y - best.Value.Y) <= Eps && x < best.Value.X - Eps))
                            {
                                best = (x, y, rot, x - min0.X, y - min0.Y,
                                    Translate(footprint, x - min0.X, y - min0.Y),
                                    footprint, min0, max0);
                            }
                            found = true;
                            break;
                        }
                    }
                }
            }

            if (best is null)
            {
                warnings.Add($"\"{file!.DisplayName}\" did not fit and was left in place.");
                skipped++;
                continue;
            }

            // Gravitation: tighten the coarse grid position toward the origin with
            // a fine 1mm scan — slide down (Y), then left (X). Avoids wasted space
            // from coarse grid granularity without implementing full NFP.
            {
                var bv = best.Value;
                var fp   = bv.Fp;
                var min0 = bv.Min0;
                var max0 = bv.Max0;

                // Slide down
                double tY = bv.Y;
                for (double ty = bv.Y - FineStepMm; ty >= margin - Eps; ty -= FineStepMm)
                {
                    if (!Collides(fp, min0, max0, bv.X, ty, placedInflated, placedBoxes))
                        tY = ty;
                    else break;
                }
                // Slide left
                double tX = bv.X;
                for (double tx = bv.X - FineStepMm; tx >= margin - Eps; tx -= FineStepMm)
                {
                    if (!Collides(fp, min0, max0, tx, tY, placedInflated, placedBoxes))
                        tX = tx;
                    else break;
                }
                var tightPoly = Translate(fp, tX - min0.X, tY - min0.Y);
                best = (tX, tY, bv.Rot, tX - min0.X, tY - min0.Y, tightPoly, fp, min0, max0);
            }

            var b = best.Value;
            // Same Part.X/Y/RotationDeg fields manual dragging writes.
            part.RotationDeg = b.Rot;
            part.X = b.PartX;
            part.Y = b.PartY;

            var inflated = spacing > 0
                ? Clipper.InflatePaths(b.Poly, spacing, JoinType.Round, EndType.Polygon)
                : b.Poly;
            placedInflated.AddRange(inflated);
            placedBoxes.Add(BoundsOf(inflated));
            placed++;
        }

        // Utilization = sum of ALL placed part areas / usable sheet area.
        // We count all parts that weren't skipped (skipped includes parts from
        // before ordered was built, but placed+skipped-startSkipped = placed count here).
        double partAreaSum = ordered.Sum(x => LocalArea(x.File!)) -
                             ordered.Where(x => warnings.Any(w => w.Contains(x.File!.DisplayName))).Sum(x => LocalArea(x.File!));
        double sheetArea = innerW * innerH;
        double utilPct = sheetArea > 0 ? Math.Round(partAreaSum / sheetArea * 100, 1) : 0;

        return new NestOutcome(placed, skipped, warnings, utilPct);
    }

    private static double[] RotationCandidates(int stepDeg)
    {
        if (stepDeg == 0) return [0];
        var list = new List<double>();
        for (int r = 0; r < 360; r += stepDeg) list.Add(r);
        return [.. list];
    }

    /// <summary>Local bbox area — only used to order parts, not for collision.</summary>
    private static double LocalArea(ImportedFile file)
    {
        var identity = new Part { FileId = file.Id };
        var (min, max) = PartTransform.WorldBounds(identity, file);
        return (max.X - min.X) * (max.Y - min.Y);
    }

    /// <summary>
    /// Collision footprint of a part at rotation <paramref name="rotDeg"/> with
    /// translation zero: the union of its closed contours, or the bbox rectangle
    /// when the file has only open paths. Holes are intentionally kept solid —
    /// we don't nest parts inside other parts' cutouts (conservative v1).
    /// </summary>
    private static PathsD FootprintAt(
        Part part, ImportedFile file, double rotDeg, out Point2 min, out Point2 max)
    {
        var probe = new Part { FileId = part.FileId, X = 0, Y = 0, RotationDeg = rotDeg };
        var world = PartTransform.WorldPolylines(probe, file);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var closed = new PathsD();
        foreach (var poly in world)
        {
            foreach (var p in poly.Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            if (poly.IsClosed && poly.Points.Count >= 3)
            {
                var path = new PathD(poly.Points.Count);
                foreach (var p in poly.Points) path.Add(new PointD(p.X, p.Y));
                closed.Add(path);
            }
        }
        min = new Point2(minX, minY);
        max = new Point2(maxX, maxY);

        if (closed.Count == 0)
        {
            // Open-path-only file (e.g. a single engraved line): occupy its bbox.
            return [[new PointD(minX, minY), new PointD(maxX, minY),
                     new PointD(maxX, maxY), new PointD(minX, maxY)]];
        }
        return Clipper.Union(closed, FillRule.NonZero);
    }

    private static bool Collides(
        PathsD footprint, Point2 min0, Point2 max0, double atX, double atY,
        PathsD placedInflated, List<(double MinX, double MinY, double MaxX, double MaxY)> placedBoxes)
    {
        double w = max0.X - min0.X, h = max0.Y - min0.Y;
        bool anyBoxHit = false;
        foreach (var b in placedBoxes)
        {
            if (atX < b.MaxX + Eps && atX + w > b.MinX - Eps &&
                atY < b.MaxY + Eps && atY + h > b.MinY - Eps)
            {
                anyBoxHit = true;
                break;
            }
        }
        if (!anyBoxHit) return false;

        var candidate = Translate(footprint, atX - min0.X, atY - min0.Y);
        var overlap = Clipper.Intersect(candidate, placedInflated, FillRule.NonZero);
        return overlap.Count > 0;
    }

    private static PathsD Translate(PathsD paths, double dx, double dy)
    {
        var result = new PathsD(paths.Count);
        foreach (var path in paths)
        {
            var moved = new PathD(path.Count);
            foreach (var p in path) moved.Add(new PointD(p.x + dx, p.y + dy));
            result.Add(moved);
        }
        return result;
    }

    private static (double, double, double, double) BoundsOf(PathsD paths)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var path in paths)
        {
            foreach (var p in path)
            {
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }
        }
        return (minX, minY, maxX, maxY);
    }
}
