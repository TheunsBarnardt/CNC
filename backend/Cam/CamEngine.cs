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

        // Build lookup dictionaries for per-layer operation mode resolution.
        var layerById = project.Layers.ToDictionary(l => l.Id);
        var partById = project.Parts.ToDictionary(p => p.Id);
        Layer? PartLayer(Part p) =>
            p.LayerId.HasValue && layerById.TryGetValue(p.LayerId.Value, out var l) ? l : null;

        // Resolve laser S-word for a cut given its layer and the global cam settings.
        int ResolveLaserS(Layer? layer, LayerOperationMode mode)
        {
            double pct = layer?.LaserPowerPercentOverride ?? settings.LaserPowerPercent;
            pct = mode switch
            {
                LayerOperationMode.Score   => pct * 0.5,
                LayerOperationMode.Engrave => pct * 0.2,
                _                         => pct,
            };
            return (int)Math.Clamp(Math.Round(pct * 10), 0, 1000);
        }

        // Resolve feed rate for a cut given its layer and the global cam settings.
        double ResolveFeed(Layer? layer, LayerOperationMode mode)
        {
            double feed = layer?.FeedRateMmMinOverride ?? settings.FeedRateMmMin;
            // Score is slower to allow better material marking; engrave is faster.
            return mode switch
            {
                LayerOperationMode.Score   => feed * 0.7,
                LayerOperationMode.Engrave => feed * 1.5,
                _                         => feed,
            };
        }

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

        bool isLaser  = settings.OperationMode == MachineType.Laser;
        bool isVinyl  = settings.OperationMode == MachineType.VinylKnife;
        bool skipKerf = isLaser || isVinyl;

        // 3. Kerf offset per contour (plasma only).
        //    Laser and vinyl trace paths as-is — no kerf compensation.
        var cutGeometry = new List<(WorldPath Source, Polyline2 Contour)>();
        if (skipKerf)
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

        // 5. Blade compensation + leads + assembly.
        //    Laser skips lead-in/out and pierce delay — beam on/off is instant.
        //    Vinyl applies drag-knife blade-offset arcs and overcut; no leads.
        foreach (int idx in order)
        {
            var (source, contour) = cutGeometry[idx];
            List<Point2> points;
            int leadIn = 0, leadOut = 0;
            bool isClosedContour = contour.IsClosed;

            if (isVinyl)
            {
                double overcut = contour.IsClosed ? settings.VinylOvercutMm : 0;
                var comp = DragKnifeCompensator.Compensate(
                    contour, settings.VinylBladeOffsetMm, overcut);
                points = comp.Points;
                isClosedContour = false; // compensated path is always open
            }
            else if (!isLaser && contour.IsClosed)
            {
                (points, leadIn, leadOut) = LeadBuilder.Build(contour, settings);
            }
            else
            {
                points = contour.Points.ToList();
            }

            var partLayer = partById.TryGetValue(source.PartId, out var sp) ? PartLayer(sp) : null;
            var opMode = partLayer?.OperationMode ?? LayerOperationMode.Cut;
            toolpath.Cuts.Add(new Cut
            {
                PartId = source.PartId,
                SourcePathId = source.SourcePathId,
                Layer = source.Layer,
                Side = source.Side,
                Points = points,
                LeadInPointCount = leadIn,
                LeadOutPointCount = leadOut,
                IsClosedContour = isClosedContour,
                PierceDelayS = (isLaser || isVinyl) ? 0 : settings.PierceDelayS,
                FeedRateMmMin = ResolveFeed(partLayer, opMode),
                OperationMode = opMode,
                LaserPowerS = isLaser ? ResolveLaserS(partLayer, opMode) : 0,
            });
        }

        return toolpath;
    }
}
