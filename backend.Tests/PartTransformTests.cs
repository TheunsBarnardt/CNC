using Backend.Geometry;
using Backend.Models;

namespace Backend.Tests;

public class PartTransformTests
{
    /// <summary>A 20×10 rectangle at local (0,0)..(20,10).</summary>
    private static ImportedFile RectFile() => new()
    {
        FileName = "rect.svg",
        DisplayName = "rect",
        Paths =
        {
            new PathGeometry
            {
                Polyline = new Polyline2
                {
                    IsClosed = true,
                    Points =
                    {
                        new Point2(0, 0), new Point2(20, 0),
                        new Point2(20, 10), new Point2(0, 10),
                    },
                },
            },
        },
    };

    [Fact]
    public void TranslationOnly_ShiftsBounds()
    {
        var part = new Part { FileId = Guid.NewGuid(), X = 100, Y = 50 };
        var (min, max) = PartTransform.WorldBounds(part, RectFile());
        Assert.Equal(new Point2(100, 50), min);
        Assert.Equal(new Point2(120, 60), max);
    }

    [Fact]
    public void Rotation_IsAboutBoundingBoxCenter()
    {
        // 90° about center (10,5): a 20×10 rect becomes 10×20 with the SAME center.
        var part = new Part { FileId = Guid.NewGuid(), RotationDeg = 90 };
        var (min, max) = PartTransform.WorldBounds(part, RectFile());

        Assert.Equal(10, (min.X + max.X) / 2, 9); // center preserved
        Assert.Equal(5, (min.Y + max.Y) / 2, 9);
        Assert.Equal(10, max.X - min.X, 9); // dimensions swapped
        Assert.Equal(20, max.Y - min.Y, 9);
    }

    [Fact]
    public void TranslationAndRotation_AreIndependent()
    {
        // Rotating must not change where the part's center sits (spin in place).
        var still = new Part { FileId = Guid.NewGuid(), X = 30, Y = 40 };
        var spun = new Part { FileId = Guid.NewGuid(), X = 30, Y = 40, RotationDeg = 137 };

        var file = RectFile();
        var (minA, maxA) = PartTransform.WorldBounds(still, file);
        var (minB, maxB) = PartTransform.WorldBounds(spun, file);

        Assert.Equal((minA.X + maxA.X) / 2, (minB.X + maxB.X) / 2, 9);
        Assert.Equal((minA.Y + maxA.Y) / 2, (minB.Y + maxB.Y) / 2, 9);
    }

    [Fact]
    public void Apply_RotatesPointCcw()
    {
        // Pivot (0,0), 90° CCW: (1,0) → (0,1).
        var part = new Part { FileId = Guid.NewGuid(), RotationDeg = 90 };
        var p = PartTransform.Apply(part, new Point2(0, 0), new Point2(1, 0));
        Assert.Equal(0, p.X, 9);
        Assert.Equal(1, p.Y, 9);
    }

    [Fact]
    public void PlaceNew_DoesNotOverlapExistingPart()
    {
        var project = new Project();
        var file = RectFile();
        project.Files.Add(file);

        var first = PartPlacer.PlaceNew(project, file);
        project.Parts.Add(first);
        var second = PartPlacer.PlaceNew(project, file);

        var (minA, maxA) = PartTransform.WorldBounds(first, file);
        var (minB, maxB) = PartTransform.WorldBounds(second, file);
        bool overlap = minA.X < maxB.X && minB.X < maxA.X && minA.Y < maxB.Y && minB.Y < maxA.Y;
        Assert.False(overlap, "second part must not overlap the first");
    }

    [Fact]
    public void PlaceNew_WrapsToNewRowWhenTableWidthExceeded()
    {
        var project = new Project();
        project.Table.WidthMm = 60; // fits two 20mm rects + gaps, not three
        var file = RectFile();
        project.Files.Add(file);

        for (int i = 0; i < 2; i++)
            project.Parts.Add(PartPlacer.PlaceNew(project, file));
        var third = PartPlacer.PlaceNew(project, file);

        Assert.Equal(10, third.X, 6); // back at left margin
        Assert.True(third.Y > 10, "third part should start a new row");
    }
}
