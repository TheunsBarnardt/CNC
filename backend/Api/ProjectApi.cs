using Backend.Import;
using Backend.Models;
using Backend.Services;

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
        group.MapGet("/geometry", (ProjectService projects) =>
            Results.Ok(projects.With(p => new
            {
                files = p.Files.Select(f => new
                {
                    fileId = f.Id,
                    paths = f.Paths.Select(path => new
                    {
                        layer = path.Layer,
                        closed = path.Polyline.IsClosed,
                        points = path.Polyline.Points
                            .Select(pt => new[] { Math.Round(pt.X, 2), Math.Round(pt.Y, 2) })
                            .ToList(),
                    }).ToList(),
                }).ToList(),
            })));

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
                return true;
            });
            return found ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
        });

        group.MapPost("/parts/{id:guid}/duplicate", (Guid id, ProjectService projects) =>
        {
            var dto = projects.With<ProjectDto?>(p =>
            {
                var source = p.Parts.FirstOrDefault(x => x.Id == id);
                if (source is null) return null;
                // Same rotation, offset placement so the copy is visibly separate.
                p.Parts.Add(new Part
                {
                    FileId = source.FileId,
                    X = source.X + 15,
                    Y = source.Y + 15,
                    RotationDeg = source.RotationDeg,
                });
                return ToDto(p);
            });
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapDelete("/parts/{id:guid}", (Guid id, ProjectService projects) =>
        {
            bool removed = projects.With(p => p.Parts.RemoveAll(x => x.Id == id) > 0);
            return removed ? Results.Ok(projects.With(ToDto)) : Results.NotFound();
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
    }

    private static string SafeFileName(string name)
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
        IReadOnlyList<string> Warnings);

    public sealed record PartDto(Guid Id, Guid FileId, double X, double Y, double RotationDeg);

    public sealed record ProjectDto(
        string Name,
        Units Units,
        TableSettingsDto Table,
        IReadOnlyList<FileSummaryDto> Files,
        IReadOnlyList<PartDto> Parts);

    public sealed record ImportResultDto(
        string FileName, bool Ok, string? Error, FileSummaryDto? File);

    public sealed record UpdateSettingsRequest(string? Name, Units? Units, TableSettingsDto? Table);

    public sealed record UpdateFileRequest(string? DisplayName, bool? Visible);

    public sealed record CreatePartRequest(Guid FileId);

    public sealed record UpdatePartRequest(double? X, double? Y, double? RotationDeg);

    private static double NormalizeDeg(double deg)
    {
        deg %= 360;
        return deg < 0 ? deg + 360 : deg;
    }

    private static ProjectDto ToDto(Project p) => new(
        p.Name,
        p.Units,
        new TableSettingsDto(
            p.Table.WidthMm, p.Table.HeightMm, p.Table.Origin, p.Table.MaterialThicknessMm),
        p.Files.Select(ToFileDto).ToList(),
        p.Parts.Select(x => new PartDto(x.Id, x.FileId, x.X, x.Y, x.RotationDeg)).ToList());

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
            f.Warnings);
    }
}
