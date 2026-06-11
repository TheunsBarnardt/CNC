namespace Backend.Models;

/// <summary>
/// Naive shelf placement for newly imported/created parts so they don't all
/// stack at the origin. Deliberately dumb — real packing is the auto-nester's
/// job (Milestone 2); this only needs to drop parts somewhere sensible.
/// </summary>
public static class PartPlacer
{
    private const double Gap = 10;

    public static Part PlaceNew(Project project, ImportedFile file)
    {
        var part = new Part { FileId = file.Id, LayerId = project.Layers.FirstOrDefault()?.Id };

        // Cursor = just right of the rightmost existing part, staying on that
        // part's row; wrap to a fresh row above everything when the table
        // width runs out.
        double cursorX = Gap, cursorY = Gap, newRowY = Gap;
        foreach (var existing in project.Parts)
        {
            var existingFile = project.Files.FirstOrDefault(f => f.Id == existing.FileId);
            if (existingFile is null) continue;
            var (min, max) = PartTransform.WorldBounds(existing, existingFile);
            if (max.X + Gap > cursorX)
            {
                cursorX = max.X + Gap;
                cursorY = min.Y;
            }
            if (max.Y + Gap > newRowY) newRowY = max.Y + Gap;
        }

        var (lmin, lmax) = LocalBounds(file);
        double width = lmax.X - lmin.X;
        if (cursorX + width + Gap > project.Table.WidthMm && cursorX > Gap)
        {
            part.X = Gap;
            part.Y = newRowY;
        }
        else
        {
            part.X = cursorX;
            part.Y = cursorY;
        }
        return part;
    }

    private static (Geometry.Point2 Min, Geometry.Point2 Max) LocalBounds(ImportedFile file)
    {
        // Geometry is normalized to (0,0) at import, so bounds = (0,0)..size,
        // but compute defensively in case of older project files.
        var identity = new Part { FileId = file.Id };
        return PartTransform.WorldBounds(identity, file);
    }
}
