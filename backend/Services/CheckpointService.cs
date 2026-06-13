using System.Text.Json;
using Backend.Machine;

namespace Backend.Services;

/// <summary>
/// Persists job checkpoints to disk so power-loss or crash recovery is
/// possible. <see cref="MachineConnectionManager"/> writes progress here
/// during streaming; <see cref="Backend.Api.MachineApi"/> reads it back
/// for the resume endpoint.
///
/// <para>On disk: a small <c>meta.json</c> (updated every 50 lines) plus a
/// <c>gcode.txt</c> written once at job start. Splitting them keeps the
/// hot-path progress writes fast and lets the big G-code blob stay
/// unchanged while a job runs.</para>
/// </summary>
public sealed class CheckpointService
{
    private readonly object _gate = new();
    private readonly string _metaPath;
    private readonly string _gcodePath;

    public CheckpointService() : this(DefaultMetaPath(), DefaultGcodePath()) { }

    /// <summary>Test seam: point the service at any meta/gcode path pair.</summary>
    public CheckpointService(string metaPath, string gcodePath)
    {
        _metaPath = metaPath;
        _gcodePath = gcodePath;
    }

    private static string DefaultMetaPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "job-checkpoint.json");

    private static string DefaultGcodePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "diy-grbl-cam", "job-gcode.txt");

    /// <summary>True when a checkpoint exists on disk (i.e. a recoverable job is pending).</summary>
    public bool Exists
    {
        get
        {
            lock (_gate) return File.Exists(_metaPath) && File.Exists(_gcodePath);
        }
    }

    /// <summary>Record the start of a new job. Overwrites any prior checkpoint.</summary>
    public void SaveJobStart(string jobId, DateTimeOffset startedAt, int totalLines,
        IReadOnlyList<string> gcodeLines)
    {
        lock (_gate)
        {
            // Write G-code first; if meta.json references a file that doesn't
            // exist, recovery can't run, so we keep the meta in sync.
            var gcodeDir = Path.GetDirectoryName(_gcodePath);
            if (!string.IsNullOrEmpty(gcodeDir)) Directory.CreateDirectory(gcodeDir);
            File.WriteAllLines(_gcodePath, gcodeLines);

            var meta = new CheckpointMeta
            {
                JobId = jobId,
                StartedAt = startedAt,
                CheckpointAt = DateTimeOffset.UtcNow,
                LastLineDone = 0,
                TotalLines = totalLines,
                LastX = 0, LastY = 0, LastZ = 0,
            };
            WriteMeta(meta);
        }
    }

    /// <summary>Update progress on the currently active checkpoint. No-op if none exists.</summary>
    public void UpdateProgress(int lastLineDone, DateTimeOffset at, double x, double y, double z)
    {
        lock (_gate)
        {
            if (!File.Exists(_metaPath)) return;
            CheckpointMeta current;
            try
            {
                using var stream = File.OpenRead(_metaPath);
                current = JsonSerializer.Deserialize<CheckpointMeta>(stream, ProjectService.JsonOptions)
                          ?? new CheckpointMeta();
            }
            catch (JsonException) { return; }

            // Don't write a checkpoint with a smaller line number than the last one
            // — progress callbacks can be reordered under contention.
            int safeLine = Math.Max(current.LastLineDone, lastLineDone);
            current.CheckpointAt = at;
            current.LastLineDone = safeLine;
            current.LastX = x; current.LastY = y; current.LastZ = z;
            WriteMeta(current);
        }
    }

    /// <summary>Reads the current checkpoint, or null if none exists (or G-code file is missing).</summary>
    public JobCheckpoint? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_metaPath) || !File.Exists(_gcodePath)) return null;

            CheckpointMeta meta;
            try
            {
                using var stream = File.OpenRead(_metaPath);
                meta = JsonSerializer.Deserialize<CheckpointMeta>(stream, ProjectService.JsonOptions);
            }
            catch (JsonException) { return null; }
            if (meta is null) return null;

            string[] gcode;
            try { gcode = File.ReadAllLines(_gcodePath); }
            catch (IOException) { return null; }

            return new JobCheckpoint(
                JobId:        meta.JobId,
                StartedAt:    meta.StartedAt,
                CheckpointAt: meta.CheckpointAt,
                LastLineDone: meta.LastLineDone,
                TotalLines:   meta.TotalLines,
                LastX:        meta.LastX,
                LastY:        meta.LastY,
                LastZ:        meta.LastZ,
                GcodeLines:   gcode);
        }
    }

    /// <summary>Removes the checkpoint files. Called on clean completion.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            TryDelete(_metaPath);
            TryDelete(_gcodePath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private void WriteMeta(CheckpointMeta meta)
    {
        var dir = Path.GetDirectoryName(_metaPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Atomic-ish: serialize to temp then move into place so a crash
        // mid-write doesn't leave a half-written meta file.
        var tmp = _metaPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(meta, ProjectService.JsonOptions));
        if (File.Exists(_metaPath)) File.Replace(tmp, _metaPath, null);
        else File.Move(tmp, _metaPath);
    }

    /// <summary>
    /// Build a recovery G-code stream. The preamble (G21/G90/etc.) is always
    /// included, then we resume from the first <c>G0</c> (or any G-move with
    /// an X/Y) at or after the interrupted sendable-line count — that way the
    /// user re-homes and re-zeros, then the machine jumps to the start of the
    /// next cut and re-pierces cleanly. Resuming mid-line is unsafe (we'd be
    /// in the middle of an unknown motion segment), so we always roll forward
    /// to a safe point.
    /// </summary>
    /// <param name="fullLines">The complete G-code of the original job.</param>
    /// <param name="lastLineDone">Count of sendable (non-blank, non-comment) lines that have completed.
    /// <c>0</c> = nothing sent yet; <c>N</c> = the first N sendable lines are done.</param>
    /// <returns>The recovery line list and the index (in the original program) of the first rapid after the interrupted cut.</returns>
    public static (IReadOnlyList<string> Lines, int ResumeFromLine) BuildRecoveryGcode(
        IReadOnlyList<string> fullLines, int lastLineDone)
    {
        if (fullLines is null || fullLines.Count == 0)
            return (Array.Empty<string>(), 0);

        static bool IsSendable(string s)
        {
            var t = s.TrimStart();
            return t.Length > 0 && !t.StartsWith(";") && !t.StartsWith("(");
        }

        static bool IsMotionWithXY(string s)
        {
            var t = s.TrimStart();
            if (!(t.StartsWith("G0") || t.StartsWith("G1") ||
                  t.StartsWith("G2") || t.StartsWith("G3"))) return false;
            return t.Contains('X') || t.Contains('Y');
        }

        // Preamble = all sendable lines from the start up to (but not
        // including) the first motion line with an X or Y. Typically
        // "G21", "G90", "G94", "M5" — the modal setup a power-cycle may
        // have wiped.
        int preambleEnd = 0;
        for (int i = 0; i < fullLines.Count; i++)
        {
            if (!IsSendable(fullLines[i])) continue;
            if (IsMotionWithXY(fullLines[i])) { preambleEnd = i; break; }
            preambleEnd = i + 1;
        }

        // Resume point: the first motion line with an X/Y at or after the
        // lastLineDone-th sendable line. We count sendable lines so blank
        // lines and comments don't shift the index.
        int resumeFrom;
        if (lastLineDone <= 0)
        {
            resumeFrom = preambleEnd;
        }
        else
        {
            int sendableSeen = 0;
            resumeFrom = fullLines.Count;
            for (int i = 0; i < fullLines.Count; i++)
            {
                if (!IsSendable(fullLines[i])) continue;
                sendableSeen++;
                if (sendableSeen > lastLineDone) { resumeFrom = i; break; }
            }
        }

        // If lastLineDone covered every sendable line, the last cut in the
        // program may have been incomplete (no trailing M5). Find the last
        // motion-with-XY line — that's the start of whichever cut was in
        // progress when the stream stopped. If the program ends with a
        // complete cut (M5), there are no remaining moves, so the recovery
        // would be just the preamble.
        if (resumeFrom >= fullLines.Count)
        {
            int lastMotionIdx = -1;
            for (int i = 0; i < fullLines.Count; i++)
            {
                if (IsMotionWithXY(fullLines[i])) lastMotionIdx = i;
            }
            if (lastMotionIdx >= 0)
            {
                // The last cut is incomplete iff the program doesn't end with
                // an M5 after that motion line.
                bool hasTrailingM5 = false;
                for (int i = lastMotionIdx + 1; i < fullLines.Count; i++)
                {
                    if (fullLines[i].TrimStart().StartsWith("M5", StringComparison.Ordinal))
                    { hasTrailingM5 = true; break; }
                }
                resumeFrom = hasTrailingM5 ? fullLines.Count : lastMotionIdx;
            }
            else
            {
                resumeFrom = preambleEnd;
            }
        }

        // Assemble: preamble (sendable lines only) + from resumeFrom onwards.
        var recovery = new List<string>();
        for (int i = 0; i < preambleEnd; i++)
            if (IsSendable(fullLines[i])) recovery.Add(fullLines[i]);
        for (int i = resumeFrom; i < fullLines.Count; i++)
            if (IsSendable(fullLines[i])) recovery.Add(fullLines[i]);

        return (recovery, resumeFrom);
    }

    /// <summary>Disk-side metadata; the G-code lives in a sibling file so we can update progress cheaply.</summary>
    private sealed class CheckpointMeta
    {
        public string JobId { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset CheckpointAt { get; set; }
        public int LastLineDone { get; set; }
        public int TotalLines { get; set; }
        public double LastX { get; set; }
        public double LastY { get; set; }
        public double LastZ { get; set; }
    }
}
