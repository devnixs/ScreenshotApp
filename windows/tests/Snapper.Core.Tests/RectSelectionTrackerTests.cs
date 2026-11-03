using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using static Snapper.Core.Tests.SelectionFixtures;
namespace Snapper.Core.Tests;
public sealed class RectSelectionTrackerTests
{
    private static RectSelectionOutcome Select(PointD start, PointD end, params DisplayInfo[] displays)
    { var t = new RectSelectionTracker(); t.MouseDown(start, displays); return t.MouseUp(end); }
    private static void Selected(RectSelectionOutcome actual, DisplayInfo display, PixelRect rect)
    {
        Assert.Equal(RectSelectionOutcomeKind.Selected, actual.Kind);
        Assert.Equal(new RectSelection(display, rect), actual.Result);
    }
    [Fact] public void ForwardAndReverseDrags()
    {
        var t = new RectSelectionTracker(); t.MouseDown(new(100,100), [A,B]);
        Assert.Equal(new RectD(100,100,0,0), t.CurrentRect);
        Selected(t.MouseUp(new(400,300)), A, new(100,100,300,200));
        Selected(Select(new(400,300), new(100,100), A,B), A, new(100,100,300,200));
    }
    [Fact] public void CrossMonitorClampsToStartMonitor()
    {
        var t = new RectSelectionTracker(); t.MouseDown(new(1800,500), [A,B]);
        Assert.Equal(new RectD(1800,500,120,300), t.Drag(new(2500,800)));
        Selected(t.MouseUp(new(2500,800)), A, new(1800,500,120,300));
    }
    [Fact] public void VerticalClamping()
    {
        Selected(Select(new(100,100), new(300,-50), A), A, new(100,0,200,100));
        Selected(Select(new(100,100), new(300,2000), A), A, new(100,100,200,980));
    }
    [Fact] public void StartMonitorStaysFixedAt150Percent()
    {
        var t = new RectSelectionTracker(); t.MouseDown(new(2000,100), [A,B]);
        Assert.Equal(new RectD(1920,100,80,400), t.Drag(new(500,500)));
        Assert.Equal(B,t.StartDisplay);
    }
    [Fact] public void NegativeOriginAt200Percent() => Selected(Select(new(-100,100),new(50,-800),A,C),C,new(-100,-500,100,600));
    [Theory]
    [InlineData(96,3,10,false)] [InlineData(96,10,3,false)] [InlineData(96,4,4,true)]
    [InlineData(120,5,5,true)] [InlineData(120,4,10,false)]
    [InlineData(144,5,100,false)] [InlineData(144,6,6,true)]
    [InlineData(168,7,7,true)] [InlineData(168,6,10,false)]
    [InlineData(192,7,100,false)] [InlineData(192,8,8,true)]
    public void ThresholdIsFourDipsPerStartMonitorDpi(int dpi,int width,int height,bool selected)
    {
        var d = Display("test",new(-100,-100,3000,2000),dpi);
        var r = Select(new(0,0),new(width,height),d);
        Assert.Equal(selected ? RectSelectionOutcomeKind.Selected : RectSelectionOutcomeKind.TooSmall,r.Kind);
        if(selected) Assert.Equal(new RectSelection(d,new(0,0,width,height)),r.Result);
    }
    [Fact] public void ClickIsTooSmall() => Assert.Equal(RectSelectionOutcomeKind.TooSmall,Select(new(100,100),new(100,100),A).Kind);
    [Fact] public void ThresholdUsesClampedRect() => Assert.Equal(RectSelectionOutcomeKind.TooSmall,Select(new(1918,100),new(2600,300),A,B).Kind);
    [Fact] public void IdleAfterOutcomeAndNewDragWorks()
    {
        var t = new RectSelectionTracker();t.MouseDown(new(100,100),[A]);
        Assert.Equal(RectSelectionOutcomeKind.TooSmall,t.MouseUp(new(102,102)).Kind);
        Assert.Null(t.CurrentRect); Assert.Null(t.CurrentPoint);Assert.Null(t.StartDisplay);
        t.MouseDown(new(100,100),[A]);Selected(t.MouseUp(new(200,200)),A,new(100,100,100,100));
        Assert.Null(t.CurrentRect);
    }
    [Fact] public void ResetDiscardsDrag()
    {
        var t = new RectSelectionTracker();t.MouseDown(new(100,100),[A]);t.Drag(new(300,300));t.Reset();
        Assert.Null(t.CurrentRect);Assert.Null(t.Drag(new(500,500)));Assert.Equal(new(RectSelectionOutcomeKind.Ignored),t.MouseUp(new(500,500)));
    }
    [Fact] public void NoMouseDown()
    {
        var t = new RectSelectionTracker();Assert.Null(t.Drag(new(100,100)));Assert.Equal(new(RectSelectionOutcomeKind.Ignored),t.MouseUp(new(100,100)));
    }
    [Fact] public void EmptyDisplaysStaysIdle()
    {
        var t = new RectSelectionTracker();t.MouseDown(new(10,10),[]);Assert.Null(t.StartDisplay);Assert.Equal(new(RectSelectionOutcomeKind.Ignored),t.MouseUp(new(100,100)));
        t.MouseDown(new(100,100),[A]);t.MouseDown(new(10,10),[]);Assert.Null(t.CurrentRect);
    }
    [Fact] public void OuterEdgeAndNearestFallback()
    {
        var t=new RectSelectionTracker();t.MouseDown(new(1920,1080),[A]);Assert.Equal(A,t.StartDisplay);Assert.Equal(new PointD(1920,1080),t.CurrentPoint);
        Selected(t.MouseUp(new(1800,1000)),A,new(1800,1000,120,80));
        t.MouseDown(new(5000,5000),[A,B]);Assert.Equal(B,t.StartDisplay);Assert.Equal(new PointD(4800,1620),t.CurrentPoint);
        Selected(t.MouseUp(new(4700,1500)),B,new(4700,1500,100,120));
    }
    [Fact] public void SharedEdgeBelongsToRightMonitor() => Selected(Select(new(1920,500),new(2000,600),A,B),B,new(1920,500,80,100));
}
