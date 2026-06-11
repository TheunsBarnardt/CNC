using Backend.Cam;
using Backend.Geometry;
using Backend.Models;
using Backend.Post;
using Xunit;

namespace Backend.Tests;

public class GrblVinylPostProcessorTests
{
    private static readonly GrblVinylPostProcessor Post = new();

    private static Project MakeProject(double bladeOffset = 1, double overcut = 1)
        => new()
        {
            Table = { WidthMm = 1000, HeightMm = 800 },
            Cam =
            {
                OperationMode = MachineType.VinylKnife,
                FeedRateMmMin = 2000,
                VinylBladeOffsetMm = bladeOffset,
                VinylOvercutMm = overcut,
                VinylKnifeUpMm = 3,
                VinylKnifeDownMm = 0,
            },
        };

    private static Cut MakeCut(params Point2[] points) => new()
    {
        PartId = Guid.NewGuid(),
        SourcePathId = Guid.NewGuid(),
        Points = points.ToList(),
        IsClosedContour = false,
        PierceDelayS = 0,
        FeedRateMmMin = 2000,
    };

    private static List<string> Motion(GcodeProgram g) =>
        g.Lines.Where(l => !l.TrimStart().StartsWith(';')).ToList();

    [Fact]
    public void Program_StartsWithKnifeUp()
    {
        var g = Post.Generate(new Toolpath(), MakeProject());
        var motion = Motion(g);
        // First motion command involving Z must be Z up.
        var firstZ = motion.First(l => l.Contains('Z'));
        Assert.Contains("Z3", firstZ);
    }

    [Fact]
    public void CutSequence_RapidThenDownThenCutThenUp()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(50, 0), new Point2(50, 50));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject());
        var motion = Motion(g);

        // Find rapid to start (G0 with X/Y but no Z), then Z down, then G1, then Z up.
        int g0StartIdx = motion.FindIndex(l => l.StartsWith("G0") && l.Contains('X') && !l.Contains('Z'));
        Assert.True(g0StartIdx >= 0, "Rapid to start not found");

        int zDownIdx = motion.FindIndex(g0StartIdx, l => l.StartsWith("G0") && l.Contains('Z') && l.Contains("Z0"));
        Assert.True(zDownIdx > g0StartIdx, "Knife down (Z0) must follow rapid to start");

        int g1Idx = motion.FindIndex(zDownIdx, l => l.StartsWith("G1"));
        Assert.True(g1Idx > zDownIdx, "G1 cut must follow knife down");

        int zUpIdx = motion.FindIndex(g1Idx, l => l.StartsWith("G0") && l.Contains("Z3"));
        Assert.True(zUpIdx > g1Idx, "Knife up (Z3) must follow G1 cuts");
    }

    [Fact]
    public void NoPierceLine_G4AbsentFromOutput()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(100, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject());
        Assert.DoesNotContain(g.Lines, l => l.TrimStart().StartsWith("G4"));
    }

    [Fact]
    public void NoSpindleCommands_M3M5Absent()
    {
        var cut = MakeCut(new Point2(0, 0), new Point2(100, 0));
        var toolpath = new Toolpath();
        toolpath.Cuts.Add(cut);

        var g = Post.Generate(toolpath, MakeProject());
        Assert.DoesNotContain(g.Lines, l => l.TrimStart().StartsWith("M3"));
        Assert.DoesNotContain(g.Lines, l => l.TrimStart().StartsWith("M5"));
    }

    [Fact]
    public void EmptyToolpath_ProducesWarning()
    {
        var g = Post.Generate(new Toolpath(), MakeProject());
        Assert.NotEmpty(g.Warnings);
    }

    [Fact]
    public void MultipleCuts_KnifeUpBetweenCuts()
    {
        var t = new Toolpath();
        t.Cuts.Add(MakeCut(new Point2(0, 0), new Point2(50, 0)));
        t.Cuts.Add(MakeCut(new Point2(100, 0), new Point2(150, 0)));

        var g = Post.Generate(t, MakeProject());
        var motion = Motion(g);

        // After the first cut, expect Z up before the next G0 rapid.
        int firstG1Idx = motion.FindIndex(l => l.StartsWith("G1"));
        int zUpAfterFirstCut = motion.FindIndex(firstG1Idx, l => l.StartsWith("G0") && l.Contains("Z3"));
        int secondRapidIdx = motion.FindIndex(zUpAfterFirstCut + 1, l =>
            l.StartsWith("G0") && l.Contains('X') && !l.Contains('Z'));

        Assert.True(zUpAfterFirstCut > firstG1Idx, "Knife up missing after first cut");
        Assert.True(secondRapidIdx > zUpAfterFirstCut, "Rapid to second cut must follow knife up");
    }
}
