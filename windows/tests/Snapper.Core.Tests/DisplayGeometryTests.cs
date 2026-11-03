using Snapper.Core.Geometry;
using static Snapper.Core.Tests.DisplayFixtures;
using static Snapper.Core.Geometry.DisplayGeometry;

namespace Snapper.Core.Tests;

public sealed class DisplayGeometryTests
{
    [Theory]
    [InlineData(96, 4)] [InlineData(120, 5)] [InlineData(144, 6)] [InlineData(168, 7)] [InlineData(192, 8)]
    public void DipPixelThreshold(int dpi, double pixels)
    {
        Assert.Equal(pixels, DipsToPixels(4, dpi));
        Assert.Equal(4, PixelsToDips(pixels, dpi));
    }
    [Fact] public void FractionalPixelConversions()
    {
        Assert.Equal(4, PixelsToDips(6, 144));
        Assert.Equal(301 / 1.5, PixelsToDips(301, 144), 1e-9);
        Assert.Equal(0.8, PixelsToDips(1, 120), 1e-9);
    }
    [Fact] public void OriginsAreSubtractedBeforeScaling()
    {
        foreach (var (d, p) in new[] { (P, new PointD(100, 200)), (R, new PointD(2070, 300)), (L, new PointD(-3640, -140)), (A, new PointD(125, -1190)) })
        {
            Assert.Equal(new PointD(100, 200), ToLocalDips(p, d));
            Assert.Equal(p, ToGlobalPixels(new PointD(100, 200), d));
        }
        Equal(new PointD(2 / 3.0, 2 / 3.0), ToLocalDips(new PointD(1921, 1), R));
        Equal(new PointD(0.8, 0.8), ToLocalDips(new PointD(1, -1439), A));
    }
    [Fact] public void RoundTripsAcrossMixedAndSingleDpiLayouts()
    {
        foreach (var d in All.Concat(new[] { 96, 120, 144, 168, 192 }.Select(Single)))
            foreach (var p in new[] { new PointD(d.Bounds.X, d.Bounds.Y), new PointD(d.Bounds.Right, d.Bounds.Bottom), RectD.FromPixelRect(d.Bounds).Center, new PointD(d.Bounds.Left + 0.5, d.Bounds.Top + 0.25) })
                Equal(p, ToGlobalPixels(ToLocalDips(p, d), d));
    }
    [Fact] public void RectConversionsStandardize()
    {
        foreach (var (d, r) in new[] { (R, new RectD(2070, 300, 300, 150)), (L, new RectD(-3640, -140, 400, 200)), (R, new RectD(2370, 450, -300, -150)) })
        {
            var local = new RectD(100, 200, 200, 100);
            Assert.Equal(local, ToLocalDips(r, d));
            Assert.Equal(r.Standardized(), ToGlobalPixels(local, d));
        }
    }
    [Fact] public void ReadoutUsesStartDisplay()
    {
        Assert.Equal(new SizeD(120, 100), DipSize(new(1800, 500, 120, 100), P));
        var size = DipSize(new(1920, 100, 100, 200), R);
        Assert.Equal(100 / 1.5, size.Width, 1e-9); Assert.Equal(200 / 1.5, size.Height, 1e-9);
        Assert.Equal(new SizeD(100, 700), DipSize(new(-200, -500, 200, 1400), L));
        Assert.Equal(new SizeD(80, 80), DipSize(new(100, -100, -100, -100), A));
    }
    [Fact] public void WindowPlacementRoundsLocalEdges()
    {
        Assert.Equal(new PixelRect(13, -1427, 126, 38), RoundToGlobalPixels(new(10, 10, 101, 31), A));
        Assert.Equal(R.Bounds, RoundToGlobalPixels(new(0, 0, 1920, 1080), R));
        Assert.Equal(new PixelRect(-3839, -539, 20, 20), RoundToGlobalPixels(new(0.3, 0.3, 10, 10), L));
        Assert.Equal(new PixelRect(11, 11, 1, 1), RoundToGlobalPixels(new(10.5, 10.5, 1, 1), P));
        Assert.Equal(new PixelRect(13, -1427, 126, 38), RoundToGlobalPixels(new(111, 41, -101, -31), A));
    }
    [Theory] [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(168)] [InlineData(192)]
    public void AdjacentDipRectsTileAfterEdgeRounding(int dpi)
    {
        var d = Single(dpi);
        for (var x = 0; x < 100; x++)
        {
            var first = RoundToGlobalPixels(new(x, 0, 1, 1), d);
            var second = RoundToGlobalPixels(new(x + 1, 0, 1, 1), d);
            Assert.Equal(first.Right, second.Left);
        }
        if (dpi == 168)
        {
            Assert.Equal(new PixelRect(0, 0, 2, 2), RoundToGlobalPixels(new(0, 0, 1, 1), d));
            Assert.Equal(new PixelRect(2, 0, 2, 2), RoundToGlobalPixels(new(1, 0, 1, 1), d));
        }
    }
    [Fact] public void PrimaryDisplayFallbacks()
    {
        Assert.Equal(P, PrimaryDisplay([R, P]));
        var unmarked = P with { IsPrimary = false };
        Assert.Equal(unmarked, PrimaryDisplay([R, unmarked]));
        Assert.Equal(R, PrimaryDisplay([R, A]));
        Assert.Null(PrimaryDisplay([]));
    }
    [Theory]
    [InlineData(0, 0, 0)] [InlineData(1919, 500, 0)] [InlineData(1919.5, 500, 0)]
    [InlineData(1920, 500, 1)] [InlineData(1920, 0, 1)] [InlineData(-0.5, 10, 2)]
    [InlineData(-1, -1, 2)] [InlineData(-3840, -540, 2)] [InlineData(100, -1, 3)]
    [InlineData(0, -1, 3)] [InlineData(2000, -1, 3)] [InlineData(4800, 500, 1)] [InlineData(100, 1080, 0)]
    public void LookupHalfOpenThenClosed(double x, double y, int index) => Assert.Equal(All[index], DisplayContaining(new(x, y), All));
    [Theory] [InlineData(4801, 500)] [InlineData(100, 1081)] [InlineData(2600, -1)]
    public void LookupOutsideDesktop(double x, double y) => Assert.Null(DisplayContaining(new(x, y), All));
    [Fact] public void EmptyLookups() { Assert.Null(DisplayContaining(new(0, 0), [])); Assert.Null(NearestDisplay(new(0, 0), [])); }
    [Fact] public void SharedEdgesIgnoreInputOrderAndClosedTiesPreserveIt()
    {
        Assert.Equal(R, DisplayContaining(new(1920, 0), [A, R, P, L]));
        var edge = new PointD(1920, 1080);
        var shortRight = R with { Bounds = new(1920, 0, 2880, 1080) };
        Assert.Equal(P, DisplayContaining(edge, [P, shortRight]));
        Assert.Equal(shortRight, DisplayContaining(edge, [shortRight, P]));
    }
    [Fact] public void NearestDisplayAndStableDistanceTies()
    {
        Assert.Equal(R, NearestDisplay(new(2600, -1), All));
        Assert.Equal(R, NearestDisplay(new(4801, 500), All));
        Assert.Equal(P, NearestDisplay(new(100, 1081), All));
        Assert.Equal(L, NearestDisplay(new(-1, -541), All));
        Assert.Equal(A, NearestDisplay(new(-1, -541), [P, R, A, L]));
        Assert.Equal(R, NearestDisplay(new(1920, 0), [A, R, P, L]));
    }
    [Fact] public void SquaredDistancesUseClosedPixelBounds()
    {
        Assert.Equal(0, SquaredDistance(new(100, 100), P.Bounds));
        Assert.Equal(0, SquaredDistance(new(1920, 10), P.Bounds));
        Assert.Equal(125, SquaredDistance(new(1925, 1090), P.Bounds));
    }
    [Fact] public void NormalizesAllDragDirections()
    {
        foreach (var (a, b) in new[] { (new PointD(10, 20), new PointD(110, 70)), (new PointD(110, 70), new PointD(10, 20)), (new PointD(110, 20), new PointD(10, 70)) })
            Assert.Equal(new RectD(10, 20, 100, 50), NormalizedRect(a, b));
        Assert.Equal(new RectD(5, 5, 0, 0), NormalizedRect(new(5, 5), new(5, 5)));
    }
    [Fact] public void ClampsPointsClosed()
    {
        Assert.Equal(new PointD(1920, 1080), Clamp(new PointD(2000, 1100), P.Bounds));
        Assert.Equal(new PointD(0, 0), Clamp(new PointD(-5, -5), P.Bounds));
        Assert.Equal(new PointD(100, 100), Clamp(new PointD(100, 100), P.Bounds));
    }
    [Fact] public void ClampsStandardizedRectsAndDisjointRects()
    {
        foreach (var (r, expected) in new[] {
            (new RectD(1800, 1000, 300, 300), new RectD(1800, 1000, 120, 80)),
            (new RectD(3000, 3000, 10, 10), new RectD(1920, 1080, 0, 0)),
            (new RectD(1920, 0, 10, 10), new RectD(1920, 0, 0, 10)),
            (new RectD(100, 100, 0, 0), new RectD(100, 100, 0, 0)),
            (new RectD(2100, 1300, -300, -300), new RectD(1800, 1000, 120, 80)) })
            Assert.Equal(expected, Clamp(r, P.Bounds));
        Assert.Equal(new RectD(-3840, -540, 140, 40), Clamp(new RectD(-4000, -600, 300, 100), L.Bounds));
    }
    [Fact] public void DragClampsToStartingMonitorAcrossMixedDpi()
    {
        Assert.Equal(new RectD(1800, 500, 120, 100), DragRect(new(1800, 500), new(2500, 600), P));
        Assert.Equal(new RectD(1920, 100, 100, 200), DragRect(new(2020, 100), new(1900, 300), R));
        Assert.Equal(new RectD(-200, -500, 200, 1400), DragRect(new(-200, -500), new(300, 900), L));
        Assert.Equal(new RectD(100, -100, 100, 100), DragRect(new(100, -100), new(200, 300), A));
        Assert.Equal(new RectD(2000, -60, 560, 10), DragRect(new(2000, -50), new(3000, -60), A));
    }
    [Fact] public void DragHandlesOutsideStartAndZeroSize()
    {
        Assert.Equal(new RectD(0, 0, 100, 100), DragRect(new(100, 100), new(-5000, -5000), P));
        Assert.Equal(new RectD(100, 100, 0, 0), DragRect(new(100, 100), new(100, 100), P));
        Assert.Equal(new RectD(0, 0, 100, 100), DragRect(new(-5, -5), new(100, 100), P));
    }
    [Fact] public void AlignsOutwardInLocalPixels()
    {
        Assert.Equal(new PixelRect(10, 20, 101, 51), PixelAligned(new(10.25, 20.75, 100.5, 50), P));
        Assert.Equal(new PixelRect(10, 20, 100, 50), PixelAligned(new(10, 20, 100, 50), P));
        Assert.Equal(new PixelRect(-3840, -540, 51, 51), PixelAligned(new(-3839.6, -539.4, 50, 50), L));
        Assert.Equal(new PixelRect(10, 20, 100, 50), PixelAligned(new(110, 70, -100, -50), P));
    }
    [Fact] public void FloatNoiseDoesNotGrowSelections()
    {
        foreach (var noise in new[] { 1e-10, -1e-10 })
            Assert.Equal(new PixelRect(1920, 0, 100, 50), PixelAligned(new(1920 + noise, 0, 100, 50), R));
        var a = ToGlobalPixels(new PointD(10.4, 20.8), A);
        var b = ToGlobalPixels(new PointD(90.4, 60.8), A);
        Assert.Equal(new PixelRect(13, -1414, 100, 50), PixelAligned(NormalizedRect(a, b), A));
    }
    [Fact] public void ZeroSizesSnapToNearestPixelWithoutGrowing()
    {
        Assert.Equal(new PixelRect(10, 11, 0, 0), PixelAligned(new(10.25, 10.6, 0, 0), P));
        Assert.Equal(new PixelRect(10, 10, 0, 2), PixelAligned(new(10.25, 10.25, 0, 1), P));
        Assert.Equal(new PixelRect(10, 10, 0, 1), PixelAligned(new(10.25, 10.25, 1e-7, 0.5), P));
    }
    [Fact] public void AlignedRectsStayWithinMonitor()
    {
        Assert.Equal(new PixelRect(1919, 1079, 1, 1), PixelAligned(new(1919.8, 1079.8, 0.4, 0.4), P));
        Assert.Equal(new PixelRect(1920, 1080, 0, 0), PixelAligned(new(2000, 2000, 0, 0), P));
    }
    [Fact] public void CaptureRectsHaveNativePixelSizeAtEveryDpi()
    {
        foreach (var (d, expected) in new[] { (P, new PixelRect(100, 100, 200, 100)), (R, new PixelRect(2070, 150, 300, 150)), (L, new PixelRect(-3640, -340, 400, 200)), (A, new PixelRect(125, -1315, 250, 125)) })
            Assert.Equal(expected, CaptureRect(ToGlobalPixels(new RectD(100, 100, 200, 100), d), d));
    }
    [Fact] public void CaptureClampsAndStandardizes()
    {
        Assert.Equal(new PixelRect(1800, 1000, 120, 80), CaptureRect(new(1800, 1000, 300, 300), P));
        Assert.Equal(new PixelRect(500, -1300, 500, 300), CaptureRect(new(1000, -1000, -500, -300), A));
        Assert.Equal(new PixelRect(1920, 1080, 0, 0), CaptureRect(new(5000, 5000, 10, 10), P));
    }
    [Fact] public void WholeMonitorCaptureAndMonitorLocalOffsets()
    {
        foreach (var d in All)
        {
            Assert.Equal(d.Bounds, CaptureRect(RectD.FromPixelRect(d.Bounds), d));
            Assert.Equal(new PixelRect(0, 0, d.Bounds.Width, d.Bounds.Height), ToMonitorLocal(d.Bounds, d));
        }
        Assert.Equal(new PixelRect(200, 400, 400, 200), ToMonitorLocal(new(-3640, -140, 400, 200), L));
        Assert.Equal(new PixelRect(150, 150, 300, 150), ToMonitorLocal(new(2070, 150, 300, 150), R));
        Assert.Equal(new PixelRect(125, 125, 250, 125), ToMonitorLocal(new(125, -1315, 250, 125), A));
        Assert.Equal(new PixelRect(-10, 0, 20, 20), ToMonitorLocal(new(-10, 0, 20, 20), P));
    }
    [Fact] public void LargestPositiveOverlapAndStableTies()
    {
        Assert.Equal(R, DisplayOverlapping(new(2000, 100, 800, 600), All));
        Assert.Equal(R, DisplayOverlapping(new(1500, 100, 1000, 500), All));
        Assert.Equal(P, DisplayOverlapping(new(1000, 100, 1000, 500), All));
        Assert.Equal(P, DisplayOverlapping(new(1420, 100, 1000, 500), All));
        Assert.Equal(R, DisplayOverlapping(new(1420, 100, 1000, 500), [R, P, L, A]));
        Assert.Null(DisplayOverlapping(new(5000, 5000, 10, 10), All));
        Assert.Null(DisplayOverlapping(new(100, 100, 0, 0), All));
        Assert.Null(DisplayOverlapping(new(100, 100, 10, 10), []));
        Assert.Equal(R, DisplayOverlapping(new(2500, 600, -1000, -500), All));
    }
}
