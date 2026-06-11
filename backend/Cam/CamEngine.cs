using Backend.Geometry;
using Backend.Models;

namespace Backend.Cam;

/// <summary>
/// The plasma CAM pipeline: placed parts → world contours → cut-side
/// classification → kerf offsetting → lead-in/out → ordering → neutral
/// toolpath. Controller-agnostic by design; post-processors (Task 4) turn the
/// result into a G-code dialect.
/// </summary>
public static class CamEngine
{
    public static Toolpath Generate(Project project, CamSettings settings)
    {
        var toolpath = new Toolpath();

        // 1. Gather world-space paths from every visible placed part.
        var worldPaths = new List<WorldPath>();
        foreach (var part in project.Parts)
        {
            var file = project.Files.FirstOrDefault(f => f.Id == part.FileId);
            if (file is null || !file.Visible) continue;
            var pivot = PartTransform.LocalCenter(file);
            foreach (var path in file.Paths)
            {
                worldPaths.Add(new WorldPath
                {
                    PartId = part.Id,
                    SourcePathId = path.Id,
                    Layer = path.Layer,
                    Polyline = new Polyline2
                    {
                        IsClosed = path.Polyline.IsClosed,
                        Points = path.Polyline.Points
                            .Select(p => PartTransform.Apply(part, pivot, p))
                            .ToList(),
                    },
                });
            }
        }
        if (worldPaths.Count == 0)
        {
            toolpath.Warnings.Add("Nothing to cut — no visible parts on the table.");
            return toolpath;
        }

        // 2. Cut sides from containment (outer ↔ hole alternation).
        ContourClassifier.Classify(worldPaths);

        bool isLaser = settings.OperationMode == MachineType.Laser;

        // 3. Kerf offset per contour (plasma only).
        //    Laser traces the path as-is — no kerf compensation needed.
        var cutGeometry = new List<(WorldPath Source, Polyline2 Contour)>();
        if (isLaser)
        {
            // Use paths directly — no offset.
            foreach (var wp in worldPaths)
                cutGeometry.Add((wp, wp.Polyline));
        }
        else
        {
            double half = settings.KerfWidthMm / 2;
            foreach (var wp in worldPaths)
            {
                if (wp.Side == CutSide.OnLine)
                {
                    cutGeometry.Add((wp, wp.Polyline));
                    continue;
                }

                double delta = wp.Side == CutSide.Outside ? half : -half;
                var offset = KerfOffsetter.OffsetClosed(wp.Polyline, delta);
                if (offset.Count == 0)
                {
                    toolpath.Warnings.Add(
                        $"A contour (layer '{wp.Layer ?? "-"}') is too small for the {settings.KerfWidthMm}mm kerf and was skipped.");
                    continue;
                }
                foreach (var contour in offset)
                {
                    KerfOffsetter.NormalizeDirection(contour, wp.Side);
                    cutGeometry.Add((wp, contour));
                }
            }
        }

        // 4. Order: inner before outer, then nearest start. Applies to both modes.
        var orderPaths = cutGeometry
            .Select(cg => new WorldPath
            {
                PartId = cg.Source.PartId,
                SourcePathId = cg.Source.SourcePathId,
                Layer = cg.Source.Layer,
                Polyline = cg.Contour,
            })
            .ToList();
        ContourClassifier.Classify(orderPaths); // re-derive tree on final geometry
        var order = CutOrderer.Order(orderPaths, new Point2(0, 0));

        // 5. Leads + assembly.
        //    Laser skips lead-in/out and pierce delay — beam on/off is instant.
        foreach (int idx in order)
        {
            var (source, contour) = cutGeometry[idx];
            List<Point2> points;
            int leadIn = 0, leadOut = 0;

            if (!isLaser && contour.IsClosed)
            {
                (points, leadIn, leadOut) = LeadBuilder.Build(contour, settings);
            }
            else
            {
                points = contour.Points.ToList();
            }

            toolpath.Cuts.Add(new Cut
            {
                PartId = source.PartId,
                SourcePathId = source.SourcePathId,
                Layer = source.Layer,
                Side = source.Side,
                Points = points,
                LeadInPointCount = leadIn,
                LeadOutPointCount = leadOut,
                IsClosedContour = contour.IsClosed,
                PierceDelayS = isLaser ? 0 : settings.PierceDelayS,
                FeedRateMmMin = settings.FeedRateMmMin,
            });
        }

        return toolpath;
    }
}
