using Backend.Cam;
using Backend.Geometry;
using Backend.Models;
using Backend.Post;
using Xunit;

namespace Backend.Tests;

public class GrblLaserPostProcessorTests
{
    private static readonly GrblLaserPostProcessor Post = new();

    private static Project MakeProject(double powerPct = 80)
        => new()
        {
            Table = { WidthMm = 1000, HeightMm = 800 },
            Cam = { OperationMode = MachineType.Laser, LaserPowerPercent = powerPct, FeedRateMmMin = 3000 },
        };

    private static Cut MakeCut(params Point2[] points) => new()
    {
        PartId = Guid.NewGuid(),
        SourcePathId = Guid.NewGuid(),
        Points = points.ToList(),
        IsClosedContour = true,
        PierceDelayS = 0,
        FeedRateMmMin = 3000,
    };

    private static List<string> Motion(GcodeProgram g) =>
        g.Lines.Where(l => !l.TrimStart().StartsWith(';')).ToList();

    [Fact]
    public void LaserOn_UsesSvalueFromPowerPercent()
    {
        // 80 % → S800
        var cut = MakeCut(new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject(80));
        var motion = Motion(g);

        Assert.Contains(motion, l => l.StartsWith("M3 S800"));
    }

    [Fact]
    public void NoPierceDelay_InGeneratedCode()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(50, 0), new Point2(0, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject());

        Assert.DoesNotContain(g.Lines, l => l.TrimStart().StartsWith("G4"));
    }

    [Fact]
    public void FullPower_ProducesS1000()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(10, 0), new Point2(0, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject(100));
        Assert.Contains(Motion(g), l => l.StartsWith("M3 S1000"));
    }

    [Fact]
    public void ZeroPower_ProducesS0()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(10, 0), new Point2(0, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject(0));
        Assert.Contains(Motion(g), l => l.StartsWith("M3 S0"));
    }

    [Fact]
    public void Program_StartsWithSafetyM5()
    {
        var toolpath = new Toolpath();
        var g = Post.Generate(toolpath, MakeProject());
        var motion = Motion(g);

        // First motion command must be M5 (laser off).
        Assert.Equal("M5", motion.First(l => l.StartsWith("G") || l.StartsWith("M")));
    }

    [Fact]
    public void EmptyToolpath_ProducesWarning()
    {
        var g = Post.Generate(new Toolpath(), MakeProject());
        Assert.NotEmpty(g.Warnings);
    }

    [Fact]
    public void LaserOff_AfterEachCut()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject());
        var motion = Motion(g);

        // Sequence must be: … M3 … G1 moves … M5 …
        var m3Idx = motion.FindIndex(l => l.StartsWith("M3"));
        var m5Idx = motion.FindIndex(m3Idx + 1, l => l == "M5");
        Assert.True(m5Idx > m3Idx, "M5 must follow M3");
    }
}
