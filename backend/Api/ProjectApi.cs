using Backend.Cam;
using Backend.Import;
using Backend.Import.Bitmap;
using Backend.Models;
using Backend.Nest;
using Backend.Post;
using Backend.Services;
using Clipper2Lib;

namespace Backend.Api;

public static class ProjectApi
{
    public static void MapProjectApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project");

        group.MapGet("/", (ProjectService projects) =>
            Results.Ok(projects.With(ToDto)));

        group.MapPost("/new", (ProjectService projects) =>
        {
            projects.Reset();
            return Results.Ok(projects.With(ToDto));
        });

        group.MapPut("/settings", (UpdateSettingsRequest request, ProjectService projects) =>
        {
            if (request.Table is { } t && (t.WidthMm <= 0 || t.HeightMm <= 0))
                return Results.BadRequest(new { error = "Table width/height must be positive." });
            if (request.Table is { MaterialThicknessMm: < 0 })
                return Results.BadRequest(new { error = "Material thickness cannot be negative." });

            projects.Mutate(p =>
            {
                if (!string.IsNullOrWhiteSpace(request.Name)) p.Name = request.Name.Trim();
                if (request.Units is { } u) p.Units = u;
                if (request.Table is { } table)
                {
                    p.Table.WidthMm = table.WidthMm;
                    p.Table.HeightMm = table.HeightMm;
                    p.Table.Origin = table.Origin;
                    p.Table.MaterialThicknessMm = table.MaterialThicknessMm;
                }
            });
            return Results.Ok(projects.With(ToDto));
        });

        // Import one or more SVG/DXF files (multipart/form-data).
        group.MapPost("/files", async (HttpRequest request, FileImportService import, ProjectService projects) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data with files." });

            var form = await request.ReadFormAsync();
            if (form.Files.Count == 0)
                return Results.BadRequest(new { error = "No files in request." });

            var results = new List<ImportResultDto>();
            foreach (var upload in form.Files)
            {
                try
                {
                    await using var stream = upload.OpenReadStream();
                    var imported = import.Import(stream, upload.FileName);
                    // Each import lands on the table as a placed part right away.
                    projects.Mutate(p =>
                    {
                        p.Files.Add(imported);
                        p.Parts.Add(PartPlacer.PlaceNew(p, imported));
                    });
                    results.Add(new ImportResultDto(upload.FileName, true, null, ToFileDto(imported)));
                }
                catch (Exception ex) when (ex is FormatException or NotSupportedException)
                {
                    results.Add(new ImportResultDto(upload.FileName, false, ex.Message, null));
                }
            }

            return Results.Ok(new { results, project = projects.With(ToDto) });
        })
        .DisableAntiforgery();

        // Create a synthetic file from raw paths (shapes drawn in-app, text converted to paths).
        group.MapPost("/files/synthetic", (SyntheticFileRequest request, ProjectService projects) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest(new { error = "displayName is required." });
            if (request.Paths is not { Count: > 0 })
                return Results.BadRequest(new { error = "At least one path is required." });

            var file = new ImportedFile
            {
                FileName = $"{request.DisplayName}.shape",
                DisplayName = request.DisplayName.Trim(),
                Kind = ImportedFileKind.Shape,
            };

            foreach (var p in request.Paths)
            {
                if (p.Points is not { Count: >= 2 }) continue;
                var polyline = new Backend.Geometry.Polyline2
                {
                    Points = p.Points.Select(pt => new Backend.Geometry.Point2(pt[0], pt[1])).ToList(),
                    IsClosed = p.Closed,
                };
                file.Paths.Add(new PathGeometry { Layer = p.Layer, Polyline = polyline });
            }

            if (file.Paths.Count == 0)
                return Results.BadRequest(new { error = "No valid paths (each needs ≥2 points)." });

            projects.Mutate(p =>
            {
                p.Files.Add(file);
                var part = PartPlacer.PlaceNew(p, file);
                // Honor caller's requested placement (user drew the shape at a specific position).
                if (request.InitialX.HasValue) part.X = request.InitialX.Value;
                if (request.InitialY.HasValue) part.Y = request.InitialY.Value;
                p.Parts.Add(part);
            });

            return Results.Ok(projects.With(ToDto));
        });

        group.MapPatch("/files/{id:guid}", (Guid id, UpdateFileRequest request, ProjectService projects) =>
        {
            bool found = projects.With(p =>
            {
                var file = p.Files.FirstOrDefault(f => f.Id == id);
                if (file is null) return false;
                if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    file.DisplayName = request.DisplayName.Trim();
                if (request.Visible is { } v) file.Visible = v;
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        group.MapDelete("/files/{id:guid}", (Guid id, ProjectService projects) =>
        {
            bool removed = projects.With(p =>
            {
                if (p.Files.RemoveAll(f => f.Id == id) == 0) return false;
                p.Parts.RemoveAll(part => part.FileId == id); // parts can't outlive their file
                return true;
            });
            return removed ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        // Raw local geometry for every file — the viewport renders from this.
        // Points are [x, y] pairs rounded to 0.01mm to keep payloads lean.
        // Path IDs are included so the node-edit UI can target specific paths.
        group.MapGet("/geometry", (ProjectService projects) =>
            Results.Ok(projects.With(p => new
            {
                files = p.Files.Select(f => new
                {
                    fileId = f.Id,
                    paths = f.Paths.Select(path => new
                    {
                        id = path.Id,
                        layer = path.Layer,
                        closed = path.Polyline.IsClosed,
                        points = path.Polyline.Points
                            .Select(pt => new[] { Math.Round(pt.X, 2), Math.Round(pt.Y, 2) })
                            .ToList(),
                    }).ToList(),
                }).ToList(),
            })));

        // Replace the node list of a specific path (node editing).
        group.MapPatch("/files/{fileId:guid}/paths/{pathId:guid}", (Guid fileId, Guid pathId, UpdatePathNodesRequest request, ProjectService projects) =>
        {
            if (request.Points is not { Count: >= 2 })
                return Results.BadRequest(new { error = "Path must have at least 2 points." });

            bool found = projects.With(p =>
            {
                var file = p.Files.FirstOrDefault(f => f.Id == fileId);
                var path = file?.Paths.FirstOrDefault(pt => pt.Id == pathId);
                if (path is null) return false;
                if (path.Polyline.IsClosed && request.Points.Count < 3)
                    return false; // closed path needs ≥3 points
                path.Polyline.Points.Clear();
                path.Polyline.Points.AddRange(request.Points.Select(pt => new Backend.Geometry.Point2(pt[0], pt[1])));
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        // Simplify all paths in a file using Ramer-Douglas-Peucker.
        group.MapPost("/files/{fileId:guid}/simplify", (Guid fileId, SimplifyRequest request, ProjectService projects) =>
        {
            double tol = Math.Max(0.01, request.ToleranceMm);
            bool found = projects.With(p =>
            {
                var file = p.Files.FirstOrDefault(f => f.Id == fileId);
                if (file is null) return false;
                foreach (var path in file.Paths)
                {
                    var simplified = PathSimplifier.Simplify(path.Polyline.Points, path.Polyline.IsClosed, tol);
                    path.Polyline.Points.Clear();
                    path.Polyline.Points.AddRange(simplified);
                }
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        // Pathfinder boolean operation on two or more placed parts.
        // All part geometries are transformed to world space, the boolean is applied,
        // and the result is stored as a new synthetic file at world position.
        group.MapPost("/parts/boolean", (BooleanRequest request, ProjectService projects) =>
        {
            if (request.PartIds is not { Count: >= 2 })
                return Results.BadRequest(new { error = "Boolean needs at least 2 parts." });
            var op = request.Operation?.ToLowerInvariant();
            if (op is not ("unite" or "subtract" or "intersect"))
                return Results.BadRequest(new { error = "operation must be 'unite', 'subtract', or 'intersect'." });

            return projects.With<IResult>(p =>
            {
                // Collect parts in request order.
                var parts = request.PartIds
                    .Select(id => p.Parts.FirstOrDefault(x => x.Id == id))
                    .ToList();
                if (parts.Any(x => x is null)) return Results.NotFound();

                // Build world-space closed paths for each part.
                // Only closed contours participate in boolean ops.
                var subjectPaths = new PathsD();
                var clipPaths = new PathsD();

                void AddWorldPaths(PathsD dest, Part part)
                {
                    var file = p.Files.FirstOrDefault(f => f.Id == part!.FileId);
                    if (file is null) return;
                    var localPts = file.Paths.Where(ph => ph.Polyline.IsClosed);
                    double rad = part!.RotationDeg * Math.PI / 180;
                    double cos = Math.Cos(rad), sin = Math.Sin(rad);
                    double sx = part.ScaleX, sy = part.ScaleY;

                    // Pivot = local bbox center (same as partToWorld in the frontend).
                    double minX = double.MaxValue, minY = double.MaxValue,
                           maxX = double.MinValue, maxY = double.MinValue;
                    foreach (var ph in file.Paths)
                        foreach (var pt in ph.Polyline.Points)
                        {
                            if (pt.X < minX) minX = pt.X; if (pt.Y < minY) minY = pt.Y;
                            if (pt.X > maxX) maxX = pt.X; if (pt.Y > maxY) maxY = pt.Y;
                        }
                    double pivX = (minX + maxX) / 2, pivY = (minY + maxY) / 2;

                    foreach (var ph in localPts)
                    {
                        var pd = new PathD(ph.Polyline.Points.Count);
                        foreach (var lp in ph.Polyline.Points)
                        {
                            double dx = (lp.X - pivX) * sx, dy = (lp.Y - pivY) * sy;
                            double wx = part.X + pivX + dx * cos - dy * sin;
                            double wy = part.Y + pivY + dx * sin + dy * cos;
                            pd.Add(new PointD(wx, wy));
                        }
                        dest.Add(pd);
                    }
                }

                AddWorldPaths(subjectPaths, parts[0]!);
                for (int i = 1; i < parts.Count; i++) AddWorldPaths(clipPaths, parts[i]!);

                if (subjectPaths.Count == 0)
                    return Results.BadRequest(new { error = "Subject part has no closed paths." });

                PathsD result = op switch
                {
                    "unite"     => Clipper.BooleanOp(ClipType.Union,    subjectPaths, clipPaths, FillRule.NonZero),
                    "subtract"  => Clipper.BooleanOp(ClipType.Difference,  subjectPaths, clipPaths, FillRule.NonZero),
                    _           => Clipper.BooleanOp(ClipType.Intersection, subjectPaths, clipPaths, FillRule.NonZero),
                };

                if (result.Count == 0)
                    return Results.BadRequest(new { error = "Boolean produced no geometry." });

                // Store result as world-space geometry at origin (Part.X=Y=0).
                var newFile = new ImportedFile
                {
                    FileName = $"{op}.shape",
                    DisplayName = $"{op} result",
                    Kind = ImportedFileKind.Shape,
                };
                foreach (var r in result)
                {
                    if (r.Count < 3) continue;
                    newFile.Paths.Add(new PathGeometry
                    {
                        Polyline = new Backend.Geometry.Polyline2
                        {
                            IsClosed = true,
                            Points = r.Select(pt => new Backend.Geometry.Point2(pt.x, pt.y)).ToList(),
                        },
                    });
                }
                if (newFile.Paths.Count == 0)
                    return Results.BadRequest(new { error = "Boolean produced no geometry." });

                projects.Mutate(mp =>
                {
                    mp.Files.Add(newFile);
                    mp.Parts.Add(new Part { FileId = newFile.Id, X = 0, Y = 0 });
                    // Optionally remove source parts (user asked for pathfinder, so remove them).
                    if (request.DeleteSources)
                    {
                        foreach (var partId in request.PartIds)
                        {
                            mp.Parts.RemoveAll(x => x.Id == partId);
                        }
                    }
                });
                return Results.Ok(projects.With(ToDto));
            });
        });

        // --- parts (placement instances) ---------------------------------

        group.MapPost("/parts", (CreatePartRequest request, ProjectService projects) =>
        {
            var dto = projects.With<ProjectDto?>(p =>
            {
                var file = p.Files.FirstOrDefault(f => f.Id == request.FileId);
                if (file is null) return null;
                p.Parts.Add(PartPlacer.PlaceNew(p, file));
                return ToDto(p);
            });
            return dto is null
                ? Results.NotFound(new { error = "No such file." })
                : Results.Ok(dto);
        });

        group.MapPatch("/parts/{id:guid}", (Guid id, UpdatePartRequest request, ProjectService projects) =>
        {
            bool found = projects.With(p =>
            {
                var part = p.Parts.FirstOrDefault(x => x.Id == id);
                if (part is null) return false;
                if (request.X is { } x) part.X = x;
                if (request.Y is { } y) part.Y = y;
                if (request.RotationDeg is { } r) part.RotationDeg = NormalizeDeg(r);
                if (request.ScaleX is { } sx) part.ScaleX = sx;
                if (request.ScaleY is { } sy) part.ScaleY = sy;
                if (request.LayerId is { } lid) part.LayerId = lid;
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        // Reorder part in the stacking order (affects render and CAM cut order).
        group.MapPost("/parts/{id:guid}/reorder", (Guid id, ReorderPartRequest request, ProjectService projects) =>
        {
            bool found = projects.With(p =>
            {
                int idx = p.Parts.FindIndex(x => x.Id == id);
                if (idx < 0) return false;
                int target = request.Direction switch
                {
                    "up"    => Math.Min(p.Parts.Count - 1, idx + 1),
                    "down"  => Math.Max(0, idx - 1),
                    "front" => p.Parts.Count - 1,
                    "back"  => 0,
                    _ => idx,
                };
                if (target == idx) return true;
                var part = p.Parts[idx];
                p.Parts.RemoveAt(idx);
                p.Parts.Insert(target, part);
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        // Contour offset: inflate/deflate the selected part's geometry by offsetMm
        // (positive = outward, negative = inward) and create a new part from the result.
        group.MapPost("/parts/{id:guid}/offset", (Guid id, OffsetPartRequest request, ProjectService projects) =>
        {
            if (request.OffsetMm == 0)
                return Results.BadRequest(new { error = "offsetMm must be non-zero." });

            return projects.With<IResult>(p =>
            {
                var source = p.Parts.FirstOrDefault(x => x.Id == id);
                if (source is null) return Results.NotFound();
                var file = p.Files.FirstOrDefault(f => f.Id == source.FileId);
                if (file is null) return Results.NotFound();

                var newFile = new ImportedFile
                {
                    FileName = $"{file.DisplayName} offset.shape",
                    DisplayName = $"{file.DisplayName} +{request.OffsetMm:0.##}mm",
                    Kind = ImportedFileKind.Shape,
                };

                foreach (var path in file.Paths)
                {
                    if (path.Polyline.IsClosed)
                    {
                        var results = KerfOffsetter.OffsetClosed(path.Polyline, request.OffsetMm);
                        foreach (var r in results)
                            newFile.Paths.Add(new PathGeometry { Layer = path.Layer, Polyline = r });
                    }
                    else
                    {
                        // Open path: use open-end offsetting (creates a closed buffer around the line).
                        var pts = new PathD(path.Polyline.Points.Select(pt => new PointD(pt.X, pt.Y)));
                        var sol = Clipper.InflatePaths([pts], request.OffsetMm,
                            JoinType.Round, EndType.Round, precision: 4);
                        foreach (var s in sol)
                        {
                            if (s.Count < 3) continue;
                            newFile.Paths.Add(new PathGeometry
                            {
                                Layer = path.Layer,
                                Polyline = new Backend.Geometry.Polyline2
                                {
                                    IsClosed = true,
                                    Points = s.Select(pt => new Backend.Geometry.Point2(pt.x, pt.y)).ToList(),
                                },
                            });
                        }
                    }
                }

                if (newFile.Paths.Count == 0)
                    return Results.BadRequest(new { error = "Offset produced no geometry (shape may be too small for this offset)." });

                projects.Mutate(p =>
                {
                    p.Files.Add(newFile);
                    var newPart = new Part
                    {
                        FileId = newFile.Id,
                        X = source.X,
                        Y = source.Y,
                        RotationDeg = source.RotationDeg,
                        ScaleX = source.ScaleX,
                        ScaleY = source.ScaleY,
                    };
                    p.Parts.Add(newPart);
                });
                return Results.Ok(projects.With(ToDto));
            });
        });

        // Grid or circular array: create copies of an existing part at computed positions.
        group.MapPost("/parts/{id:guid}/array", (Guid id, ArrayRequest request, ProjectService projects) =>
        {
            return projects.With<IResult>(p =>
            {
                var source = p.Parts.FirstOrDefault(x => x.Id == id);
                if (source is null) return Results.NotFound();
                var file = p.Files.FirstOrDefault(f => f.Id == source.FileId);
                if (file is null) return Results.NotFound();

                var newParts = new List<Part>();
                if (request.Type == "grid")
                {
                    if (request.Rows < 1 || request.Cols < 1)
                        return Results.BadRequest(new { error = "Rows and cols must be ≥1." });

                    var (_, bMax) = PartTransform.WorldBounds(source, file);
                    var (bMin, _) = PartTransform.WorldBounds(source, file);
                    double partW = bMax.X - bMin.X;
                    double partH = bMax.Y - bMin.Y;
                    double stepX = partW + request.SpacingXMm;
                    double stepY = partH + request.SpacingYMm;

                    for (int row = 0; row < request.Rows; row++)
                    for (int col = 0; col < request.Cols; col++)
                    {
                        if (row == 0 && col == 0) continue; // keep original in place
                        newParts.Add(new Part
                        {
                            FileId = source.FileId,
                            X = source.X + col * stepX,
                            Y = source.Y + row * stepY,
                            RotationDeg = source.RotationDeg,
                            ScaleX = source.ScaleX,
                            ScaleY = source.ScaleY,
                        });
                    }
                }
                else if (request.Type == "circular")
                {
                    if (request.Count < 2)
                        return Results.BadRequest(new { error = "Circular array needs count ≥2." });

                    // The circle is centered on the original part's world bbox center.
                    var (bMin2, bMax2) = PartTransform.WorldBounds(source, file);
                    double cx = (bMin2.X + bMax2.X) / 2;
                    double cy = (bMin2.Y + bMax2.Y) / 2;

                    for (int k = 1; k < request.Count; k++)
                    {
                        double angleDeg = request.StartAngleDeg + k * (360.0 / request.Count);
                        double angleRad = angleDeg * Math.PI / 180.0;
                        double rot = request.RotateWithArray
                            ? NormalizeDeg(source.RotationDeg + angleDeg)
                            : source.RotationDeg;

                        // New center position = circle center + radius * direction.
                        // Convert back: part.X = worldCenter.X - partLocalCenter.X (before part transform).
                        var (bMin3, bMax3) = PartTransform.WorldBounds(source, file);
                        double srcCx = (bMin3.X + bMax3.X) / 2 - source.X;
                        double srcCy = (bMin3.Y + bMax3.Y) / 2 - source.Y;

                        newParts.Add(new Part
                        {
                            FileId = source.FileId,
                            X = cx + request.RadiusMm * Math.Cos(angleRad) - srcCx,
                            Y = cy + request.RadiusMm * Math.Sin(angleRad) - srcCy,
                            RotationDeg = rot,
                            ScaleX = source.ScaleX,
                            ScaleY = source.ScaleY,
                        });
                    }
                }
                else
                {
                    return Results.BadRequest(new { error = "type must be 'grid' or 'circular'." });
                }

                projects.Mutate(mp => mp.Parts.AddRange(newParts));
                return Results.Ok(projects.With(ToDto));
            });
        });

        // Material test array: generate a grid of test cuts varying two CAM parameters.
        // Returns G-code directly as a file download.
        group.MapPost("/parts/{id:guid}/test-array", (Guid id, TestArrayRequest request, ProjectService projects, PostProcessorRegistry posts) =>
        {
            return projects.With<IResult>(p =>
            {
                var source = p.Parts.FirstOrDefault(x => x.Id == id);
                if (source is null) return Results.NotFound();
                var file = p.Files.FirstOrDefault(f => f.Id == source.FileId);
                if (file is null) return Results.NotFound();

                if (request.Rows < 1 || request.Cols < 1 || request.Rows > 20 || request.Cols > 20)
                    return Results.BadRequest(new { error = "Rows/cols must be 1–20." });
                if (request.SpacingMm < 1)
                    return Results.BadRequest(new { error = "Spacing must be ≥1mm." });

                var (bMin, bMax) = PartTransform.WorldBounds(source, file);
                double partW = bMax.X - bMin.X;
                double partH = bMax.Y - bMin.Y;
                double stepX = partW + request.SpacingMm;
                double stepY = partH + request.SpacingMm;

                double LerpVal(TestParamRange r, int steps, int i) =>
                    steps == 1 ? r.Min : r.Min + (r.Max - r.Min) * i / (steps - 1);

                // Pick post-processor matching the project's operation mode.
                var post = p.Cam.OperationMode switch
                {
                    MachineType.Laser      => posts.Find("grbl-laser") ?? posts.Default,
                    MachineType.VinylKnife => posts.Find("grbl-vinyl") ?? posts.Default,
                    _                      => posts.Default,
                };

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"; Material test array {request.Rows}×{request.Cols} — DIY GRBL Cutting CAM");
                sb.AppendLine($"; Rows: {request.Rows} × {request.Param1.Type} ({request.Param1.Min}–{request.Param1.Max})");
                sb.AppendLine($"; Cols: {request.Cols} × {request.Param2.Type} ({request.Param2.Min}–{request.Param2.Max})");

                bool firstCell = true;
                for (int row = 0; row < request.Rows; row++)
                {
                    double v1 = LerpVal(request.Param1, request.Rows, row);
                    for (int col = 0; col < request.Cols; col++)
                    {
                        double v2 = LerpVal(request.Param2, request.Cols, col);

                        sb.AppendLine();
                        sb.AppendLine($"; [{row},{col}] {request.Param1.Type}={v1:0.###} {request.Param2.Type}={v2:0.###}");

                        // Build a mini-project with this one part positioned at the grid cell.
                        double offX = source.X + col * stepX - bMin.X;
                        double offY = source.Y + row * stepY - bMin.Y;

                        var cellPart = new Part
                        {
                            FileId = source.FileId,
                            X = offX,
                            Y = offY,
                            RotationDeg = source.RotationDeg,
                            ScaleX = source.ScaleX,
                            ScaleY = source.ScaleY,
                        };
                        var mini = new Project { Table = p.Table };
                        mini.Files.Add(file);
                        mini.Parts.Add(cellPart);

                        // Build settings with test parameters applied.
                        var cam = new CamSettings
                        {
                            OperationMode   = p.Cam.OperationMode,
                            FeedRateMmMin   = ApplyParam(p.Cam.FeedRateMmMin, "feed",   request.Param1, v1, request.Param2, v2),
                            KerfWidthMm     = p.Cam.KerfWidthMm,
                            PierceDelayS    = ApplyParam(p.Cam.PierceDelayS,   "pierce", request.Param1, v1, request.Param2, v2),
                            CutHeightMm     = p.Cam.CutHeightMm,
                            PierceHeightMm  = p.Cam.PierceHeightMm,
                            LeadInType      = p.Cam.LeadInType,
                            LeadInLengthMm  = p.Cam.LeadInLengthMm,
                            LeadOutType     = p.Cam.LeadOutType,
                            LeadOutLengthMm = p.Cam.LeadOutLengthMm,
                            LaserPowerPercent = ApplyParam(p.Cam.LaserPowerPercent, "power", request.Param1, v1, request.Param2, v2),
                            VinylBladeOffsetMm = p.Cam.VinylBladeOffsetMm,
                            VinylOvercutMm   = p.Cam.VinylOvercutMm,
                            VinylKnifeUpMm   = p.Cam.VinylKnifeUpMm,
                            VinylKnifeDownMm = p.Cam.VinylKnifeDownMm,
                        };

                        mini.Cam = cam;
                        var toolpath = CamEngine.Generate(mini, cam);
                        var program = post.Generate(toolpath, mini);

                        // Emit lines: skip G21/G90/M2 preamble/end on all but first/last cell.
                        var lines = program.Lines;
                        for (int li = 0; li < lines.Count; li++)
                        {
                            var line = lines[li];
                            // Strip global preamble from all cells except first.
                            if (!firstCell && (line.StartsWith("G21") || line.StartsWith("G90") ||
                                line.StartsWith("G17") || line.StartsWith("G94")))
                                continue;
                            // Strip end commands — we append them once at the end.
                            if (line.StartsWith("G0 X0 Y0") || line == "M2") continue;
                            sb.AppendLine(line);
                        }
                        firstCell = false;
                    }
                }

                // Final park + end.
                var origin = GrblPlasmaPostProcessor.OriginPoint(p.Table);
                sb.AppendLine($"G0 X{origin.X:0.###} Y{origin.Y:0.###}");
                sb.AppendLine("M2");

                string name = SafeFileName(p.Name);
                return Results.File(
                    System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                    "text/plain",
                    $"{name}-test-array.nc");
            });
        });

        group.MapPost("/parts/{id:guid}/duplicate", (Guid id, ProjectService projects) =>
        {
            var dto = projects.With<ProjectDto?>(p =>
            {
                var source = p.Parts.FirstOrDefault(x => x.Id == id);
                if (source is null) return null;
                p.Parts.Add(new Part
                {
                    FileId = source.FileId,
                    X = source.X + 15,
                    Y = source.Y + 15,
                    RotationDeg = source.RotationDeg,
                    ScaleX = source.ScaleX,
                    ScaleY = source.ScaleY,
                    LayerId = source.LayerId,
                });
                return ToDto(p);
            });
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        // --- layers -----------------------------------------------------------

        group.MapPost("/layers", (CreateLayerRequest request, ProjectService projects) =>
        {
            projects.Mutate(p => p.Layers.Add(new Layer
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? $"Layer {p.Layers.Count + 1}" : request.Name.Trim(),
                Color = request.Color ?? "#3b82f6",
            }));
            return Results.Ok(projects.With(ToDto));
        });

        group.MapPatch("/layers/{id:guid}", (Guid id, UpdateLayerRequest request, ProjectService projects) =>
        {
            bool found = projects.With(p =>
            {
                var layer = p.Layers.FirstOrDefault(l => l.Id == id);
                if (layer is null) return false;
                if (request.Name is { Length: > 0 } n) layer.Name = n.Trim();
                if (request.Color is { Length: > 0 } c) layer.Color = c;
                if (request.Visible is { } v) layer.Visible = v;
                if (request.Locked is { } lk) layer.Locked = lk;
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        group.MapDelete("/layers/{id:guid}", (Guid id, ProjectService projects) =>
        {
            bool found = projects.With(p =>
            {
                var layer = p.Layers.FirstOrDefault(l => l.Id == id);
                if (layer is null) return false;
                // Can't delete the last layer.
                if (p.Layers.Count == 1) return false;
                // Reassign parts on this layer to the first remaining layer.
                var fallback = p.Layers.First(l => l.Id != id);
                foreach (var part in p.Parts.Where(x => x.LayerId == id))
                    part.LayerId = fallback.Id;
                p.Layers.Remove(layer);
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.BadRequest(new { error = "Cannot delete the only layer." });
        });

        group.MapDelete("/parts/{id:guid}", (Guid id, ProjectService projects) =>
        {
            bool removed = projects.With(p => p.Parts.RemoveAll(x => x.Id == id) > 0);
            return removed ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        // Auto-nest: reposition all parts to save material. Same transform
        // fields as manual dragging — users can adjust the result by hand.
        group.MapPost("/nest", (NestRequest? request, ProjectService projects) =>
        {
            var settings = new NestSettings
            {
                MarginMm = request?.MarginMm ?? 10,
                SpacingMm = request?.SpacingMm ?? 5,
                RotationStepDeg = request?.RotationStepDeg ?? 90,
            };
            try
            {
                settings.Validate();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return Results.Ok(projects.With(p =>
            {
                var outcome = Nester.Nest(p, settings);
                return new NestResultDto(
                    ToDto(p), outcome.PlacedCount, outcome.SkippedCount, outcome.Warnings);
            }));
        });

        // --- CAM ----------------------------------------------------------

        // Persisted CAM settings (kerf, feeds, leads). Saved in the project file.
        group.MapPut("/cam", (CamSettings settings, ProjectService projects) =>
        {
            if (settings.KerfWidthMm < 0 || settings.FeedRateMmMin <= 0 ||
                settings.PierceDelayS < 0 || settings.LeadInLengthMm < 0 || settings.LeadOutLengthMm < 0)
                return Results.BadRequest(new { error = "CAM settings out of range." });
            projects.Mutate(p => p.Cam = settings);
            return Results.Ok(projects.With(p => p.Cam));
        });

        group.MapGet("/cam", (ProjectService projects) =>
            Results.Ok(projects.With(p => p.Cam)));

        // Generate the neutral toolpath from the current placement + settings.
        group.MapPost("/toolpath", (ProjectService projects) =>
        {
            var toolpath = projects.With(p => CamEngine.Generate(p, p.Cam));
            return Results.Ok(ToToolpathDto(toolpath));
        });

        // --- post-processing (G-code) --------------------------------------

        // Available post-processors (pluggable; laser/vinyl posts join later).
        app.MapGet("/api/posts", (PostProcessorRegistry posts) =>
            Results.Ok(posts.All.Select(p => new PostProcessorDto(
                p.Id, p.DisplayName, p.Description, p.FileExtension,
                p.Id == posts.Default.Id))));

        // Toolpath → G-code via the chosen (or default) post-processor.
        group.MapPost("/gcode", (GcodeRequest? request, PostProcessorRegistry posts, ProjectService projects) =>
        {
            IPostProcessor? post = request?.PostId is { Length: > 0 } id
                ? posts.Find(id)
                : posts.Default;
            if (post is null)
                return Results.BadRequest(new { error = $"Unknown post-processor '{request!.PostId}'." });

            return Results.Ok(projects.With(p =>
            {
                var toolpath = CamEngine.Generate(p, p.Cam);
                var program = post.Generate(toolpath, p);
                // Cache lines so /api/machine/run can stream without re-generating.
                projects.CacheGcode(program.Lines);
                // Rapid stats measured from the work origin — that's where the
                // generated program starts and parks.
                var origin = GrblPlasmaPostProcessor.OriginPoint(p.Table);
                return new GcodeResultDto(
                    post.Id,
                    $"{SafeFileName(p.Name)}{post.FileExtension}",
                    program.ToText(),
                    program.Lines.Count,
                    toolpath.Cuts.Count,
                    Math.Round(toolpath.TotalCutLengthMm, 2),
                    Math.Round(toolpath.TotalRapidLengthMm(origin), 2),
                    program.Warnings);
            }));
        });

        // Save: the whole project (settings + geometry) as one JSON file.
        group.MapGet("/export", (ProjectService projects) =>
        {
            string json = projects.ExportJson();
            string name = projects.With(p => p.Name);
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(json),
                "application/json",
                $"{SafeFileName(name)}.grblcam.json");
        });

        // Load: replace the current project from an uploaded project file.
        group.MapPost("/load", async (HttpRequest request, ProjectService projects) =>
        {
            try
            {
                if (request.HasFormContentType)
                {
                    var form = await request.ReadFormAsync();
                    var upload = form.Files.FirstOrDefault();
                    if (upload is null)
                        return Results.BadRequest(new { error = "No project file in request." });
                    await using var stream = upload.OpenReadStream();
                    projects.LoadJson(stream);
                }
                else
                {
                    projects.LoadJson(request.Body);
                }
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            return Results.Ok(projects.With(ToDto));
        })
        .DisableAntiforgery();

        // Serve the original raster image for a bitmap-traced file.
        group.MapGet("/files/{id:guid}/bitmap-image", (Guid id, ProjectService projects) =>
        {
            var result = projects.With<(byte[]? data, string? mime)>(p =>
            {
                var f = p.Files.FirstOrDefault(x => x.Id == id);
                return (f?.BitmapData, f?.BitmapMimeType);
            });
            if (result.data is null)
                return Results.NotFound();
            return Results.File(result.data, result.mime ?? "image/png");
        });

        // Re-trace a bitmap file with new settings.
        group.MapPost("/files/{id:guid}/retrace", (Guid id, RetraceRequest request, ProjectService projects) =>
        {
            return projects.With<IResult>(p =>
            {
                var file = p.Files.FirstOrDefault(f => f.Id == id);
                if (file is null || file.BitmapData is null)
                    return Results.NotFound();

                var settings = new BitmapTraceSettings(
                    request.ThresholdPercent,
                    request.Invert,
                    request.Brightness,
                    request.Contrast,
                    request.Mode == "centerline" ? TraceMode.Centerline : TraceMode.Outline,
                    request.SimplifyToleranceMm
                );

                var (newPaths, _, _) = BitmapTracer.Trace(file.BitmapData, settings);

                projects.Mutate(mp =>
                {
                    var mf = mp.Files.First(f => f.Id == id);
                    mf.Paths.Clear();
                    foreach (var poly in newPaths)
                        mf.Paths.Add(new PathGeometry { Polyline = poly });
                    mf.Warnings.Clear();
                    if (mf.Paths.Count == 0)
                        mf.Warnings.Add("No foreground found — try adjusting the threshold.");
                    mf.BitmapTraceSettingsJson =
                        System.Text.Json.JsonSerializer.Serialize(settings);
                });

                return Results.Ok(projects.With(ToDto));
            });
        });
    }

    private static string SafeFileName(string name) // placed after bitmap section
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "project" : cleaned;
    }

    // --- DTOs ---------------------------------------------------------------
    // The frontend gets summaries, not raw point lists — geometry stays on the
    // backend until the viewport (Task 2) needs it.

    public sealed record TableSettingsDto(
        double WidthMm, double HeightMm, TableOrigin Origin, double MaterialThicknessMm);

    public sealed record FileSummaryDto(
        Guid Id,
        string FileName,
        string DisplayName,
        ImportedFileKind Kind,
        bool Visible,
        int PathCount,
        int ClosedPathCount,
        int OpenPathCount,
        int TotalPoints,
        double WidthMm,
        double HeightMm,
        IReadOnlyList<string> Layers,
        IReadOnlyList<string> Warnings,
        bool HasBitmap,
        string? BitmapTraceSettingsJson);

    public sealed record LayerDto(Guid Id, string Name, string Color, bool Visible, bool Locked);
    public sealed record PartDto(Guid Id, Guid FileId, double X, double Y, double RotationDeg, double ScaleX, double ScaleY, Guid? LayerId);

    public sealed record ProjectDto(
        string Name,
        Units Units,
        TableSettingsDto Table,
        IReadOnlyList<FileSummaryDto> Files,
        IReadOnlyList<PartDto> Parts,
        IReadOnlyList<LayerDto> Layers);

    public sealed record ImportResultDto(
        string FileName, bool Ok, string? Error, FileSummaryDto? File);

    public sealed record UpdateSettingsRequest(string? Name, Units? Units, TableSettingsDto? Table);

    public sealed record UpdateFileRequest(string? DisplayName, bool? Visible);

    public sealed record CreatePartRequest(Guid FileId);

    public sealed record UpdatePartRequest(double? X, double? Y, double? RotationDeg, double? ScaleX, double? ScaleY, Guid? LayerId);
    public sealed record ReorderPartRequest(string Direction); // "up" | "down" | "front" | "back"
    public sealed record OffsetPartRequest(double OffsetMm);

    public sealed record UpdatePathNodesRequest(IReadOnlyList<double[]> Points);
    public sealed record SimplifyRequest(double ToleranceMm);
    public sealed record BooleanRequest(string Operation, IReadOnlyList<Guid> PartIds, bool DeleteSources = true);

    public sealed record ArrayRequest(
        string Type,           // "grid" | "circular"
        // grid:
        int Rows = 2, int Cols = 2,
        double SpacingXMm = 10, double SpacingYMm = 10,
        // circular:
        int Count = 6, double RadiusMm = 50,
        double StartAngleDeg = 0, bool RotateWithArray = false);

    public sealed record TestParamRange(
        string Type,    // "feed" | "pierce" | "power"
        double Min, double Max);

    public sealed record TestArrayRequest(
        int Rows, int Cols, double SpacingMm,
        TestParamRange Param1, TestParamRange Param2);

    public sealed record CreateLayerRequest(string? Name, string? Color);

    public sealed record RetraceRequest(
        int ThresholdPercent = 50,
        bool Invert = false,
        float Brightness = 0f,
        float Contrast = 1f,
        string Mode = "outline",    // "outline" | "centerline"
        double SimplifyToleranceMm = 0.3
    );
    public sealed record UpdateLayerRequest(string? Name, string? Color, bool? Visible, bool? Locked);
    public sealed record NestRequest(double? MarginMm, double? SpacingMm, int? RotationStepDeg);

    public sealed record NestResultDto(
        ProjectDto Project, int PlacedCount, int SkippedCount, IReadOnlyList<string> Warnings);

    public sealed record PostProcessorDto(
        string Id, string DisplayName, string Description, string FileExtension, bool IsDefault);

    public sealed record GcodeRequest(string? PostId);

    public sealed record SyntheticPathDto(bool Closed, string? Layer, IReadOnlyList<double[]> Points);
    public sealed record SyntheticFileRequest(
        string DisplayName,
        IReadOnlyList<SyntheticPathDto> Paths,
        double? InitialX,
        double? InitialY);

    public sealed record GcodeResultDto(
        string PostId,
        string FileName,
        string Gcode,
        int LineCount,
        int CutCount,
        double TotalCutLengthMm,
        double TotalRapidLengthMm,
        IReadOnlyList<string> Warnings);

    private static double NormalizeDeg(double deg)
    {
        deg %= 360;
        return deg < 0 ? deg + 360 : deg;
    }

    // Applies a test-array parameter override: if param1 or param2 matches the
    // named type, return the swept value; otherwise return the project default.
    private static double ApplyParam(
        double defaultVal, string paramName,
        TestParamRange p1, double v1, TestParamRange p2, double v2)
    {
        if (p1.Type.Equals(paramName, StringComparison.OrdinalIgnoreCase)) return v1;
        if (p2.Type.Equals(paramName, StringComparison.OrdinalIgnoreCase)) return v2;
        return defaultVal;
    }

    public sealed record CutDto(
        Guid PartId,
        Guid SourcePathId,
        string? Layer,
        CutSide Side,
        bool Closed,
        int LeadInPointCount,
        int LeadOutPointCount,
        double PierceDelayS,
        double FeedRateMmMin,
        double CutLengthMm,
        IReadOnlyList<double[]> Points);

    public sealed record ToolpathDto(
        IReadOnlyList<CutDto> Cuts,
        double TotalCutLengthMm,
        double TotalRapidLengthMm,
        IReadOnlyList<string> Warnings);

    private static ToolpathDto ToToolpathDto(Toolpath tp) => new(
        tp.Cuts.Select(c => new CutDto(
            c.PartId,
            c.SourcePathId,
            c.Layer,
            c.Side,
            c.IsClosedContour,
            c.LeadInPointCount,
            c.LeadOutPointCount,
            c.PierceDelayS,
            c.FeedRateMmMin,
            Math.Round(c.CutLengthMm(), 2),
            c.Points.Select(p => new[] { Math.Round(p.X, 2), Math.Round(p.Y, 2) }).ToList()))
            .ToList(),
        Math.Round(tp.TotalCutLengthMm, 2),
        Math.Round(tp.TotalRapidLengthMm(new Backend.Geometry.Point2(0, 0)), 2),
        tp.Warnings);

    private static ProjectDto ToDto(Project p) => new(
        p.Name,
        p.Units,
        new TableSettingsDto(
            p.Table.WidthMm, p.Table.HeightMm, p.Table.Origin, p.Table.MaterialThicknessMm),
        p.Files.Select(ToFileDto).ToList(),
        p.Parts.Select(x => new PartDto(x.Id, x.FileId, x.X, x.Y, x.RotationDeg, x.ScaleX, x.ScaleY, x.LayerId)).ToList(),
        p.Layers.Select(l => new LayerDto(l.Id, l.Name, l.Color, l.Visible, l.Locked)).ToList());

    private static FileSummaryDto ToFileDto(ImportedFile f)
    {
        double maxX = 0, maxY = 0;
        int points = 0;
        foreach (var path in f.Paths)
        {
            points += path.Polyline.Points.Count;
            var (_, max) = path.Polyline.Bounds();
            if (max.X > maxX) maxX = max.X;
            if (max.Y > maxY) maxY = max.Y;
        }
        // Geometry is normalized to (0,0) at import, so max == size.
        return new FileSummaryDto(
            f.Id,
            f.FileName,
            f.DisplayName,
            f.Kind,
            f.Visible,
            f.Paths.Count,
            f.Paths.Count(p => p.Polyline.IsClosed),
            f.Paths.Count(p => !p.Polyline.IsClosed),
            points,
            Math.Round(maxX, 2),
            Math.Round(maxY, 2),
            f.Paths.Select(p => p.Layer).OfType<string>().Distinct().Order().ToList(),
            f.Warnings,
            f.BitmapData is not null,
            f.BitmapTraceSettingsJson);
    }
}
