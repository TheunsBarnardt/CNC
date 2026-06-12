using Backend.Geometry;
using Backend.Models;
using SixLabors.ImageSharp;

namespace Backend.Import.Bitmap;

/// <summary>
/// Imports PNG/JPG raster images by tracing them to vector polylines.
/// Default trace settings are applied; the user can re-trace with
/// POST /api/project/files/{id}/retrace to adjust threshold / mode / etc.
/// </summary>
public sealed class BitmapImporter : IFileImporter
{
    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>([".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"]);

    public ImportedFile Import(Stream content, string fileName)
    {
        byte[] data;
        using (var ms = new MemoryStream())
        {
            content.CopyTo(ms);
            data = ms.ToArray();
        }

        string mime = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"            => "image/gif",
            ".bmp"            => "image/bmp",
            ".webp"           => "image/webp",
            _                 => "image/png",
        };

        var settings = new BitmapTraceSettings();
        var (paths, widthMm, heightMm) = BitmapTracer.Trace(data, settings);

        var file = new ImportedFile
        {
            FileName    = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            Kind        = ImportedFileKind.Bitmap,
            BitmapData  = data,
            BitmapMimeType = mime,
            BitmapTraceSettingsJson = System.Text.Json.JsonSerializer.Serialize(settings),
        };

        foreach (var poly in paths)
        {
            file.Paths.Add(new PathGeometry
            {
                Layer    = null,
                Polyline = poly,
            });
        }

        if (file.Paths.Count == 0)
            file.Warnings.Add("No foreground regions found at the current threshold — try adjusting the threshold and re-tracing.");

        return file;
    }
}
