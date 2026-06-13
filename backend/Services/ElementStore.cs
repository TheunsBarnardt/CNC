using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Persisted element library: a named, reusable geometric element. Saving an
/// element serializes a full <see cref="ImportedFile"/> (with its paths) as
/// one JSON file under LocalAppData. Inserting an element into the current
/// project deep-copies the geometry into a new <see cref="ImportedFile"/> on
/// the project and immediately places it on the table.
///
/// <para>Modelled after <see cref="TemplateStore"/>: the directory is the
/// source of truth, the index file is just for fast cold starts.</para>
/// </summary>
public sealed class ElementStore
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly List<ElementInfo> _index = [];

    public ElementStore() : this(DefaultDir()) { }

    /// <summary>Test seam: point the store at any directory.</summary>
    public ElementStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
        Load();
    }

    private static string DefaultDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "elements");

    public IReadOnlyList<ElementInfo> All()
    {
        lock (_gate) return _index.Select(Clone).ToList();
    }

    /// <summary>Save a copy of the given file (paths only) under the element name.</summary>
    public ElementInfo Save(string name, ImportedFile source)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Element name is required.", nameof(name));
        if (source is null) throw new ArgumentNullException(nameof(source));

        var (minX, minY, maxX, maxY) = source.BoundingBox;
        var info = new ElementInfo
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            PathCount = source.Paths.Count,
            WidthMm = Math.Round(maxX - minX, 2),
            HeightMm = Math.Round(maxY - minY, 2),
        };

        // Persist only the path data — drop the bitmap bytes and the
        // transient DisplayName; both are project-local concerns.
        var payload = new ElementPayload
        {
            Name = source.DisplayName,
            Paths = source.Paths.Select(ClonePath).ToList(),
        };

        File.WriteAllText(PathFor(info.Id), JsonSerializer.Serialize(payload, ProjectService.JsonOptions));
        lock (_gate) _index.Add(Clone(info));
        Save();
        return info;
    }

    /// <summary>
    /// Loads the element's saved geometry as a fresh <see cref="ImportedFile"/>
    /// that can be appended to a project's <c>Files</c> list. The element's
    /// stable name is carried over as the new file's DisplayName.
    /// </summary>
    public ImportedFile? LoadFile(Guid id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;

        ElementPayload? payload;
        try
        {
            using var stream = File.OpenRead(path);
            payload = JsonSerializer.Deserialize<ElementPayload>(stream, ProjectService.JsonOptions);
        }
        catch (JsonException) { return null; }
        if (payload is null) return null;

        var info = FindInfo(id);
        var file = new ImportedFile
        {
            FileName = $"{info?.Name ?? payload.Name}.element",
            DisplayName = info?.Name ?? payload.Name,
            Kind = ImportedFileKind.Shape,  // elements are user-curated geometry
        };
        foreach (var p in payload.Paths)
            file.Paths.Add(ClonePathBack(p));
        return file;
    }

    public bool Delete(Guid id)
    {
        var path = PathFor(id);
        bool removed;
        lock (_gate) removed = _index.RemoveAll(e => e.Id == id) > 0;
        if (removed) Save();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
        return removed;
    }

    private ElementInfo? FindInfo(Guid id)
    {
        lock (_gate) return _index.FirstOrDefault(e => e.Id == id);
    }

    private string PathFor(Guid id) => Path.Combine(_dir, $"{id:N}.json");

    private void Load()
    {
        _index.Clear();
        foreach (var path in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var fname = Path.GetFileNameWithoutExtension(path);
            if (fname == "index") continue;
            if (!Guid.TryParseExact(fname, "N", out var id)) continue;

            try
            {
                using var stream = File.OpenRead(path);
                var payload = JsonSerializer.Deserialize<ElementPayload>(stream, ProjectService.JsonOptions);
                if (payload is null) continue;
                var (minX, minY, maxX, maxY) = ComputeBounds(payload.Paths);
                _index.Add(new ElementInfo
                {
                    Id = id,
                    Name = payload.Name,
                    CreatedAt = File.GetCreationTimeUtc(path),
                    PathCount = payload.Paths.Count,
                    WidthMm = Math.Round(maxX - minX, 2),
                    HeightMm = Math.Round(maxY - minY, 2),
                });
            }
            catch (JsonException) { /* skip corrupt */ }
        }
        _index.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
    }

    private void Save()
    {
        var indexPath = Path.Combine(_dir, "index.json");
        lock (_gate)
            File.WriteAllText(indexPath, JsonSerializer.Serialize(_index));
    }

    private static (double minX, double minY, double maxX, double maxY) ComputeBounds(
        IReadOnlyList<PathPayload> paths)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in paths)
            foreach (var pt in p.Points)
            {
                if (pt[0] < minX) minX = pt[0];
                if (pt[1] < minY) minY = pt[1];
                if (pt[0] > maxX) maxX = pt[0];
                if (pt[1] > maxY) maxY = pt[1];
            }
        return minX > maxX ? (0, 0, 0, 0) : (minX, minY, maxX, maxY);
    }

    private static PathPayload ClonePath(PathGeometry p) => new()
    {
        Closed = p.Polyline.IsClosed,
        Layer = p.Layer,
        Points = p.Polyline.Points.Select(pt => new[] { pt.X, pt.Y }).ToList(),
        Handles = p.Handles?.Select(h => h?.ToArray()).ToList(),
    };

    private static PathGeometry ClonePathBack(PathPayload p) => new()
    {
        Layer = p.Layer,
        Polyline = new Backend.Geometry.Polyline2
        {
            IsClosed = p.Closed,
            Points = p.Points.Select(pt => new Backend.Geometry.Point2(pt[0], pt[1])).ToList(),
        },
        Handles = p.Handles?.Select(h => h?.ToArray()).ToList(),
    };

    private static ElementInfo Clone(ElementInfo e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        CreatedAt = e.CreatedAt,
        PathCount = e.PathCount,
        WidthMm = e.WidthMm,
        HeightMm = e.HeightMm,
    };

    private sealed class ElementPayload
    {
        public string Name { get; set; } = "";
        public List<PathPayload> Paths { get; set; } = [];
    }

    private sealed class PathPayload
    {
        public bool Closed { get; set; }
        public string? Layer { get; set; }
        public List<double[]> Points { get; set; } = [];
        public List<double[]?>? Handles { get; set; }
    }
}

/// <summary>Lightweight summary of a saved library element — used by the list endpoint.</summary>
public sealed record ElementInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public int PathCount { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
}
