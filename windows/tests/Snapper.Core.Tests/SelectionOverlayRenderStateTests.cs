using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using static Snapper.Core.Tests.SelectionFixtures;
namespace Snapper.Core.Tests;
public sealed class SelectionOverlayRenderStateTests
{
    private static OverlayFrame Render(DisplayInfo display, RectSelectionTracker tracker) => SelectionOverlayRenderState.For(display, tracker, _ => new(60,20));
    [Fact] public void IdleTrackerIsEmptyEverywhere()
    {Assert.Equal(OverlayFrame.Empty,Render(A,new()));Assert.Equal(OverlayFrame.Empty,Render(B,new()));}
    [Fact] public void At150PercentConvertsToLocalDips()
    {
        var t=new RectSelectionTracker();t.MouseDown(new(2070,150),[A,B]);t.Drag(new(2370,450));
        Assert.Equal(new(new RectD(100,100,200,200),"200 × 200",new PointD(312,312)),Render(B,t));Assert.Equal(OverlayFrame.Empty,Render(A,t));
    }
    [Fact] public void NegativeOriginAt200Percent()
    {
        var t=new RectSelectionTracker();t.MouseDown(new(-3800,-400),[A,C]);t.Drag(new(-3000,0));
        Assert.Equal(new(new RectD(20,50,400,200),"400 × 200",new PointD(432,262)),Render(C,t));
    }
    [Fact] public void At100PercentNearBottomRightFlips()
    {
        var t=new RectSelectionTracker();t.MouseDown(new(1000,500),[A]);t.Drag(new(1915,1075));
        Assert.Equal(new(new RectD(1000,500,915,575),"915 × 575",new PointD(1843,1043)),Render(A,t));
    }
    [Fact] public void CrossMonitorDragRendersOnlyOnStartMonitor()
    {
        var t=new RectSelectionTracker();t.MouseDown(new(1800,500),[A,B]);t.Drag(new(2500,800));
        Assert.Equal(new RectD(1800,500,120,300),Render(A,t).SelectionDips);Assert.Equal(OverlayFrame.Empty,Render(B,t));
    }
    [Fact] public void MatchesDisplayByDeviceName()
    {
        var t=new RectSelectionTracker();t.MouseDown(new(100,100),[A]);t.Drag(new(200,200));
        Assert.NotEqual(OverlayFrame.Empty,Render(A with{ WorkArea=new(0,0,1920,1000)},t));
    }
}
