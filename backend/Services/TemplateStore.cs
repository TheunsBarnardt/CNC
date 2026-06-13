using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Persisted project templates — full Project snapshots the user can name and
/// reopen later. Templates are useful for "I always cut the same way on this
/// material" workflows. Each template is one JSON file under LocalAppData.
///
/// <para>The store keeps a tiny in-memory index (id, name, timestamp, part/file
/// counts, description) for the list endpoint so we can avoid reading the full
/// project blobs from disk on every list call.</para>
/// </summary>
public sealed class TemplateStore
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly List<TemplateInfo> _index = [];

    public TemplateStore() : this(DefaultDir()) { }

    /// <summary>Test seam: point the store at any directory.</summary>
    public TemplateStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
        Load();
    }

    private static string DefaultDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "templates");

    public IReadOnlyList<TemplateInfo> All()
    {
        lock (_gate) return _index.Select(Clone).ToList();
    }

    public TemplateInfo Save(string name, string description, Project project)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        var info = new TemplateInfo
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim() ?? "",
            CreatedAt = DateTimeOffset.UtcNow,
            FileCount = project.Files.Count,
            PartCount = project.Parts.Count,
        };

        var path = PathFor(info.Id);
        File.WriteAllText(path, JsonSerializer.Serialize(project, ProjectService.JsonOptions));

        lock (_gate) _index.Add(Clone(info));
        Save();
        return info;
    }

    public Project? LoadProject(Guid id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Project>(stream, ProjectService.JsonOptions);
        }
        catch (JsonException)
        {
            // Corrupt template — remove the index entry but leave the file for inspection.
            lock (_gate) _index.RemoveAll(t => t.Id == id);
            Save();
            return null;
        }
    }

    public bool Delete(Guid id)
    {
        var path = PathFor(id);
        bool removed;
        lock (_gate) removed = _index.RemoveAll(t => t.Id == id) > 0;
        if (removed) Save();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
        return removed;
    }

    private string PathFor(Guid id) => Path.Combine(_dir, $"{id:N}.json");

    private void Load()
    {
        // The directory itself is the source of truth: each .json file IS a template.
        // The index is rebuilt by scanning files. We only persist the small info records
        // for fast cold starts.
        _index.Clear();
        foreach (var path in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var project = JsonSerializer.Deserialize<Project>(stream, ProjectService.JsonOptions);
                if (project is null) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                if (!Guid.TryParseExact(name, "N", out var id)) continue;
                _index.Add(new TemplateInfo
                {
                    Id = id,
                    Name = project.Name,
                    Description = "",   // description wasn't in v1 schema
                    CreatedAt = File.GetCreationTimeUtc(path),
                    FileCount = project.Files.Count,
                    PartCount = project.Parts.Count,
                });
            }
            catch (JsonException) { /* skip corrupt */ }
        }
        // Sort newest first for a friendlier default ordering.
        _index.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
    }

    private void Save()
    {
        // Persist the index for fast cold starts. (Source of truth is still the directory.)
        var indexPath = Path.Combine(_dir, "index.json");
        lock (_gate)
            File.WriteAllText(indexPath, JsonSerializer.Serialize(_index));
    }

    private static TemplateInfo Clone(TemplateInfo t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Description = t.Description,
        CreatedAt = t.CreatedAt,
        FileCount = t.FileCount,
        PartCount = t.PartCount,
    };
}

/// <summary>Lightweight summary of a saved template — used by the list endpoint.</summary>
public sealed record TemplateInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public int FileCount { get; init; }
    public int PartCount { get; init; }
}
