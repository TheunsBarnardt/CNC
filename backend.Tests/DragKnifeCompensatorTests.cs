using Backend.Cam;
using Backend.Geometry;
using Xunit;

namespace Backend.Tests;

public class DragKnifeCompensatorTests
{
    // Tolerance for floating-point comparisons (mm).
    private const double Eps = 1e-6;

    private static Polyline2 Open(params (double X, double Y)[] pts) => new()
    {
        IsClosed = false,
        Points = pts.Select(p => new Point2(p.X, p.Y)).ToList(),
    };

    private static Polyline2 Closed(params (double X, double Y)[] pts) => new()
    {
        IsClosed = true,
        Points = pts.Select(p => new Point2(p.X, p.Y)).ToList(),
    };

    [Fact]
    public void StraightLine_PivotShiftedBehindBladeTip()
    {
        // Open 2-point line going right. Pivot trails by 1mm.
        var path = Open((0, 0), (100, 0));
        var result = DragKnifeCompensator.Compensate(path, bladeOffsetMm: 1, overcutMm: 0);

        Assert.Equal(2, result.Points.Count);
        Assert.False(result.IsClosed);
        Assert.Equal(-1, result.Points[0].X, Eps); // pivot starts 1mm behind blade tip
        Assert.Equal(0, result.Points[0].Y, Eps);
        Assert.Equal(99, result.Points[1].X, Eps); // pivot ends 1mm behind blade tip
        Assert.Equal(0, result.Points[1].Y, Eps);
    }

    [Fact]
    public void NinetyDegreeTurnLeft_InsertsArcPoints()
    {
        // L-shape: right then up (CCW / left turn).
        var path = Open((0, 0), (100, 0), (100, 100));
        var result = DragKnifeCompensator.Compensate(path, bladeOffsetMm: 1, overcutMm: 0);

        // Without arc: 3 points. With 90° arc at 15°/step → 6 arc points → 9 total.
        Assert.True(result.Points.Count > 3, $"Expected arc points, got {result.Points.Count}");

        // First point: pivot behind blade start.
        Assert.Equal(-1, result.Points[0].X, Eps);
        Assert.Equal(0, result.Points[0].Y, Eps);

        // Last point: pivot behind blade tip at path end.
        Assert.Equal(100, result.Points[^1].X, Eps);
        Assert.Equal(99, result.Points[^1].Y, Eps);
    }

    [Fact]
    public void NinetyDegreeTurnRight_InsertsArcPoints()
    {
        // L-shape: right then down (CW / right turn).
        var path = Open((0, 0), (100, 0), (100, -100));
        var result = DragKnifeCompensator.Compensate(path, bladeOffsetMm: 1, overcutMm: 0);

        Assert.True(result.Points.Count > 3, $"Expected arc points, got {result.Points.Count}");

        // Last point: pivot behind blade at end of second segment (going down).
        Assert.Equal(100, result.Points[^1].X, Eps);
        Assert.Equal(-99, result.Points[^1].Y, Eps);
    }

    [Fact]
    public void ClosedSquare_AddsOvercutAtEnd()
    {
        // Closed 4-point square. Overcut should extend along the first segment direction.
        var path = Closed((0, 0), (100, 0), (100, 100), (0, 100));
        var result = DragKnifeCompensator.Compensate(path, bladeOffsetMm: 1, overcutMm: 2);

        // The second-to-last point is the closing arc end; the last point is overcut.
        var secondLast = result.Points[^2];
        var last       = result.Points[^1];

        // Overcut goes along dirs[0] = (1,0), so dx=2, dy=0.
        Assert.Equal(2, last.X - secondLast.X, Eps);
        Assert.Equal(0, last.Y - secondLast.Y, Eps);
    }

    [Fact]
    public void ClosedSquare_ZeroOvercut_NoExtraPoint()
    {
        var pathWithOvercut = Closed((0, 0), (100, 0), (100, 100), (0, 100));
        var pathNoOvercut   = Closed((0, 0), (100, 0), (100, 100), (0, 100));

        var withOvercut = DragKnifeCompensator.Compensate(pathWithOvercut, 1, overcutMm: 1);
        var noOvercut   = DragKnifeCompensator.Compensate(pathNoOvercut,   1, overcutMm: 0);

        // No overcut means one fewer point.
        Assert.Equal(withOvercut.Points.Count - 1, noOvercut.Points.Count);
    }

    [Fact]
    public void OpenPath_NoOvercutEvenWhenSpecified()
    {
        // Overcut only applies to closed paths.
        var path = Open((0, 0), (100, 0), (100, 100));
        var withOvercut = DragKnifeCompensator.Compensate(path, 1, overcutMm: 5);
        var noOvercut   = DragKnifeCompensator.Compensate(path, 1, overcutMm: 0);

        Assert.Equal(withOvercut.Points.Count, noOvercut.Points.Count);
    }

    [Fact]
    public void ZeroBladeOffset_NoArcPoints_PathMatchesOriginalStructure()
    {
        // With zero blade offset, all pivot positions equal design vertices.
        var path = Open((0, 0), (100, 0), (100, 100));
        var result = DragKnifeCompensator.Compensate(path, bladeOffsetMm: 0, overcutMm: 0);

        // Exactly 3 points: start, end-of-seg0, end-of-seg1 — no arcs.
        Assert.Equal(3, result.Points.Count);
        Assert.Equal(0, result.Points[0].X, Eps);
        Assert.Equal(100, result.Points[1].X, Eps);
        Assert.Equal(100, result.Points[2].X, Eps);
        Assert.Equal(100, result.Points[2].Y, Eps);
    }

    [Fact]
    public void SmallAngle_BelowThreshold_NoArc()
    {
        // A ~0.57° turn (100mm horizontal, 1mm vertical) is below the 5° threshold.
        var path = Open((0, 0), (100, 0), (200, 1));
        var result = DragKnifeCompensator.Compensate(path, bladeOffsetMm: 1, overcutMm: 0);

        // Start + 2 segEnd = 3 points (no arc inserted).
        Assert.Equal(3, result.Points.Count);
    }

    [Fact]
    public void ResultIsAlwaysOpen()
    {
        var openPath   = Open((0, 0), (100, 0));
        var closedPath = Closed((0, 0), (100, 0), (100, 100), (0, 100));

        Assert.False(DragKnifeCompensator.Compensate(openPath, 1, 0).IsClosed);
        Assert.False(DragKnifeCompensator.Compensate(closedPath, 1, 1).IsClosed);
    }
}
