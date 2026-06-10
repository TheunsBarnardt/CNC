using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Machine;

namespace Backend.Services;

/// <summary>
/// Persists job-execution checkpoints to LocalAppData so a power-loss or
/// application-crash recovery can offer a safe resume from the start of the
/// interrupted cut.
///
/// Storage: two files under %LOCALAPPDATA%/diy-grbl-cam/
///   job-checkpoint-gcode.txt — raw G-code lines (written once per job)
///   job-checkpoint-meta.json — progress metadata (updated frequently)
///
/// G-code is stored separately so the frequent meta updates stay cheap.
/// Both files must be present for Load() to succeed; a corrupt or missing
/// pair produces null (no recovery available).
/// </summary>
public sealed class CheckpointService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _metaPath;
    private readonly string _gcodePath;
    private readonly object _gate = new();

    // Cached in memory so UpdateProgress can keep startedAt without re-reading.
    private CheckpointMeta? _current;

    public CheckpointService() : this(
        Path.Combine(AppDataDir(), "job-checkpoint-meta.json"),
        Path.Combine(AppDataDir(), "job-checkpoint-gcode.txt"))
    { }

    /// <summary>Test seam: inject explicit paths.</summary>
    public CheckpointService(string metaPath, string gcodePath)
    {
        _metaPath = metaPath;
        _gcodePath = gcodePath;
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
    }

    // ── Write path ────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a job starts. Writes the full G-code once; subsequent
    /// updates only rewrite the small metadata file.
    /// </summary>
    public void SaveJobStart(string jobId, DateTimeOffset startedAt, int totalLines,
        IReadOnlyList<string> gcodeLines)
    {
        lock (_gate)
        {
            File.WriteAllLines(_gcodePath, gcodeLines);
            _current = new CheckpointMeta(jobId, startedAt, startedAt, 0, totalLines, 0, 0, 0);
            WriteMetaLocked();
        }
    }

    /// <summary>
    /// Update progress + last-known position. Called frequently; only rewrites
    /// the small metadata file.
    /// </summary>
    public void UpdateProgress(int lastLineDone, DateTimeOffset at,
        double x, double y, double z)
    {
        lock (_gate)
        {
            if (_current is null) return;
            _current = _current with
            {
                LastLineDone = lastLineDone,
                CheckpointAt = at,
                LastX = x, LastY = y, LastZ = z,
            };
            WriteMetaLocked();
        }
    }

    // ── Read path ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the persisted checkpoint, or null if no valid checkpoint exists.
    /// </summary>
    public JobCheckpoint? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_metaPath) || !File.Exists(_gcodePath)) return null;
            try
            {
                var meta = JsonSerializer.Deserialize<CheckpointMeta>(
                    File.ReadAllText(_metaPath), JsonOpts);
                if (meta is null) return null;
                var lines = (IReadOnlyList<string>)File.ReadAllLines(_gcodePath);
                return new JobCheckpoint(
                    meta.JobId, meta.StartedAt, meta.CheckpointAt,
                    meta.LastLineDone, meta.TotalLines,
                    meta.LastX, meta.LastY, meta.LastZ,
                    lines);
            }
            catch { return null; }
        }
    }

    /// <summary>Delete the checkpoint — called on clean job completion.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            try { File.Delete(_metaPath); } catch { }
            try { File.Delete(_gcodePath); } catch { }
        }
    }

    // ── Recovery G-code builder ───────────────────────────────────────────

    /// <summary>
    /// Build recovery G-code from a checkpoint. Returns:
    ///   recoveryLines — preamble (mode/unit setup) + all lines from the safe
    ///                   resume point (the G0 that begins the interrupted cut)
    ///                   to the end of the program.
    ///   resumeFromSentLine — 0-based index in the sendable-line list where
    ///                        recovery begins (for display only).
    ///
    /// Safe resume point: the G0 rapid that immediately precedes the last M3
    /// (torch-on) at or before <paramref name="lastLineDone"/>. This re-runs
    /// the interrupted cut from scratch rather than starting mid-cut, which
    /// is the correct behaviour for plasma (fresh pierce).
    /// </summary>
    public static (IReadOnlyList<string> recoveryLines, int resumeFromSentLine)
        BuildRecoveryGcode(IReadOnlyList<string> gcodeLines, int lastLineDone)
    {
        // Work on the sendable (non-blank) lines to match the streamer's view.
        var sendable = gcodeLines
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToList();

        int sentCount = Math.Clamp(lastLineDone, 0, sendable.Count);

        // Preamble end: the index of the first rapid move (G0/G00).
        // Everything before it sets up machine state (G21, G90, M5, etc.).
        int preambleEnd = sendable.Count;
        for (int i = 0; i < sendable.Count; i++)
        {
            if (IsRapid(sendable[i])) { preambleEnd = i; break; }
        }

        // Find last M3 (torch on) at or before sentCount.
        int lastM3 = -1;
        for (int i = sentCount - 1; i >= 0; i--)
        {
            if (sendable[i].StartsWith("M3", StringComparison.OrdinalIgnoreCase))
            {
                lastM3 = i;
                break;
            }
        }

        int cutStart;
        if (lastM3 <= 0)
        {
            // No torch-on yet — restart from the first rapid (or from 0 if no rapids).
            cutStart = preambleEnd < sendable.Count ? preambleEnd : 0;
        }
        else
        {
            // Scan back from M3 to the preceding G0 (rapid to cut-start position).
            cutStart = lastM3; // fallback: start at the M3 itself
            for (int i = lastM3 - 1; i >= preambleEnd; i--)
            {
                if (IsRapid(sendable[i])) { cutStart = i; break; }
            }
        }

        // Recovery lines: preamble + everything from cutStart onwards.
        var recovery = new List<string>(preambleEnd + sendable.Count - cutStart);
        recovery.AddRange(sendable[..preambleEnd]);
        recovery.AddRange(sendable[cutStart..]);

        return (recovery, cutStart);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void WriteMetaLocked()
    {
        if (_current is null) return;
        File.WriteAllText(_metaPath, JsonSerializer.Serialize(_current, JsonOpts));
    }

    private static bool IsRapid(string line) =>
        line.StartsWith("G0 ", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("G00 ", StringComparison.OrdinalIgnoreCase)
        || line.Equals("G0", StringComparison.OrdinalIgnoreCase)
        || line.Equals("G00", StringComparison.OrdinalIgnoreCase);

    private static string AppDataDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "diy-grbl-cam");

    // ── Internal metadata record ──────────────────────────────────────────

    private sealed record CheckpointMeta(
        string JobId,
        DateTimeOffset StartedAt,
        DateTimeOffset CheckpointAt,
        int LastLineDone,
        int TotalLines,
        double LastX,
        double LastY,
        double LastZ);
}
