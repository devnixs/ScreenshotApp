using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class WindowTrackingReducerTests
{
    private static readonly WindowTrackingState Initial = new(new(100,100,800,600));
    [Fact] public void SameFrameAndUnavailablePreserveState()
    {
        foreach (WindowPollResult poll in new WindowPollResult[] { new WindowPollResult.Unavailable(),new WindowPollResult.Present(Initial.Frame,true) })
        { var (state,e)=WindowTrackingReducer.Reduce(Initial,poll); Assert.Equal(Initial,state); Assert.IsType<WindowTrackingEvent.Unchanged>(e); }
    }
    [Theory, InlineData(300,120,800,600), InlineData(-1820,80,800,600), InlineData(100,-1000,400,300)]
    public void MovementPreservesPhysicalCoordinates(int x,int y,int w,int h)
    {
        var frame=new PixelRect(x,y,w,h);var (state,e)=WindowTrackingReducer.Reduce(Initial,new WindowPollResult.Present(frame,true));
        Assert.Equal(frame,state.Frame);Assert.Equal(new WindowTrackingEvent.Moved(frame),e);
    }
    [Fact] public void ClosedEmittedOnceAndNeverReopens()
    {
        var (closed,e)=WindowTrackingReducer.Reduce(Initial,new WindowPollResult.Gone());Assert.True(closed.IsClosed);Assert.IsType<WindowTrackingEvent.Closed>(e);
        foreach(WindowPollResult poll in new WindowPollResult[]{new WindowPollResult.Gone(),new WindowPollResult.Unavailable(),new WindowPollResult.Present(Initial.Frame,true)})
        {var (state,next)=WindowTrackingReducer.Reduce(closed,poll);Assert.Equal(closed,state);Assert.IsType<WindowTrackingEvent.Unchanged>(next);}
    }
    [Fact] public void HiddenUpdatesFrameAndRestoresWithLatestBounds()
    {
        var offscreen=new PixelRect(-32000,-32000,160,28);
        var (hidden,e)=WindowTrackingReducer.Reduce(Initial,new WindowPollResult.Present(offscreen,false));
        Assert.IsType<WindowTrackingEvent.Hidden>(e);Assert.False(hidden.IsVisible);Assert.False(hidden.IsClosed);Assert.Equal(offscreen,hidden.Frame);
        var (same,again)=WindowTrackingReducer.Reduce(hidden,new WindowPollResult.Present(offscreen,false));Assert.Equal(hidden,same);Assert.IsType<WindowTrackingEvent.Unchanged>(again);
        var frame=new PixelRect(200,100,400,300);var (shown,restored)=WindowTrackingReducer.Reduce(same,new WindowPollResult.Present(frame,true));
        Assert.True(shown.IsVisible);Assert.Equal(new WindowTrackingEvent.Shown(frame),restored);Assert.Equal(frame,shown.Frame);
    }
    [Fact] public void UnavailableWhileHiddenDoesNotShowOrClose()
    {
        var hidden=Initial with {IsVisible=false};var(state,e)=WindowTrackingReducer.Reduce(hidden,new WindowPollResult.Unavailable());
        Assert.Equal(hidden,state);Assert.IsType<WindowTrackingEvent.Unchanged>(e);
    }
}
