using Snapper.Core.Geometry;
using static Snapper.Core.Tests.DisplayFixtures;

namespace Snapper.Core.Tests;

public sealed class WindowDisplayRuleTests
{
    [Fact] public void FullyContainedWindows()
    {
        Assert.Equal(R, WindowDisplayRule.Display(new(2000, 100, 800, 600), All));
        Assert.Equal(L, WindowDisplayRule.Display(new(-3000, 0, 400, 300), All));
    }
    [Fact] public void SpanningWindowsChooseLargestPixelOverlap()
    {
        Assert.Equal(R, WindowDisplayRule.Display(new(1500, 100, 1000, 500), All));
        Assert.Equal(P, WindowDisplayRule.Display(new(1000, 100, 1000, 500), All));
        Assert.Equal(R, WindowDisplayRule.Display(new(1421, 100, 1000, 500), All));
    }
    [Fact] public void NegativeOriginsAndThreeMonitorWindows()
    {
        Assert.Equal(L, WindowDisplayRule.Display(new(-600, -300, 1000, 200), All));
        Assert.Equal(A, WindowDisplayRule.Display(new(-400, -300, 1000, 200), All));
        Assert.Equal(P, WindowDisplayRule.Display(new(-100, -100, 400, 400), All));
    }
    [Fact] public void OverlapTiesPreserveListOrder()
    {
        Assert.Equal(P, WindowDisplayRule.Display(new(1420, 100, 1000, 500), All));
        Assert.Equal(R, WindowDisplayRule.Display(new(1420, 100, 1000, 500), [R, P, L, A]));
    }
    [Fact] public void OffscreenAndMinimizedWindowsUseCenterDistance()
    {
        Assert.Equal(A, WindowDisplayRule.Display(new(2600, -300, 100, 100), All));
        Assert.Equal(R, WindowDisplayRule.Display(new(5000, 500, 100, 100), All));
        Assert.Equal(L, WindowDisplayRule.Display(new(-32000, -32000, 160, 28), All));
        Assert.Equal(P, WindowDisplayRule.Display(new(100, 100, 0, 0), All));
        Assert.Null(WindowDisplayRule.Display(new(0, 0, 100, 100), []));
    }
    [Fact] public void DistanceTiesPreserveOrderEvenAtSharedEdges()
    {
        var p2 = P with { DeviceName = "P2" };
        Assert.Equal(P, WindowDisplayRule.Display(new(3000, 100, 100, 100), [P, p2]));
        Assert.Equal(p2, WindowDisplayRule.Display(new(3000, 100, 100, 100), [p2, P]));
        // A zero-size window has no positive overlap: closed-distance ties use list order.
        Assert.Equal(P, WindowDisplayRule.Display(new(1920, 100, 0, 0), [P, R]));
        Assert.Equal(R, WindowDisplayRule.Display(new(1920, 100, 0, 0), [R, P]));
    }
    [Fact] public void NegativeSizeFramesAreStandardized() => Assert.Equal(R, WindowDisplayRule.Display(new(2800, 700, -800, -600), All));
}
