using Backend.Cam;
using Backend.Geometry;
using Backend.Models;
using Backend.Post;
using Xunit;

namespace Backend.Tests;

public class GrblPlasmaPostProcessorTests
{
    private static readonly GrblPlasmaPostProcessor Post = new();

    private static Project MakeProject(TableOrigin origin = TableOrigin.BottomLeft)
        => new() { Table = { WidthMm = 1000, HeightMm = 800, Origin = origin } };

    private static Cut MakeCut(params Point2[] points) => new()
    {
        PartId = Guid.NewGuid(),
        SourcePathId = Guid.NewGuid(),
        Points = points.ToList(),
        IsClosedContour = true,
        PierceDelayS = 0.5,
        FeedRateMmMin = 2000,
    };

    private static List<string> Motion(GcodeProgram g) =>
        g.Lines.Where(l => !l.TrimStart().StartsWith(';')).ToList();

    [Fact]
    public void EmptyToolpath_StillProducesValidProgram_WithWarning()
    {
        var g = Post.Generate(new Toolpath(), MakeProject());

        Assert.Contains(g.Warnings, w => w.Contains("empty"));
        var motion = Motion(g);
        // Preamble, safety M5, park, M2 — and no torch-on anywhere.
        Assert.Contains("G21 ; millimeters", motion);
        Assert.Contains("G90 ; absolute coordinates", motion);
        Assert.DoesNotContain(motion, l => l.StartsWith("M3"));
        Assert.Equal("M2 ; program end", motion[^1]);
    }

    [Fact]
    public void TorchIsOffBeforeAnyMotion()
    {
        var toolpath = new Toolpath { Cuts = { MakeCut(new(10, 10), new(20, 10)) } };
        var g = Post.Generate(toolpath, MakeProject());
        var motion = Motion(g);

        int firstMove = motion.FindIndex(l => l.StartsWith("G0 ") || l.StartsWith("G1 "));
        int safetyOff = motion.FindIndex(l => l.StartsWith("M5"));
        Assert.True(safetyOff >= 0 && safetyOff < firstMove,
            "M5 must appear before the first motion command");
    }

    [Fact]
    public void EachCut_RapidThenTorchOnThenDwellThenFeed_ThenTorchOff()
    {
        var toolpath = new Toolpath
        {
            Cuts = { MakeCut(new(10, 10), new(20, 10), new(20, 20)) },
        };
        var g = Post.Generate(toolpath, MakeProject());
        var motion = Motion(g);

        int rapid = motion.FindIndex(l => l.StartsWith("G0 X10 Y10"));
        Assert.True(rapid >= 0, "rapid to pierce point expected");
        Assert.StartsWith("M3", motion[rapid + 1]);
        Assert.StartsWith("G4 P0.5", motion[rapid + 2]);
        Assert.StartsWith("G1 X20 Y10 F2000", motion[rapid + 3]);
        Assert.StartsWith("G1 X20 Y20", motion[rapid + 4]);
        Assert.False(motion[rapid + 4].Contains('F'), "feed word only on the first cutting move");
        Assert.StartsWith("M5", motion[rapid + 5]);
    }

    [Fact]
    public void ZeroPierceDelay_OmitsDwell()
    {
        var cut = MakeCut(new(0, 0), new(5, 0));
        var toolpath = new Toolpath
        {
            Cuts =
            {
                new Cut
                {
                    PartId = cut.PartId,
                    SourcePathId = cut.SourcePathId,
                    Points = cut.Points,
                    PierceDelayS = 0,
                    FeedRateMmMin = 1500,
                },
            },
        };
        var g = Post.Generate(toolpath, MakeProject());
        Assert.DoesNotContain(g.Lines, l => l.StartsWith("G4"));
    }

    [Fact]
    public void TorchOnAndOffArePaired_OncePerCut()
    {
        var toolpath = new Toolpath
        {
            Cuts =
            {
                MakeCut(new(0, 0), new(10, 0)),
                MakeCut(new(50, 50), new(60, 50)),
                MakeCut(new(100, 0), new(110, 0)),
            },
        };
        var g = Post.Generate(toolpath, MakeProject());
        var motion = Motion(g);

        Assert.Equal(3, motion.Count(l => l.StartsWith("M3")));
        // 1 safety off + 3 per-cut offs.
        Assert.Equal(4, motion.Count(l => l.StartsWith("M5")));
        // Torch is never on across a rapid: every G0 must be preceded by state-off.
        bool torchOn = false;
        foreach (var line in motion)
        {
            if (line.StartsWith("M3")) torchOn = true;
            else if (line.StartsWith("M5")) torchOn = false;
            else if (line.StartsWith("G0")) Assert.False(torchOn, $"rapid with torch on: {line}");
        }
        Assert.False(torchOn, "program must end with the torch off");
    }

    [Theory]
    [InlineData(TableOrigin.BottomLeft, 100, 100)]
    [InlineData(TableOrigin.BottomRight, -900, 100)]
    [InlineData(TableOrigin.TopLeft, 100, -700)]
    [InlineData(TableOrigin.TopRight, -900, -700)]
    [InlineData(TableOrigin.Center, -400, -300)]
    public void WorkOrigin_ShiftsCoordinates(TableOrigin origin, double expectX, double expectY)
    {
        // A pierce at table point (100, 100) on a 1000x800 table.
        var toolpath = new Toolpath { Cuts = { MakeCut(new(100, 100), new(110, 100)) } };
        var g = Post.Generate(toolpath, MakeProject(origin));

        Assert.Contains(g.Lines, l => l.StartsWith(
            $"G0 X{expectX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} " +
            $"Y{expectY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"));
    }

    [Fact]
    public void Numbers_UseInvariantDecimalPoint()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A comma-decimal culture must not leak into G-code.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var toolpath = new Toolpath { Cuts = { MakeCut(new(1.25, 2.5), new(3.75, 2.5)) } };
            var g = Post.Generate(toolpath, MakeProject());

            Assert.Contains(g.Lines, l => l.StartsWith("G0 X1.25 Y2.5"));
            Assert.DoesNotContain(g.Lines, l => !l.TrimStart().StartsWith(';') && l.Contains(','));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void EndToEnd_ProjectThroughCamAndPost()
    {
        // A 100x100 square part placed at (50, 50): CAM classifies + offsets,
        // the post emits one full cut and parks at origin.
        var square = new Polyline2
        {
            IsClosed = true,
            Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)],
        };
        var file = new ImportedFile
        {
            FileName = "square.svg",
            DisplayName = "square",
            Paths = { new PathGeometry { Polyline = square } },
        };
        var project = MakeProject();
        project.Files.Add(file);
        project.Parts.Add(new Part { FileId = file.Id, X = 50, Y = 50 });

        var toolpath = CamEngine.Generate(project, project.Cam);
        var g = Post.Generate(toolpath, project);
        var motion = Motion(g);

        Assert.Single(toolpath.Cuts);
        Assert.Single(motion, l => l.StartsWith("M3"));
        Assert.Equal("M2 ; program end", motion[^1]);
        Assert.Equal("G0 X0 Y0 ; park at work origin", motion[^2]);
        // Every emitted coordinate stays within the table (plus kerf margin).
        foreach (var line in motion.Where(l => l.StartsWith("G0 ") || l.StartsWith("G1 ")))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double x = double.Parse(parts[1][1..], System.Globalization.CultureInfo.InvariantCulture);
            double y = double.Parse(parts[2][1..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(x, -5, 1005);
            Assert.InRange(y, -5, 805);
        }
    }
}
