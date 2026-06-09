using Backend.Geometry;
using Backend.Import.Svg;

namespace Backend.Tests;

public class SvgPathParserTests
{
    [Fact]
    public void CompactNumberSyntax_ParsesCorrectly()
    {
        // "1.5.5" = (1.5, .5); "1-2" = (1, -2) — SVG micro-syntax quirks.
        var subs = SvgPathParser.Parse("M1.5.5L1-2");
        var sub = Assert.Single(subs);
        Assert.Equal(new Point2(1.5, 0.5), sub.Start);
        var line = Assert.IsType<SvgSegment.Line>(Assert.Single(sub.Segments));
        Assert.Equal(new Point2(1, -2), line.To);
    }

    [Fact]
    public void PackedArcFlags_Parse()
    {
        // Arc flags may be packed without separators: "...0 0150 50" = large=0 sweep=0 x=150 y=50...
        // here: a25 25 -30 0 1 50 -25 written compactly.
        var subs = SvgPathParser.Parse("M100 100a25 25 -30 0150 -25");
        var sub = Assert.Single(subs);
        Assert.NotEmpty(sub.Segments);
        Assert.All(sub.Segments, s => Assert.IsType<SvgSegment.Cubic>(s));
    }

    [Fact]
    public void ImplicitLineToAfterMove_Works()
    {
        // "M 0 0 10 10 20 0" — pairs after M are implicit L.
        var subs = SvgPathParser.Parse("M 0 0 10 10 20 0");
        var sub = Assert.Single(subs);
        Assert.Equal(2, sub.Segments.Count);
        Assert.All(sub.Segments, s => Assert.IsType<SvgSegment.Line>(s));
    }

    [Fact]
    public void MultipleSubpaths_AreSeparated()
    {
        var subs = SvgPathParser.Parse("M0 0 L10 0 Z M20 20 L30 20");
        Assert.Equal(2, subs.Count);
        Assert.True(subs[0].Closed);
        Assert.False(subs[1].Closed);
    }

    [Fact]
    public void SmoothCubic_ReflectsControlPoint()
    {
        var subs = SvgPathParser.Parse("M0 0 C 0 10 10 10 10 0 S 20 -10 20 0");
        var sub = Assert.Single(subs);
        Assert.Equal(2, sub.Segments.Count);
        var second = Assert.IsType<SvgSegment.Cubic>(sub.Segments[1]);
        // Reflection of (10,10) around (10,0) → (10,-10).
        Assert.Equal(new Point2(10, -10), second.C1);
    }

    [Fact]
    public void HorizontalAndVertical_TrackCurrentPoint()
    {
        var subs = SvgPathParser.Parse("M5 5 H 15 V 25 h -10 v -20");
        var sub = Assert.Single(subs);
        var ends = sub.Segments.Cast<SvgSegment.Line>().Select(l => l.To).ToList();
        Assert.Equal(new Point2(15, 5), ends[0]);
        Assert.Equal(new Point2(15, 25), ends[1]);
        Assert.Equal(new Point2(5, 25), ends[2]);
        Assert.Equal(new Point2(5, 5), ends[3]);
    }

    [Fact]
    public void PathNotStartingWithMove_Throws()
    {
        Assert.Throws<FormatException>(() => SvgPathParser.Parse("L 10 10"));
    }
}
