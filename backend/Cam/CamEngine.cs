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
                // Flatten Bézier handles to straight-segment polyline for CAM.
                var localFlat = FlattenPath(path);
                worldPaths.Add(new WorldPath
                {
                    PartId = part.Id,
                    SourcePathId = path.Id,
                    Layer = path.Layer,
                    Polyline = new Polyline2
                    {
                        IsClosed = path.Polyline.IsClosed,
                        Points = localFlat
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

        // Override: parts explicitly marked as cutouts always get Inside side.
        var cutoutIds = new HashSet<Guid>(project.Parts.Where(p => p.IsCutout).Select(p => p.Id));
        if (cutoutIds.Count > 0)
            foreach (var wp in worldPaths)
                if (cutoutIds.Contains(wp.PartId) && wp.Polyline.IsClosed)
                    wp.Side = CutSide.Inside;

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

            // Insert holding tabs for plasma closed cuts when the part requests them.
            List<(int Start, int End)> tabSpans = [];
            if (!isLaser && !isVinyl && isClosedContour
                && partById.TryGetValue(source.PartId, out var tabPart)
                && tabPart.TabCount > 0)
            {
                (points, tabSpans) = TabBuilder.Apply(points, tabPart.TabCount, tabPart.TabWidthMm);
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
                IsClosedContour = isClosedContour,
                PierceDelayS = (isLaser || isVinyl) ? 0 : settings.PierceDelayS,
                FeedRateMmMin = ResolveFeed(partLayer, opMode),
                OperationMode = opMode,
                LaserPowerS = isLaser ? ResolveLaserS(partLayer, opMode) : 0,
                TabSpans = tabSpans,
            });
        }

        // 6. Validate no-go zones: warn if any cut or rapid enters a zone.
        if (project.NoGoZones.Count > 0)
            ValidateNoGoZones(toolpath, project.NoGoZones);

        return toolpath;
    }

    private static void ValidateNoGoZones(Toolpath tp, IReadOnlyList<NoGoZone> zones)
    {
        for (int ci = 0; ci < tp.Cuts.Count; ci++)
        {
            var cut = tp.Cuts[ci];
            foreach (var zone in zones)
            {
                // Check every cut segment.
                for (int i = 0; i < cut.Points.Count - 1; i++)
                {
                    if (SegmentIntersectsZone(cut.Points[i], cut.Points[i + 1], zone))
                    {
                        tp.Warnings.Add(
                            $"Cut {ci + 1} crosses no-go zone \"{zone.Label}\" " +
                            $"({zone.X:F0},{zone.Y:F0} {zone.Width:F0}×{zone.Height:F0}mm)");
                        break;
                    }
                }
                // Check rapid from previous cut end to this pierce.
                if (ci > 0)
                {
                    var prev = tp.Cuts[ci - 1].EndsAt;
                    if (SegmentIntersectsZone(prev, cut.PierceAt, zone))
                        tp.Warnings.Add(
                            $"Rapid before cut {ci + 1} crosses no-go zone \"{zone.Label}\"");
                }
            }
        }
    }

    private static bool SegmentIntersectsZone(Point2 a, Point2 b, NoGoZone z)
    {
        // Separating axis test: segment intersects AABB.
        double x0 = z.X, y0 = z.Y, x1 = z.X + z.Width, y1 = z.Y + z.Height;
        if (Math.Min(a.X, b.X) > x1 || Math.Max(a.X, b.X) < x0) return false;
        if (Math.Min(a.Y, b.Y) > y1 || Math.Max(a.Y, b.Y) < y0) return false;
        // AABB overlaps segment bounding box — check if any corner is on segment side or
        // if either endpoint is inside. Simple AABB clamp test suffices for warning.
        return true;
    }

    // ── Bézier flattening helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns a flattened list of local-space points for the path.
    /// If the path has Handles entries for smooth nodes, cubic Bézier segments
    /// are subdivided to 0.1mm chord tolerance. Otherwise the anchor list is
    /// returned directly (no copy).
    /// </summary>
    private static List<Point2> FlattenPath(Backend.Models.PathGeometry path)
    {
        var pts = path.Polyline.Points;
        var handles = path.Handles;
        if (handles == null || handles.All(h => h == null))
            return pts; // no curves — fast path

        int n = pts.Count;
        int segCount = path.Polyline.IsClosed ? n : n - 1;
        var result = new List<Point2> { pts[0] };

        for (int i = 0; i < segCount; i++)
        {
            var p0 = pts[i];
            var p3 = pts[(i + 1) % n];
            double[]? hOut = (i < handles.Count && handles[i] is { Length: >= 4 } h0) ? [h0[2], h0[3]] : null;
            double[]? hIn  = ((i + 1) < handles.Count && handles[(i + 1) % n] is { Length: >= 2 } h1) ? [h1[0], h1[1]] : null;

            if (hOut == null && hIn == null)
            {
                result.Add(p3);
            }
            else
            {
                var cp1 = hOut != null ? new Point2(hOut[0], hOut[1]) : p0;
                var cp2 = hIn  != null ? new Point2(hIn[0],  hIn[1])  : p3;
                SubdivideBezier(result, p0, cp1, cp2, p3, 0.1);
            }
        }
        return result;
    }

    private static void SubdivideBezier(List<Point2> result, Point2 p0, Point2 cp1, Point2 cp2, Point2 p3, double tol)
    {
        // Flatness test using cross-product deviation.
        double d = Math.Abs((cp1.X - p0.X) * (p3.Y - p0.Y) - (cp1.Y - p0.Y) * (p3.X - p0.X))
                 + Math.Abs((cp2.X - p3.X) * (p3.Y - p0.Y) - (cp2.Y - p3.Y) * (p3.X - p0.X));
        if (d <= tol * (p0.DistanceTo(p3) + 1))
        {
            result.Add(p3);
            return;
        }
        // De Casteljau split at t=0.5.
        var m01   = Mid(p0,  cp1);
        var m12   = Mid(cp1, cp2);
        var m23   = Mid(cp2, p3);
        var m012  = Mid(m01, m12);
        var m123  = Mid(m12, m23);
        var m0123 = Mid(m012, m123);
        SubdivideBezier(result, p0,    m01,  m012,  m0123, tol);
        SubdivideBezier(result, m0123, m123, m23,   p3,    tol);
    }

    private static Point2 Mid(Point2 a, Point2 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
}
