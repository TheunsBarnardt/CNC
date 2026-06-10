using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Holds the single currently-open project in memory and (de)serializes it to
/// the one-file JSON project format. The backend owns project state because
/// later milestones (job state, recovery) need it server-side anyway.
/// </summary>
public sealed class ProjectService
{
    private readonly object _gate = new();
    private Project _project = new();
    private IReadOnlyList<string>? _lastGcodeLines;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    /// <summary>Runs <paramref name="action"/> with exclusive access to the project.</summary>
    public T With<T>(Func<Project, T> action)
    {
        lock (_gate) return action(_project);
    }

    public void Mutate(Action<Project> action)
    {
        lock (_gate) action(_project);
    }

    public void Reset(string name = "Untitled")
    {
        lock (_gate) _project = new Project { Name = name };
    }

    public string ExportJson()
    {
        lock (_gate) return JsonSerializer.Serialize(_project, JsonOptions);
    }

    /// <summary>Replaces the current project from saved JSON. Throws <see cref="FormatException"/> when invalid.</summary>
    public void LoadJson(Stream json)
    {
        Project loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<Project>(json, JsonOptions)
                ?? throw new FormatException("Project file is empty.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Not a valid project file: {ex.Message}", ex);
        }

        if (loaded.SchemaVersion > 1)
            throw new FormatException(
                $"Project file schema v{loaded.SchemaVersion} is newer than this app supports (v1).");

        lock (_gate) _project = loaded;
    }

    /// <summary>Cache the last generated G-code so /api/machine/run can stream it.</summary>
    public void CacheGcode(IReadOnlyList<string> lines)
    {
        lock (_gate) { _lastGcodeLines = lines; }
    }

    /// <summary>Returns the last cached G-code lines, or null if none generated yet.</summary>
    public IReadOnlyList<string>? GetCachedGcode()
    {
        lock (_gate) { return _lastGcodeLines; }
    }
}
