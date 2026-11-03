using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using static Snapper.Core.Tests.WindowCandidateFilterTests;
namespace Snapper.Core.Tests;
public sealed class WindowSelectionTests
{
    internal static readonly DisplayInfo A = SelectionFixtures.Display("A",new(0,0,1920,1080),96,true);
    internal static readonly DisplayInfo B = SelectionFixtures.Display("B",new(1920,-540,3840,2160),192);
    internal static readonly DisplayInfo C = SelectionFixtures.Display("C",new(-2560,0,2560,1440),144);
    internal static readonly DisplayInfo[] Displays = [A,B,C];
    private static WindowHitTester Tester(PixelRect frame) => new([Candidate(frame:frame)],99,new HashSet<nint>());
    [Fact] public void NullTesterReturnsNull() => Assert.Null(WindowSelection.Resolve(null,new(0,0),Displays));
    [Fact] public void EmptyDesktopReturnsNull() => Assert.Null(WindowSelection.Resolve(Tester(new(10,10,100,100)),new(0,0),Displays));
    [Fact] public void WindowOnNegativeOriginMonitorPicksIt()
    {
        var r=new PixelRect(-2000,300,900,600);
        Assert.Equal(new WindowSelectionResult(1,r,C),WindowSelection.Resolve(Tester(r),new(-1500,500),Displays));
    }
    [Theory][InlineData(1500,1000,"B")][InlineData(1700,400,"A")]
    public void SpanningWindowPicksLargestOverlap(int x,int width,string expected)
    {
        var result=Assert.IsType<WindowSelectionResult>(WindowSelection.Resolve(Tester(new(x,100,width,400)),new(x+1,200),Displays));
        Assert.Equal(expected,result.Display.DeviceName);
    }
    [Fact] public void ReleaseOnOtherMonitorHitTestsReleasePoint()
    {
        var s=new SelectionSession();s.ToggleMode();s.MouseDown(new(100,100),Displays);
        var result=Assert.IsType<SelectionSessionOutcome.WindowClick>(s.MouseUp(new(2500,200)));
        Assert.Equal(new PixelPoint(2500,200),result.At);
        var frame=new PixelRect(2200,0,800,600);
        Assert.Equal(new WindowSelectionResult(1,frame,B),WindowSelection.Resolve(Tester(frame),result.At,Displays));
    }
    [Fact] public void NoDisplaysReturnsNull() => Assert.Null(WindowSelection.Resolve(Tester(new(0,0,100,100)),new(10,10),[]));
    [Fact] public void OffscreenWindowUsesNearestDisplay()
    {
        var frame=new PixelRect(-4000,300,900,600);
        Assert.Equal(C,Assert.IsType<WindowSelectionResult>(WindowSelection.Resolve(Tester(frame),new(-3900,500),Displays)).Display);
    }
}
