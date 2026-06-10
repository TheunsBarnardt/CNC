using Backend.Services;
using Xunit;

namespace Backend.Tests;

/// <summary>
/// Tests for CheckpointService: persistence round-trips and the recovery
/// G-code builder (the safety-critical resume-point logic).
/// </summary>
public class CheckpointServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cnc-checkpoint-tests-" + Guid.NewGuid());
    private string MetaPath => Path.Combine(_dir, "meta.json");
    private string GcodePath => Path.Combine(_dir, "gcode.txt");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private CheckpointService MakeSvc() => new(MetaPath, GcodePath);

    // ── Persistence ──────────────────────────────────────────────────────

    [Fact]
    public void Load_returns_null_when_no_files_exist()
    {
        var svc = MakeSvc();
        Assert.Null(svc.Load());
    }

    [Fact]
    public void SaveJobStart_then_Load_roundtrips_all_fields()
    {
        var svc = MakeSvc();
        var lines = new[] { "G21", "G0 X0 Y0", "M3 S1000", "G1 X10 F2000", "M5" };
        var started = DateTimeOffset.UtcNow;

        svc.SaveJobStart("job-001", started, 5, lines);
        var cp = svc.Load();

        Assert.NotNull(cp);
        Assert.Equal("job-001", cp!.JobId);
        Assert.Equal(5, cp.TotalLines);
        Assert.Equal(0, cp.LastLineDone);
        Assert.Equal(lines.Length, cp.GcodeLines.Count);
    }

    [Fact]
    public void UpdateProgress_persists_position_and_line()
    {
        var svc = MakeSvc();
        svc.SaveJobStart("j", DateTimeOffset.UtcNow, 100, ["G21"]);
        svc.UpdateProgress(42, DateTimeOffset.UtcNow, 12.5, 34.0, 0.0);

        var cp = svc.Load();
        Assert.NotNull(cp);
        Assert.Equal(42, cp!.LastLineDone);
        Assert.Equal(12.5, cp.LastX);
        Assert.Equal(34.0, cp.LastY);
    }

    [Fact]
    public void Clear_removes_checkpoint()
    {
        var svc = MakeSvc();
        svc.SaveJobStart("j", DateTimeOffset.UtcNow, 10, ["G21"]);
        svc.Clear();
        Assert.Null(svc.Load());
    }

    [Fact]
    public void Load_returns_null_if_gcode_file_missing()
    {
        var svc = MakeSvc();
        svc.SaveJobStart("j", DateTimeOffset.UtcNow, 1, ["G21"]);
        File.Delete(GcodePath);
        Assert.Null(svc.Load());
    }

    // ── BuildRecoveryGcode — resume point logic ───────────────────────────

    // Typical plasma G-code: preamble → two complete cuts → one interrupted cut.
    private static readonly string[] TypicalGcode =
    [
        // Preamble (lines 0-2, sendable idx 0-2)
        "G21",
        "G90",
        "M5",
        // Cut 1 (complete) — sendable idx 3-7
        "G0 X10 Y10",
        "M3 S1000",
        "G4 P1.5",
        "G1 X20 F2000",
        "M5",
        // Cut 2 (complete) — sendable idx 8-12
        "G0 X30 Y10",
        "M3 S1000",
        "G4 P1.5",
        "G1 X40 F2000",
        "M5",
        // Cut 3 (interrupted mid-cut) — sendable idx 13-15
        "G0 X50 Y10",
        "M3 S1000",
        "G4 P1.5",
        // power loss happened after line 15 (3 lines of cut 3 sent)
    ];

    [Fact]
    public void BuildRecoveryGcode_resumes_from_G0_of_interrupted_cut()
    {
        // 16 sendable lines sent (preamble 0-2, cut1 3-7, cut2 8-12, cut3 13-15)
        var (recovery, resumeFrom) = CheckpointService.BuildRecoveryGcode(TypicalGcode, 16);

        // resumeFrom should be the G0 that starts cut 3 (sendable index 13)
        Assert.Equal(13, resumeFrom);

        // Recovery = preamble (3 lines) + cut 3 from its G0 (3 lines)
        Assert.Equal(6, recovery.Count);
        Assert.Equal("G0 X50 Y10", recovery[3]);  // first line after preamble is the G0 of cut 3
    }

    [Fact]
    public void BuildRecoveryGcode_includes_preamble()
    {
        var (recovery, _) = CheckpointService.BuildRecoveryGcode(TypicalGcode, 16);

        Assert.Equal("G21", recovery[0]);
        Assert.Equal("G90", recovery[1]);
        Assert.Equal("M5", recovery[2]);
    }

    [Fact]
    public void BuildRecoveryGcode_when_interrupted_before_first_M3_starts_from_first_rapid()
    {
        // Only preamble lines sent (3 lines, no M3 yet)
        var (recovery, resumeFrom) = CheckpointService.BuildRecoveryGcode(TypicalGcode, 3);

        // Should start from first G0 (sendable index 3)
        Assert.Equal(3, resumeFrom);
        Assert.StartsWith("G0", recovery[3]);
    }

    [Fact]
    public void BuildRecoveryGcode_skips_blank_lines()
    {
        var withBlanks = new[] { "G21", "", "G0 X0 Y0", "M3 S1000", "G1 X10", "M5" };
        // 5 sendable lines: G21, G0, M3, G1, M5
        var (recovery, _) = CheckpointService.BuildRecoveryGcode(withBlanks, 5);

        // No blank lines in recovery output
        Assert.DoesNotContain("", recovery);
    }

    [Fact]
    public void BuildRecoveryGcode_complete_job_returns_just_preamble()
    {
        // lastLineDone = all lines → no cuts remaining
        var gcode = new[] { "G21", "G0 X0 Y0", "M3 S1000", "G1 X10", "M5" };
        var (recovery, resumeFrom) = CheckpointService.BuildRecoveryGcode(gcode, gcode.Length);

        // Preamble (G21) + from last G0 onwards: G0 X0 Y0, M3, G1, M5
        // resumeFrom = 1 (index of G0)
        Assert.Equal(1, resumeFrom);
        Assert.Equal(5, recovery.Count); // preamble (G21) + cut 4 lines
    }
}
