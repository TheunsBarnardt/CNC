using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Persists project templates under LocalAppData so users can start new
/// projects from a saved layout. Each template = a stripped project snapshot
/// (no raster bitmap data) stored as a JSON file.
/// </summary>
public sealed class TemplateStore
{
    private readonly string _dir;

    public TemplateStore() : this(DefaultDir()) { }

    public TemplateStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
    }

    private static string DefaultDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "templates");

    public IReadOnlyList<TemplateInfo> ListAll()
    {
        var infos = new List<TemplateInfo>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var tpl = JsonSerializer.Deserialize<TemplateFile>(stream, ProjectService.JsonOptions);
                if (tpl is not null)
                    infos.Add(new TemplateInfo(tpl.Id, tpl.Name, tpl.CreatedAt, tpl.PartCount, tpl.FileCount, tpl.Description));
            }
            catch { /* skip corrupt files */ }
        }
        return infos.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public TemplateInfo Save(string name, Project project, string description = "")
    {
        // Strip bitmap bytes so templates stay small.
        var id = Guid.NewGuid();
        var tpl = new TemplateFile
        {
            Id = id,
            Name = name.Trim(),
            Description = description.Trim(),
            CreatedAt = DateTime.UtcNow,
            PartCount = project.Parts.Count,
            FileCount = project.Files.Count,
            ProjectJson = SerializeStripped(project),
        };
        var path = FilePath(id);
        File.WriteAllText(path, JsonSerializer.Serialize(tpl, ProjectService.JsonOptions));
        return new TemplateInfo(tpl.Id, tpl.Name, tpl.CreatedAt, tpl.PartCount, tpl.FileCount, tpl.Description);
    }

    /// <summary>Returns the project snapshot for a template, or null if not found.</summary>
    public Project? Load(Guid id)
    {
        var path = FilePath(id);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var tpl = JsonSerializer.Deserialize<TemplateFile>(stream, ProjectService.JsonOptions);
            if (tpl?.ProjectJson is null) return null;
            return JsonSerializer.Deserialize<Project>(tpl.ProjectJson, ProjectService.JsonOptions);
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

    private static string SerializeStripped(Project project)
    {
        // Clone without bitmap bytes — they can be several MB each.
        var stripped = new Project
        {
            SchemaVersion = project.SchemaVersion,
            Name = project.Name,
            Units = project.Units,
            Table = project.Table,
            Cam = project.Cam,
        };
        foreach (var f in project.Files)
        {
            var sf = new ImportedFile
            {
                FileName = f.FileName,
                DisplayName = f.DisplayName,
                Kind = f.Kind,
                Visible = f.Visible,
            };
            sf.Paths.AddRange(f.Paths);
            sf.Warnings.AddRange(f.Warnings);
            // BitmapData intentionally omitted.
            stripped.Files.Add(sf);
        }
        stripped.Parts.AddRange(project.Parts);
        stripped.Layers.Clear();
        stripped.Layers.AddRange(project.Layers);
        return JsonSerializer.Serialize(stripped, ProjectService.JsonOptions);
    }
}

public sealed record TemplateInfo(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    int PartCount,
    int FileCount,
    string Description);

/// <summary>On-disk representation of a saved template.</summary>
internal sealed class TemplateFile
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; init; }
    public int PartCount { get; set; }
    public int FileCount { get; set; }
    public string? ProjectJson { get; set; }
}
