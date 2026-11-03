using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class WindowPollClassifierTests
{
    private static readonly DisplayInfo P=new("P",new(0,0,1920,1080),new(0,0,1920,1040),96,true);
    private static readonly DisplayInfo L=new("L",new(-2880,0,2880,1620),new(-2880,0,2880,1580),144,false);
    private static readonly DisplayInfo R=new("R",new(1920,-200,3840,2160),new(1920,-200,3840,2120),192,false);
    private static readonly WindowProbe Visible=new(true,42,true,false,false,new(100,100,800,600));
    [Fact] public void MissingAndReusedHandleAreGone()
    {
        Assert.IsType<WindowPollResult.Gone>(WindowPollClassifier.Classify(Visible with{Exists=false},42,[P,L,R]));
        Assert.IsType<WindowPollResult.Gone>(WindowPollClassifier.Classify(Visible with{ThreadId=43},42,[P,L,R]));
    }
    [Fact] public void MissingDwmBoundsIsUnavailableNotClosed() => Assert.IsType<WindowPollResult.Unavailable>(WindowPollClassifier.Classify(Visible with{ExtendedFrame=null},42,[P,L,R]));
    [Theory, InlineData(100,100,800,600),InlineData(-1500,200,1200,900),InlineData(2500,-150,1600,1200),InlineData(-300,100,1000,600)]
    public void VisibleAcrossDpiAndNegativeOrigins(int x,int y,int w,int h)
    {var frame=new PixelRect(x,y,w,h);Assert.Equal(new WindowPollResult.Present(frame,true),WindowPollClassifier.Classify(Visible with{ExtendedFrame=frame},42,[P,L,R]));}
    [Theory, InlineData(0),InlineData(1),InlineData(2)] public void HiddenMinimizedOrCloakedNeverClosed(int reason)
    {
        var probe=reason switch{0=>Visible with{IsVisible=false},1=>Visible with{IsMinimized=true,ExtendedFrame=new(-32000,-32000,160,28)},_=>Visible with{IsCloaked=true}};
        Assert.Equal(new WindowPollResult.Present(probe.ExtendedFrame.GetValueOrDefault(),false),WindowPollClassifier.Classify(probe,42,[P,L,R]));
    }
    [Theory,InlineData(10000,10000,100,100),InlineData(1920,500,100,100),InlineData(100,100,0,100)]
    public void RequiresPositiveMonitorIntersection(int x,int y,int w,int h)
    {var frame=new PixelRect(x,y,w,h);Assert.Equal(new WindowPollResult.Present(frame,false),WindowPollClassifier.Classify(Visible with{ExtendedFrame=frame},42,[P]));}
    [Fact] public void PillAssignmentUsesOverlapAndHiddenHasNoOverlap()
    {
        Assert.Equal(P,WindowDisplayRule.Display(new(-300,100,1000,600),[P,L,R]));
        Assert.Equal(L,WindowDisplayRule.Display(new(-700,100,1000,600),[P,L,R]));
        Assert.Null(DisplayGeometry.DisplayOverlapping(new(10000,10000,100,100),[P,L,R]));
    }
}
