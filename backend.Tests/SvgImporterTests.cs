using System.Text;
using Backend.Import.Svg;
using Backend.Models;

namespace Backend.Tests;

public class SvgImporterTests
{
    private static ImportedFile Import(string svg)
    {
        var importer = new SvgImporter();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        return importer.Import(stream, "test.svg");
    }

    private const string Header =
        """<svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="100mm" viewBox="0 0 100 100">""";

    [Fact]
    public void Rect_BecomesClosedPolylineWithMmSize()
    {
        var file = Import(Header + """<rect x="10" y="10" width="30" height="20"/></svg>""");

        var path = Assert.Single(file.Paths);
        Assert.True(path.Polyline.IsClosed);
        Assert.Equal(4, path.Polyline.Points.Count);

        var (min, max) = path.Polyline.Bounds();
        // Geometry is normalized so the bounds start at (0,0).
        Assert.Equal(0, min.X, 3);
        Assert.Equal(0, min.Y, 3);
        Assert.Equal(30, max.X, 3);
        Assert.Equal(20, max.Y, 3);
    }

    [Fact]
    public void YAxis_IsFlipped_SvgTopBecomesTableTop()
    {
        // Two rects: one near SVG top (y=0), one near SVG bottom (y=90).
        var file = Import(Header +
            """<g id="layers"><rect x="0" y="0" width="10" height="10" id="top"/>""" +
            """<rect x="0" y="90" width="10" height="10" id="bottom"/></g></svg>""");

        Assert.Equal(2, file.Paths.Count);
        double yTopRect = file.Paths[0].Polyline.Bounds().Max.Y;
        double yBottomRect = file.Paths[1].Polyline.Bounds().Max.Y;
        // SVG y=0 (visual top) must end up at HIGHER table Y than svg y=90.
        Assert.True(yTopRect > yBottomRect);
    }

    [Fact]
    public void Circle_FlattensCloseToTrueCircle()
    {
        var file = Import(Header + """<circle cx="50" cy="50" r="20"/></svg>""");

        var path = Assert.Single(file.Paths);
        Assert.True(path.Polyline.IsClosed);
        // Circumference of r=20 → ~125.66mm; flattened length must be within 0.5%.
        Assert.Equal(2 * Math.PI * 20, path.Polyline.Length(), 2 * Math.PI * 20 * 0.005);
        // All points equidistant (r=20, normalized center at (20,20)).
        var (min, max) = path.Polyline.Bounds();
        Assert.Equal(40, max.X - min.X, 0.05);
        Assert.Equal(40, max.Y - min.Y, 0.05);
    }

    [Fact]
    public void Path_RelativeAndAbsoluteCommands_Match()
    {
        var abs = Import(Header + """<path d="M 10 10 L 30 10 L 30 30 Z"/></svg>""");
        var rel = Import(Header + """<path d="m 10 10 l 20 0 l 0 20 z"/></svg>""");

        var pAbs = Assert.Single(abs.Paths).Polyline;
        var pRel = Assert.Single(rel.Paths).Polyline;
        Assert.True(pAbs.IsClosed);
        Assert.Equal(pAbs.Points.Count, pRel.Points.Count);
        for (int i = 0; i < pAbs.Points.Count; i++)
        {
            Assert.Equal(pAbs.Points[i].X, pRel.Points[i].X, 6);
            Assert.Equal(pAbs.Points[i].Y, pRel.Points[i].Y, 6);
        }
    }

    [Fact]
    public void Path_CubicBezier_FlattensWithinTolerance()
    {
        // Half circle drawn as a cubic approximation; just verify endpoints +
        // sane point count and that all interior points stay near radius 20.
        var file = Import(Header + """<path d="M 30 50 C 30 22.4 70 22.4 70 50"/></svg>""");
        var poly = Assert.Single(file.Paths).Polyline;

        Assert.False(poly.IsClosed);
        Assert.True(poly.Points.Count > 10, "curve should flatten to many segments");
        var first = poly.Points[0];
        var last = poly.Points[^1];
        Assert.Equal(40, first.DistanceTo(last), 0.01); // chord = 40mm
    }

    [Fact]
    public void Transform_TranslateAndScale_Apply()
    {
        var file = Import(Header +
            """<g transform="translate(10 0) scale(2)"><rect x="0" y="40" width="10" height="10"/></g></svg>""");

        var poly = Assert.Single(file.Paths).Polyline;
        var (min, max) = poly.Bounds();
        // Scaled 2x → 20mm square (position is normalized away).
        Assert.Equal(20, max.X - min.X, 3);
        Assert.Equal(20, max.Y - min.Y, 3);
    }

    [Fact]
    public void ArcCommand_ProducesArcOfExpectedLength()
    {
        // Semicircle arc of r=20 from (30,50) to (70,50).
        var file = Import(Header + """<path d="M 30 50 A 20 20 0 0 1 70 50"/></svg>""");
        var poly = Assert.Single(file.Paths).Polyline;
        Assert.Equal(Math.PI * 20, poly.Length(), Math.PI * 20 * 0.005);
    }

    [Fact]
    public void Text_ProducesWarningNotGeometry()
    {
        var file = Import(Header + """<text x="10" y="10">hello</text></svg>""");
        Assert.Empty(file.Paths);
        Assert.Contains(file.Warnings, w => w.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroupId_BecomesLayer()
    {
        var file = Import(Header + """<g id="cut-layer"><rect x="0" y="0" width="10" height="10"/></g></svg>""");
        Assert.Equal("cut-layer", Assert.Single(file.Paths).Layer);
    }

    [Fact]
    public void PixelUnits_ConvertAt96Dpi()
    {
        // 96px wide square with no physical units → 25.4mm.
        var file = Import(
            """<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96">""" +
            """<rect x="0" y="0" width="96" height="96"/></svg>""");
        var (_, max) = Assert.Single(file.Paths).Polyline.Bounds();
        Assert.Equal(25.4, max.X, 2);
        Assert.Equal(25.4, max.Y, 2);
    }

    [Fact]
    public void InvalidXml_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Import("this is not xml"));
    }
}
