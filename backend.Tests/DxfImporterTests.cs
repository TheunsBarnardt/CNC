using Backend.Import.Dxf;
using Backend.Models;
using netDxf;
using netDxf.Entities;
using netDxf.Units;

namespace Backend.Tests;

public class DxfImporterTests
{
    /// <summary>Builds a DXF in memory with netDxf and runs it through the importer.</summary>
    private static ImportedFile Import(Action<DxfDocument> build, DrawingUnits units = DrawingUnits.Millimeters)
    {
        var doc = new DxfDocument();
        doc.DrawingVariables.InsUnits = units;
        build(doc);

        using var stream = new MemoryStream();
        doc.Save(stream);
        stream.Position = 0;
        return new DxfImporter().Import(stream, "test.dxf");
    }

    [Fact]
    public void Line_ImportsAsOpenPath()
    {
        var file = Import(d => d.Entities.Add(new Line(new Vector2(0, 0), new Vector2(100, 0))));

        var path = Assert.Single(file.Paths);
        Assert.False(path.Polyline.IsClosed);
        Assert.Equal(100, path.Polyline.Length(), 3);
    }

    [Fact]
    public void Circle_ImportsClosedWithAccurateCircumference()
    {
        var file = Import(d => d.Entities.Add(new Circle(new Vector2(50, 50), 25)));

        var path = Assert.Single(file.Paths);
        Assert.True(path.Polyline.IsClosed);
        double expected = 2 * Math.PI * 25;
        Assert.Equal(expected, path.Polyline.Length(), expected * 0.005);
    }

    [Fact]
    public void Arc_ImportsOpenWithAccurateLength()
    {
        // Quarter arc r=40, 0°→90°.
        var file = Import(d => d.Entities.Add(new Arc(new Vector2(0, 0), 40, 0, 90)));

        var path = Assert.Single(file.Paths);
        Assert.False(path.Polyline.IsClosed);
        double expected = Math.PI * 40 / 2;
        Assert.Equal(expected, path.Polyline.Length(), expected * 0.005);
    }

    [Fact]
    public void PolylineWithBulge_ExpandsArcSegment()
    {
        // Two vertices joined by a semicircular bulge (bulge=1 → 180°), r=25.
        var file = Import(d =>
        {
            var pl = new Polyline2D(
            [
                new Polyline2DVertex(0, 0) { Bulge = 1 },
                new Polyline2DVertex(50, 0),
            ]);
            d.Entities.Add(pl);
        });

        var path = Assert.Single(file.Paths);
        double expected = Math.PI * 25; // semicircle length
        Assert.Equal(expected, path.Polyline.Length(), expected * 0.01);
        Assert.True(path.Polyline.Points.Count > 10, "bulge should expand to many segments");
    }

    [Fact]
    public void InchUnits_ScaleToMm()
    {
        var file = Import(
            d => d.Entities.Add(new Line(new Vector2(0, 0), new Vector2(1, 0))),
            DrawingUnits.Inches);

        Assert.Equal(25.4, Assert.Single(file.Paths).Polyline.Length(), 3);
    }

    [Fact]
    public void UnitlessDrawing_AssumesMmWithWarning()
    {
        var file = Import(
            d => d.Entities.Add(new Line(new Vector2(0, 0), new Vector2(10, 0))),
            DrawingUnits.Unitless);

        Assert.Equal(10, Assert.Single(file.Paths).Polyline.Length(), 3);
        Assert.Contains(file.Warnings, w => w.Contains("assuming millimeters"));
    }

    [Fact]
    public void LayerName_IsPreserved()
    {
        var file = Import(d =>
        {
            var line = new Line(new Vector2(0, 0), new Vector2(10, 0))
            {
                Layer = new netDxf.Tables.Layer("CUT"),
            };
            d.Entities.Add(line);
        });

        Assert.Equal("CUT", Assert.Single(file.Paths).Layer);
    }

    [Fact]
    public void Geometry_IsNormalizedToOrigin()
    {
        var file = Import(d => d.Entities.Add(new Line(new Vector2(500, 700), new Vector2(600, 700))));

        var (min, _) = Assert.Single(file.Paths).Polyline.Bounds();
        Assert.Equal(0, min.X, 6);
        Assert.Equal(0, min.Y, 6);
    }

    [Fact]
    public void GarbageContent_ThrowsFormatException()
    {
        using var stream = new MemoryStream("not a dxf at all"u8.ToArray());
        Assert.Throws<FormatException>(() => new DxfImporter().Import(stream, "junk.dxf"));
    }

    [Fact]
    public void ArcSegments_RespectsChordTolerance()
    {
        // r=100mm full circle at 0.05mm tolerance → sagitta error must hold.
        int n = DxfImporter.ArcSegments(100, 2 * Math.PI, 0.05);
        double step = 2 * Math.PI / n;
        double sagitta = 100 * (1 - Math.Cos(step / 2));
        Assert.True(sagitta <= 0.05, $"sagitta {sagitta} exceeds tolerance");
    }
}
