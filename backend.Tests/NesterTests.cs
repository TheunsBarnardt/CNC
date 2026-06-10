using Backend.Geometry;
using Backend.Models;
using Backend.Nest;

namespace Backend.Tests;

public class NesterTests
{
    /// <summary>Project with one file per size, one part per file.</summary>
    private static Project ProjectWithRects(
        double tableW, double tableH, params (double W, double H)[] sizes)
    {
        var project = new Project();
        project.Table.WidthMm = tableW;
        project.Table.HeightMm = tableH;
        foreach (var (w, h) in sizes)
        {
            var file = new ImportedFile { FileName = $"r{w}x{h}.svg", DisplayName = $"r{w}x{h}" };
            file.Paths.Add(new PathGeometry
            {
                Polyline = new Polyline2
                {
                    IsClosed = true,
                    Points =
                    {
                        new Point2(0, 0), new Point2(w, 0),
                        new Point2(w, h), new Point2(0, h),
                    },
                },
            });
            project.Files.Add(file);
            // Deliberately awful starting placement: everything stacked at 500,500.
            project.Parts.Add(new Part { FileId = file.Id, X = 500, Y = 500 });
        }
        return project;
    }

    private static (Point2 Min, Point2 Max) BoundsOf(Project p, Part part)
    {
        var file = p.Files.First(f => f.Id == part.FileId);
        return PartTransform.WorldBounds(part, file);
    }

    [Fact]
    public void PlacesAllPartsInsideMargin()
    {
        var p = ProjectWithRects(1000, 1000, (100, 50), (200, 80), (60, 60));
        var outcome = Nester.Nest(p, new NestSettings { MarginMm = 20, SpacingMm = 5 });

        Assert.Equal(3, outcome.PlacedCount);
        Assert.Empty(outcome.Warnings);
        foreach (var part in p.Parts)
        {
            var (min, max) = BoundsOf(p, part);
            Assert.True(min.X >= 20 - 1e-6, $"minX {min.X}");
            Assert.True(min.Y >= 20 - 1e-6, $"minY {min.Y}");
            Assert.True(max.X <= 980 + 1e-6, $"maxX {max.X}");
            Assert.True(max.Y <= 980 + 1e-6, $"maxY {max.Y}");
        }
    }

    [Fact]
    public void RespectsSpacingBetweenParts()
    {
        var p = ProjectWithRects(1000, 1000, (100, 100), (100, 100));
        var outcome = Nester.Nest(p, new NestSettings { MarginMm = 10, SpacingMm = 8 });

        Assert.Equal(2, outcome.PlacedCount);
        var (minA, maxA) = BoundsOf(p, p.Parts[0]);
        var (minB, maxB) = BoundsOf(p, p.Parts[1]);
        // Axis-aligned rectangles: gap = max per-axis separation.
        double gapX = Math.Max(minB.X - maxA.X, minA.X - maxB.X);
        double gapY = Math.Max(minB.Y - maxA.Y, minA.Y - maxB.Y);
        Assert.True(Math.Max(gapX, gapY) >= 8 - 1e-6,
            $"parts closer than spacing: gapX {gapX}, gapY {gapY}");
    }

    [Fact]
    public void PartsDoNotOverlap()
    {
        var p = ProjectWithRects(500, 500, (150, 100), (150, 100), (150, 100), (100, 100));
        var outcome = Nester.Nest(p, new NestSettings { MarginMm = 5, SpacingMm = 4 });

        Assert.Equal(4, outcome.PlacedCount);
        for (int i = 0; i < p.Parts.Count; i++)
        {
            for (int j = i + 1; j < p.Parts.Count; j++)
            {
                var (minA, maxA) = BoundsOf(p, p.Parts[i]);
                var (minB, maxB) = BoundsOf(p, p.Parts[j]);
                bool overlap = minA.X < maxB.X - 1e-6 && maxA.X > minB.X + 1e-6 &&
                               minA.Y < maxB.Y - 1e-6 && maxA.Y > minB.Y + 1e-6;
                Assert.False(overlap, $"parts {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void RotatesTallPartToFitWideSheet()
    {
        // 300x80 sheet, 60x200 part: only fits rotated 90°.
        var p = ProjectWithRects(300, 80, (60, 200));
        var outcome = Nester.Nest(p, new NestSettings
        {
            MarginMm = 5,
            SpacingMm = 5,
            RotationStepDeg = 90,
        });

        Assert.Equal(1, outcome.PlacedCount);
        Assert.Empty(outcome.Warnings);
        var (min, max) = BoundsOf(p, p.Parts[0]);
        Assert.True(max.X - min.X > max.Y - min.Y, "part should lie on its side");
        Assert.True(max.X <= 295 + 1e-6 && max.Y <= 75 + 1e-6);
    }

    [Fact]
    public void PartTooBigForSheet_WarnsAndLeavesTransform()
    {
        var p = ProjectWithRects(100, 100, (200, 200));
        var outcome = Nester.Nest(p, new NestSettings { MarginMm = 10, SpacingMm = 5 });

        Assert.Equal(0, outcome.PlacedCount);
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Contains(outcome.Warnings, w => w.Contains("did not fit"));
        Assert.Equal(500, p.Parts[0].X); // untouched
        Assert.Equal(500, p.Parts[0].Y);
    }

    [Fact]
    public void MarginLargerThanSheet_WarnsWithoutNesting()
    {
        var p = ProjectWithRects(100, 100, (20, 20));
        var outcome = Nester.Nest(p, new NestSettings { MarginMm = 60, SpacingMm = 5 });

        Assert.Equal(0, outcome.PlacedCount);
        Assert.Single(outcome.Warnings);
        Assert.Equal(500, p.Parts[0].X);
    }

    [Fact]
    public void IsDeterministic()
    {
        var run = () =>
        {
            var p = ProjectWithRects(800, 600, (120, 40), (90, 90), (200, 30), (50, 70));
            Nester.Nest(p, new NestSettings { MarginMm = 10, SpacingMm = 5 });
            return p.Parts.Select(x => (x.X, x.Y, x.RotationDeg)).ToList();
        };
        Assert.Equal(run(), run());
    }

    [Fact]
    public void LargestPartIsPlacedFirst_AtBottomLeft()
    {
        var p = ProjectWithRects(1000, 1000, (50, 50), (300, 300));
        Nester.Nest(p, new NestSettings { MarginMm = 10, SpacingMm = 5 });

        // The 300x300 (parts[1]) should own the bottom-left corner.
        var (minBig, _) = BoundsOf(p, p.Parts[1]);
        Assert.Equal(10, minBig.X, 6);
        Assert.Equal(10, minBig.Y, 6);
    }
}
