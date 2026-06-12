using System.Text.Json;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Persists reusable shape elements (like a clip-art library). Each element is
/// a named set of geometry paths that can be inserted into any project. Stored
/// under LocalAppData, one JSON file per element.
/// </summary>
public sealed class ElementStore
{
    private readonly string _dir;

    public ElementStore() : this(DefaultDir()) { }

    public ElementStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
    }

    private static string DefaultDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "library");

    public IReadOnlyList<ElementInfo> ListAll()
    {
        var list = new List<ElementInfo>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var el = JsonSerializer.Deserialize<LibraryElement>(stream, ProjectService.JsonOptions);
                if (el is not null)
                    list.Add(ToInfo(el));
            }
            catch { /* skip corrupt */ }
        }
        return list.OrderBy(e => e.Name).ToList();
    }

    public ElementInfo Save(string name, ImportedFile file)
    {
        var element = new LibraryElement
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAt = DateTime.UtcNow,
            Paths = file.Paths.Select(p => new LibraryPath
            {
                Layer = p.Layer,
                IsClosed = p.Polyline.IsClosed,
                Points = p.Polyline.Points.Select(pt => new double[] { pt.X, pt.Y }).ToList(),
            }).ToList(),
            WidthMm = file.Paths.SelectMany(p => p.Polyline.Points).Select(pt => pt.X).DefaultIfEmpty(0).Max(),
            HeightMm = file.Paths.SelectMany(p => p.Polyline.Points).Select(pt => pt.Y).DefaultIfEmpty(0).Max(),
        };
        var path = FilePath(element.Id);
        File.WriteAllText(path, JsonSerializer.Serialize(element, ProjectService.JsonOptions));
        return ToInfo(element);
    }

    public List<PathGeometry>? LoadPaths(Guid id)
    {
        var path = FilePath(id);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var el = JsonSerializer.Deserialize<LibraryElement>(stream, ProjectService.JsonOptions);
            if (el is null) return null;
            return el.Paths.Select(p => new PathGeometry
            {
                Layer = p.Layer,
                Polyline = new Polyline2
                {
                    IsClosed = p.IsClosed,
                    Points = p.Points.Select(pt => new Point2(pt[0], pt[1])).ToList(),
                },
            }).ToList();
        }
        catch { return null; }
    }

    public bool Delete(Guid id)
    {
        var path = FilePath(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string FilePath(Guid id) => Path.Combine(_dir, $"{id}.json");

    private static ElementInfo ToInfo(LibraryElement el) =>
        new(el.Id, el.Name, el.CreatedAt, el.Paths.Count, el.WidthMm, el.HeightMm);
}

public sealed record ElementInfo(
    Guid Id, string Name, DateTime CreatedAt, int PathCount, double WidthMm, double HeightMm);

/// <summary>On-disk representation of a library element.</summary>
internal sealed class LibraryElement
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; init; }
    public List<LibraryPath> Paths { get; init; } = [];
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
}

internal sealed class LibraryPath
{
    public string? Layer { get; set; }
    public bool IsClosed { get; set; }
    public List<double[]> Points { get; init; } = [];
}
