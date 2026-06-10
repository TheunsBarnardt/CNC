using Backend.Models;
using Backend.Services;

namespace Backend.Api;

public static class ProfileApi
{
    public static void MapProfileApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profiles");

        group.MapGet("/", (ProfileStore store) => Results.Ok(store.All()));

        group.MapPost("/", (MaterialProfile profile, ProfileStore store) =>
        {
            var error = Validate(profile);
            if (error is not null) return Results.BadRequest(new { error });
            return Results.Ok(store.Add(profile));
        });

        group.MapPut("/{id:guid}", (Guid id, MaterialProfile profile, ProfileStore store) =>
        {
            if (profile.Id != id)
                return Results.BadRequest(new { error = "Profile id mismatch." });
            var error = Validate(profile);
            if (error is not null) return Results.BadRequest(new { error });
            return store.Update(profile) ? Results.Ok(profile) : Results.NotFound();
        });

        group.MapDelete("/{id:guid}", (Guid id, ProfileStore store) =>
            store.Delete(id) ? Results.Ok() : Results.NotFound());

        // Share profiles between machines/users as one JSON file.
        group.MapGet("/export", (ProfileStore store) => Results.File(
            System.Text.Encoding.UTF8.GetBytes(store.ExportJson()),
            "application/json",
            "material-profiles.json"));

        group.MapPost("/import", async (HttpRequest request, ProfileStore store) =>
        {
            try
            {
                int count;
                if (request.HasFormContentType)
                {
                    var form = await request.ReadFormAsync();
                    var upload = form.Files.FirstOrDefault();
                    if (upload is null)
                        return Results.BadRequest(new { error = "No profile file in request." });
                    await using var stream = upload.OpenReadStream();
                    count = store.ImportJson(stream);
                }
                else
                {
                    count = store.ImportJson(request.Body);
                }
                return Results.Ok(new { imported = count, profiles = store.All() });
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .DisableAntiforgery();
    }

    private static string? Validate(MaterialProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return "Profile name is required.";
        if (p.ThicknessMm < 0) return "Thickness cannot be negative.";
        if (p.KerfWidthMm < 0) return "Kerf width cannot be negative.";
        if (p.FeedRateMmMin <= 0) return "Feed rate must be positive.";
        if (p.PierceDelayS < 0) return "Pierce delay cannot be negative.";
        if (p.CutHeightMm < 0 || p.PierceHeightMm < 0) return "Heights cannot be negative.";
        return null;
    }
}
