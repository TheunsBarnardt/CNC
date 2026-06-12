using Backend.Services;

namespace Backend.Api;

public static class TemplateApi
{
    public static void MapTemplateApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/templates");

        // List all saved templates (metadata only).
        group.MapGet("/", (TemplateStore store) =>
            Results.Ok(store.ListAll()));

        // Save the current project as a named template.
        group.MapPost("/", (SaveTemplateRequest request, TemplateStore store, ProjectService projects) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required." });

            var info = projects.With(p => store.Save(request.Name, p, request.Description ?? ""));
            return Results.Ok(info);
        });

        // Load a template: replaces the current project with the saved snapshot.
        group.MapPost("/{id:guid}/load", (Guid id, TemplateStore store, ProjectService projects) =>
        {
            var template = store.Load(id);
            if (template is null)
                return Results.NotFound(new { error = "Template not found." });

            // Give the loaded template a fresh name so the user knows it's a copy.
            template.Name = $"{template.Name} (from template)";
            projects.ReplaceProject(template);
            return Results.Ok(projects.With(ProjectApi.ToProjectDto));
        });

        // Delete a saved template.
        group.MapDelete("/{id:guid}", (Guid id, TemplateStore store) =>
            store.Delete(id)
                ? Results.Ok(new { deleted = true })
                : Results.NotFound());
    }
}

public sealed record SaveTemplateRequest(string Name, string? Description);
