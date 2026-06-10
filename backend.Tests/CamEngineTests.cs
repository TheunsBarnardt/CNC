using Backend.Cam;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Tests;

/// <summary>Helpers to build projects with known geometry.</summary>
internal static class CamTestData
{
    public static Polyline2 Square(double x, double y, double size) => new()
    {
        IsClosed = true,
        Points =
        {
            new Point2(x, y), new Point2(x + size, y),
            new Point2(x + size, y + size), new Point2(x, y + size),
        },
    };

    public static Polyline2 OpenLine(double x0, double y0, double x1, double y1) => new()
    {
        IsClosed = false,
        Points = { new Point2(x0, y0), new Point2(x1, y1) },
    };

    /// <summary>One file containing the given local paths, placed at a part offset.</summary>
    public static Project ProjectWith(double partX, double partY, params Polyline2[] paths)
    {
        var file = new ImportedFile { FileName = "t.svg", DisplayName = "t" };
        foreach (var p in paths) file.Paths.Add(new PathGeometry { Polyline = p });
        var project = new Project();
        project.Files.Add(file);
        project.Parts.Add(new Part { FileId = file.Id, X = partX, Y = partY });
        return project;
    }

    public static CamSettings NoLeads(double kerf = 2) => new()
    {
        KerfWidthMm = kerf,
        LeadInType = LeadType.None,
        LeadOutType = LeadType.None,
    };
}

public class KerfOffsetterTests
{
    [Fact]
    public void OutsideOffset_GrowsBoundsByHalfKerf()
    {
        var offset = KerfOffsetter.OffsetClosed(CamTestData.Square(0, 0, 100), +1.0);
        var contour = Assert.Single(offset);
        var (min, max) = contour.Bounds();
        Assert.Equal(-1, min.X, 2);
        Assert.Equal(-1, min.Y, 2);
        Assert.Equal(101, max.X, 2);
        Assert.Equal(101, max.Y, 2);
    }

    [Fact]
    public void InsideOffset_ShrinksBoundsByHalfKerf()
    {
        var offset = KerfOffsetter.OffsetClosed(CamTestData.Square(0, 0, 100), -1.0);
        var contour = Assert.Single(offset);
        var (min, max) = contour.Bounds();
        Assert.Equal(1, min.X, 2);
        Assert.Equal(99, max.X, 2);
    }

    [Fact]
    public void HoleSmallerThanKerf_Vanishes()
    {
        // 1.5mm square hole, 1mm inward offset per side → nothing left.
        var offset = KerfOffsetter.OffsetClosed(CamTestData.Square(0, 0, 1.5), -1.0);
        Assert.Empty(offset);
    }

    [Fact]
    public void NormalizeDirection_OutsideCcw_InsideCw()
    {
        var ccw = CamTestData.Square(0, 0, 10); // built CCW
        KerfOffsetter.NormalizeDirection(ccw, CutSide.Outside);
        Assert.True(KerfOffsetter.SignedArea(ccw) > 0);

        var hole = CamTestData.Square(0, 0, 10);
        KerfOffsetter.NormalizeDirection(hole, CutSide.Inside);
        Assert.True(KerfOffsetter.SignedArea(hole) < 0);
    }
}

public class ContourClassifierTests
{
    private static WorldPath Wp(Polyline2 p) => new()
    {
        PartId = Guid.NewGuid(),
        SourcePathId = Guid.NewGuid(),
        Polyline = p,
    };

    [Fact]
    public void SquareWithHole_ClassifiesOuterAndInner()
    {
        var paths = new List<WorldPath>
        {
            Wp(CamTestData.Square(0, 0, 100)),
            Wp(CamTestData.Square(30, 30, 20)),
        };
        ContourClassifier.Classify(paths);

        Assert.Equal(CutSide.Outside, paths[0].Side);
        Assert.Equal(CutSide.Inside, paths[1].Side);
        Assert.Equal([1], paths[0].Children);
    }

    [Fact]
    public void TripleNesting_AlternatesSides()
    {
        // Ring-in-ring: outer (Outside), hole (Inside), island inside hole (Outside).
        var paths = new List<WorldPath>
        {
            Wp(CamTestData.Square(0, 0, 100)),
            Wp(CamTestData.Square(20, 20, 60)),
            Wp(CamTestData.Square(40, 40, 20)),
        };
        ContourClassifier.Classify(paths);
        Assert.Equal(CutSide.Outside, paths[0].Side);
        Assert.Equal(CutSide.Inside, paths[1].Side);
        Assert.Equal(CutSide.Outside, paths[2].Side);
    }

    [Fact]
    public void OpenPath_IsOnLine()
    {
        var paths = new List<WorldPath>
        {
            Wp(CamTestData.Square(0, 0, 100)),
            Wp(CamTestData.OpenLine(10, 10, 50, 50)),
        };
        ContourClassifier.Classify(paths);
        Assert.Equal(CutSide.OnLine, paths[1].Side);
    }
}

public class LeadBuilderTests
{
    [Fact]
    public void LineLeadIn_StartsLeadLengthFromPierce_OnWasteSide()
    {
        var contour = CamTestData.Square(0, 0, 100); // CCW outer
        KerfOffsetter.NormalizeDirection(contour, CutSide.Outside);
        var settings = new CamSettings
        {
            LeadInType = LeadType.Line,
            LeadInLengthMm = 5,
            LeadOutType = LeadType.None,
        };

        var (points, leadIn, leadOut) = LeadBuilder.Build(contour, settings);
        Assert.Equal(1, leadIn);
        Assert.Equal(0, leadOut);

        var pierce = points[leadIn];
        Assert.Equal(5, points[0].DistanceTo(pierce), 6);
        // Waste side for an outer CCW profile = outside the polygon.
        Assert.False(ContourClassifier.ContainsPoint(contour, points[0]));
    }

    [Fact]
    public void ArcLeadIn_EndsAtPierce_WithCorrectArcLength()
    {
        var contour = CamTestData.Square(0, 0, 100);
        KerfOffsetter.NormalizeDirection(contour, CutSide.Outside);
        var settings = new CamSettings
        {
            LeadInType = LeadType.Arc,
            LeadInLengthMm = 4, // quarter arc radius 4 → length 2π·4/4 ≈ 6.28
            LeadOutType = LeadType.None,
        };

        var (points, leadIn, _) = LeadBuilder.Build(contour, settings);
        Assert.True(leadIn > 1);
        double arcLen = 0;
        for (int i = 1; i <= leadIn; i++) arcLen += points[i - 1].DistanceTo(points[i]);
        Assert.Equal(Math.PI / 2 * 4, arcLen, Math.PI / 2 * 4 * 0.01);
        // Every lead point sits outside the part for an outer cut.
        for (int i = 0; i < leadIn; i++)
            Assert.False(ContourClassifier.ContainsPoint(contour, points[i]));
    }

    [Fact]
    public void HoleLead_StaysInsideTheHole()
    {
        // For an Inside (hole) cut, waste = hole interior — the pierce divot
        // must land inside the hole, never in the surrounding part.
        var hole = CamTestData.Square(0, 0, 40);
        KerfOffsetter.NormalizeDirection(hole, CutSide.Inside);
        var settings = new CamSettings
        {
            LeadInType = LeadType.Line,
            LeadInLengthMm = 5,
            LeadOutType = LeadType.None,
        };

        var (points, leadIn, _) = LeadBuilder.Build(hole, settings);
        Assert.True(ContourClassifier.ContainsPoint(hole, points[0]),
            "lead-in start must be inside the hole (waste region)");
        _ = leadIn;
    }

    [Fact]
    public void ClosedLoop_ReturnsToPierce()
    {
        var contour = CamTestData.Square(0, 0, 100);
        var (points, leadIn, leadOut) = LeadBuilder.Build(contour, CamTestData.NoLeads());
        Assert.Equal(0, leadIn);
        Assert.Equal(0, leadOut);
        Assert.Equal(points[0].X, points[^1].X, 9);
        Assert.Equal(points[0].Y, points[^1].Y, 9);
        // Perimeter preserved.
        double len = 0;
        for (int i = 1; i < points.Count; i++) len += points[i - 1].DistanceTo(points[i]);
        Assert.Equal(400, len, 6);
    }
}

public class CutOrdererTests
{
    private static WorldPath Wp(Polyline2 p) => new()
    {
        PartId = Guid.NewGuid(),
        SourcePathId = Guid.NewGuid(),
        Polyline = p,
    };

    [Fact]
    public void HolesAreCutBeforeTheirOuterContour()
    {
        var paths = new List<WorldPath>
        {
            Wp(CamTestData.Square(0, 0, 100)),   // outer — starts nearest to origin!
            Wp(CamTestData.Square(60, 60, 20)),  // hole, farther away
        };
        ContourClassifier.Classify(paths);
        var order = CutOrderer.Order(paths, new Point2(0, 0));

        Assert.Equal([1, 0], order); // hole first despite being farther
    }

    [Fact]
    public void UnrelatedCuts_NearestFirst()
    {
        var paths = new List<WorldPath>
        {
            Wp(CamTestData.Square(500, 500, 10)),
            Wp(CamTestData.Square(10, 10, 10)),
            Wp(CamTestData.Square(200, 200, 10)),
        };
        ContourClassifier.Classify(paths);
        var order = CutOrderer.Order(paths, new Point2(0, 0));

        Assert.Equal([1, 2, 0], order);
    }
}

public class CamEngineEndToEndTests
{
    [Fact]
    public void PlateWithHole_ProducesOrderedCompensatedToolpath()
    {
        // 100mm plate with a 20mm hole, part placed at (50, 50).
        var project = CamTestData.ProjectWith(
            50, 50,
            CamTestData.Square(0, 0, 100),
            CamTestData.Square(40, 40, 20));
        var toolpath = CamEngine.Generate(project, CamTestData.NoLeads(kerf: 2));

        Assert.Equal(2, toolpath.Cuts.Count);
        Assert.Empty(toolpath.Warnings);

        // Hole first, inner side; outer second.
        Assert.Equal(CutSide.Inside, toolpath.Cuts[0].Side);
        Assert.Equal(CutSide.Outside, toolpath.Cuts[1].Side);

        // Hole shrinks: perimeter < 80; outer grows: perimeter > 400.
        Assert.True(toolpath.Cuts[0].CutLengthMm() < 80);
        Assert.True(toolpath.Cuts[1].CutLengthMm() > 400);

        // Everything in world coordinates (offset by the part placement).
        var (min, _) = new Polyline2 { Points = toolpath.Cuts[1].Points }.Bounds();
        Assert.True(min.X >= 48 && min.X <= 50, $"outer cut minX {min.X} should sit ~49 (50 − kerf/2)");
    }

    [Fact]
    public void HiddenFile_IsExcluded()
    {
        var project = CamTestData.ProjectWith(0, 0, CamTestData.Square(0, 0, 50));
        project.Files[0].Visible = false;
        var toolpath = CamEngine.Generate(project, CamTestData.NoLeads());
        Assert.Empty(toolpath.Cuts);
        Assert.NotEmpty(toolpath.Warnings);
    }

    [Fact]
    public void TinyHole_ProducesWarningButOtherCutsSurvive()
    {
        var project = CamTestData.ProjectWith(
            0, 0,
            CamTestData.Square(0, 0, 100),
            CamTestData.Square(50, 50, 1)); // 1mm hole vs 2mm kerf → gone
        var toolpath = CamEngine.Generate(project, CamTestData.NoLeads(kerf: 2));

        Assert.Single(toolpath.Cuts);
        Assert.Contains(toolpath.Warnings, w => w.Contains("too small"));
    }

    [Fact]
    public void RotatedPart_CutsFollowTheRotation()
    {
        var project = CamTestData.ProjectWith(0, 0, CamTestData.Square(0, 0, 100));
        project.Parts[0].RotationDeg = 45;
        var toolpath = CamEngine.Generate(project, CamTestData.NoLeads(kerf: 0));

        // A 100mm square rotated 45° spans ~141.4mm; centered on (50,50).
        var (min, max) = new Polyline2 { Points = toolpath.Cuts[0].Points }.Bounds();
        Assert.Equal(100 * Math.Sqrt(2), max.X - min.X, 0.1);
        Assert.Equal(100 * Math.Sqrt(2), max.Y - min.Y, 0.1);
    }

    [Fact]
    public void OpenPath_IsCutOnLineWithoutLeads()
    {
        var project = CamTestData.ProjectWith(0, 0, CamTestData.OpenLine(0, 0, 100, 0));
        var settings = new CamSettings { LeadInType = LeadType.Arc, LeadOutType = LeadType.Line };
        var toolpath = CamEngine.Generate(project, settings);

        var cut = Assert.Single(toolpath.Cuts);
        Assert.Equal(CutSide.OnLine, cut.Side);
        Assert.Equal(0, cut.LeadInPointCount);
        Assert.Equal(100, cut.CutLengthMm(), 2);
    }
}
