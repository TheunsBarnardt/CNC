using System.Text.Json;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// App-level material profile storage: one JSON file under LocalAppData so
/// profiles survive across projects and app restarts. All access is locked;
/// every mutation writes through to disk immediately (profiles are small and
/// rarely change, so simplicity beats write batching).
/// </summary>
public sealed class ProfileStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private List<MaterialProfile> _profiles;

    public ProfileStore() : this(DefaultPath()) { }

    /// <summary>Test seam: point the store at any file path.</summary>
    public ProfileStore(string path)
    {
        _path = path;
        _profiles = Load();
        if (_profiles.Count == 0)
        {
            _profiles = SeedProfiles();
            Save();
        }
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "material-profiles.json");

    public IReadOnlyList<MaterialProfile> All()
    {
        lock (_gate) return _profiles.Select(Clone).ToList();
    }

    public MaterialProfile? Find(Guid id)
    {
        lock (_gate)
        {
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            return p is null ? null : Clone(p);
        }
    }

    public MaterialProfile Add(MaterialProfile profile)
    {
        lock (_gate)
        {
            _profiles.Add(Clone(profile));
            Save();
            return profile;
        }
    }

    public bool Update(MaterialProfile profile)
    {
        lock (_gate)
        {
            int i = _profiles.FindIndex(x => x.Id == profile.Id);
            if (i < 0) return false;
            _profiles[i] = Clone(profile);
            Save();
            return true;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            if (_profiles.RemoveAll(x => x.Id == id) == 0) return false;
            Save();
            return true;
        }
    }

    public string ExportJson()
    {
        lock (_gate) return JsonSerializer.Serialize(_profiles, ProjectService.JsonOptions);
    }

    /// <summary>
    /// Merges profiles from an exported file: same id = replace, new id = add.
    /// Returns how many were imported. Throws <see cref="FormatException"/> on bad input.
    /// </summary>
    public int ImportJson(Stream json)
    {
        List<MaterialProfile> incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<List<MaterialProfile>>(json, ProjectService.JsonOptions)
                ?? throw new FormatException("Profile file is empty.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Not a valid profile file: {ex.Message}", ex);
        }

        lock (_gate)
        {
            foreach (var p in incoming)
            {
                int i = _profiles.FindIndex(x => x.Id == p.Id);
                if (i >= 0) _profiles[i] = p;
                else _profiles.Add(p);
            }
            Save();
            return incoming.Count;
        }
    }

    private List<MaterialProfile> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<List<MaterialProfile>>(stream, ProjectService.JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt profile file shouldn't brick the app; start fresh and
            // keep the broken file aside for the user.
            try { File.Move(_path, _path + ".corrupt", overwrite: true); } catch { /* best effort */ }
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_profiles, ProjectService.JsonOptions));
    }

    private static MaterialProfile Clone(MaterialProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Material = p.Material,
        ThicknessMm = p.ThicknessMm,
        KerfWidthMm = p.KerfWidthMm,
        FeedRateMmMin = p.FeedRateMmMin,
        PierceDelayS = p.PierceDelayS,
        CutHeightMm = p.CutHeightMm,
        PierceHeightMm = p.PierceHeightMm,
    };

    /// <summary>
    /// Starter profiles so the picker isn't empty on first run. Cutting numbers
    /// depend heavily on the torch/amperage — these are ballpark values for a
    /// ~45A air plasma and are explicitly labeled as needing tuning.
    /// </summary>
    private static List<MaterialProfile> SeedProfiles() =>
    [
        new()
        {
            Name = "Mild steel 1.5mm — example, tune for your machine",
            Material = "Mild steel",
            ThicknessMm = 1.5,
            KerfWidthMm = 1.3,
            FeedRateMmMin = 3500,
            PierceDelayS = 0.3,
            CutHeightMm = 1.5,
            PierceHeightMm = 3.5,
        },
        new()
        {
            Name = "Mild steel 3mm — example, tune for your machine",
            Material = "Mild steel",
            ThicknessMm = 3,
            KerfWidthMm = 1.5,
            FeedRateMmMin = 2200,
            PierceDelayS = 0.5,
            CutHeightMm = 1.5,
            PierceHeightMm = 3.8,
        },
        new()
        {
            Name = "Mild steel 6mm — example, tune for your machine",
            Material = "Mild steel",
            ThicknessMm = 6,
            KerfWidthMm = 1.8,
            FeedRateMmMin = 1100,
            PierceDelayS = 0.8,
            CutHeightMm = 1.8,
            PierceHeightMm = 4.0,
        },
    ];
}
