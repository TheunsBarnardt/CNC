using Backend.Models;
using Backend.Services;
using Backend.Geometry;

namespace Backend.Api;

public static class LibraryApi
{
    public static void MapLibraryApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        // List all saved library elements.
        group.MapGet("/", (ElementStore store) =>
            Results.Ok(store.ListAll()));

        // Save a file from the current project as a named library element.
        group.MapPost("/", (SaveElementRequest request, ElementStore store, ProjectService projects) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required." });

            return projects.With<IResult>(p =>
            {
                var file = p.Files.FirstOrDefault(f => f.Id == request.FileId);
                if (file is null) return Results.NotFound(new { error = "File not found in project." });
                var info = store.Save(request.Name, file);
                return Results.Ok(info);
            });
        });

        // Insert a library element into the current project as a new synthetic file.
        group.MapPost("/{id:guid}/insert", (Guid id, ElementStore store, ProjectService projects) =>
        {
            var paths = store.LoadPaths(id);
            if (paths is null)
                return Results.NotFound(new { error = "Library element not found." });

            projects.Mutate(p =>
            {
                var file = new ImportedFile
                {
                    FileName = $"library-{id}.shape",
                    DisplayName = "Library element",
                    Kind = ImportedFileKind.Shape,
                };
                file.Paths.AddRange(paths);
                p.Files.Add(file);
                p.Parts.Add(PartPlacer.PlaceNew(p, file));
            });
            return Results.Ok(projects.With(ProjectApi.ToProjectDto));
        });

        // Delete a library element.
        group.MapDelete("/{id:guid}", (Guid id, ElementStore store) =>
            store.Delete(id)
                ? Results.Ok(new { deleted = true })
                : Results.NotFound());
    }
}

public sealed record SaveElementRequest(string Name, Guid FileId);
