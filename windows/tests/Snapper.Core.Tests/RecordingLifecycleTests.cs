using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingLifecycleTests
{
    private const string Path = "/recording.mp4";
    private static readonly RecordingFailure Failure = new("Writer failed");
    private static readonly RecordingEffect StopCapture = new RecordingEffect.StopCapture(), Timeout = new RecordingEffect.ScheduleStopTimeout(),
        Ended = new RecordingEffect.Ended(), Reveal = new RecordingEffect.Reveal(Path), Delete = new RecordingEffect.DeleteFile(Path);
    private static RecordingLifecycle Starting()
    {
        var l = new RecordingLifecycle(); Assert.Equal([new RecordingEffect.StartCapture()], l.Handle(new RecordingEvent.Start(Path)));
        Assert.IsType<RecordingPhase.Starting>(l.Phase); return l;
    }
    private static RecordingLifecycle Started() { var l = Starting(); Assert.Empty(l.Handle(new RecordingEvent.CaptureStarted())); Assert.IsType<RecordingPhase.Recording>(l.Phase); return l; }
    [Fact] public void HappyPath()
    {
        var l=Started(); Assert.Equal([StopCapture,Timeout],l.Handle(new RecordingEvent.StopRequested())); Assert.IsType<RecordingPhase.Stopping>(l.Phase);
        Assert.Equal([Reveal,Ended],l.Handle(new RecordingEvent.OutputFinished())); Assert.Equal(new RecordingPhase.Finished(Path),l.Phase);
        Assert.Equal(TimeSpan.FromSeconds(30),RecordingLifecycle.StopTimeout); Assert.Equal(TimeSpan.FromSeconds(5),RecordingLifecycle.StartTimeout);
    }
    [Fact] public void SpontaneousWriterCompletionStopsStream() => Assert.Equal([StopCapture,Reveal,Ended],Started().Handle(new RecordingEvent.OutputFinished()));
    [Fact] public void DoubleStopAndCancelAfterStopAreIgnored()
    {
        var l=Started(); l.Handle(new RecordingEvent.StopRequested());
        foreach(var e in new RecordingEvent[]{new RecordingEvent.StopRequested(),new RecordingEvent.CancelRequested(),new RecordingEvent.StreamStopped(Failure)}) Assert.Empty(l.Handle(e));
        Assert.IsType<RecordingPhase.Stopping>(l.Phase); Assert.Equal([Reveal,Ended],l.Handle(new RecordingEvent.OutputFinished()));
    }
    [Fact] public void StopWhileStarting()
    {
        var l=Starting(); Assert.Empty(l.Handle(new RecordingEvent.StopRequested())); Assert.IsType<RecordingPhase.Starting>(l.Phase);
        Assert.Equal([StopCapture,Timeout],l.Handle(new RecordingEvent.CaptureStarted())); Assert.IsType<RecordingPhase.Stopping>(l.Phase);
    }
    [Fact] public void CancellationRemovesFile()
    {
        var l=Started(); Assert.Equal([StopCapture,Timeout],l.Handle(new RecordingEvent.CancelRequested()));
        Assert.Equal([Delete,Ended],l.Handle(new RecordingEvent.OutputFinished())); Assert.IsType<RecordingPhase.Cancelled>(l.Phase);
    }
    [Theory, InlineData(true), InlineData(false)] public void CancellationWinsPendingStop(bool cancelFirst)
    {
        var l=Starting();
        var events= new RecordingEvent[]{new RecordingEvent.StopRequested(),new RecordingEvent.CancelRequested()};
        foreach(var e in cancelFirst ? events.Reverse(): events) Assert.Empty(l.Handle(e));
        Assert.Equal([StopCapture,Timeout],l.Handle(new RecordingEvent.CaptureStarted())); Assert.Equal([Delete,Ended],l.Handle(new RecordingEvent.OutputFinished()));
    }
    [Fact] public void FailedStartShowsError()
    {
        var l=Starting(); Assert.Equal([Delete,new RecordingEffect.ShowError(Failure),Ended],l.Handle(new RecordingEvent.CaptureFailedToStart(Failure)));
        Assert.Equal(new RecordingPhase.Failed(Failure),l.Phase);
    }
    [Fact] public void FailedStartWithPendingStopStillEnds()
    {
        var l=Starting(); l.Handle(new RecordingEvent.StopRequested());
        Assert.Equal([Delete,new RecordingEffect.ShowError(Failure),Ended],l.Handle(new RecordingEvent.CaptureFailedToStart(Failure)));
    }
    [Fact] public void StreamAlreadyStoppedWaitsForWriter()
    {
        var l=Started(); Assert.Equal([Timeout],l.Handle(new RecordingEvent.StreamStopped(Failure)));
        Assert.Empty(l.Handle(new RecordingEvent.StopRequested())); Assert.Empty(l.Handle(new RecordingEvent.StreamStopped(Failure)));
        Assert.Equal([Reveal,Ended],l.Handle(new RecordingEvent.OutputFinished()));
    }
    [Fact] public void WindowStreamErrorReportsAfterFinalizing()
    {
        var l=Started(); Assert.Equal([Timeout],l.Handle(new RecordingEvent.WindowStreamFailed(Failure)));
        Assert.Empty(l.Handle(new RecordingEvent.StopRequested())); Assert.Empty(l.Handle(new RecordingEvent.WindowStreamFailed(Failure)));
        Assert.Equal([Reveal,new RecordingEffect.ShowError(Failure),Ended],l.Handle(new RecordingEvent.OutputFinished()));
        Assert.Empty(l.Handle(new RecordingEvent.OutputFinished()));
    }
    [Fact] public void DuplicateWindowClosureSavesOnce()
    {
        var l=Started(); l.Handle(new RecordingEvent.StopRequested()); Assert.Empty(l.Handle(new RecordingEvent.StopRequested()));
        Assert.Empty(l.Handle(new RecordingEvent.WindowStreamFailed(Failure))); Assert.Equal([Reveal,Ended],l.Handle(new RecordingEvent.OutputFinished()));
        Assert.Empty(l.Handle(new RecordingEvent.StopRequested())); Assert.Empty(l.Handle(new RecordingEvent.OutputFinished()));
    }
    [Theory, InlineData(true), InlineData(false)] public void WindowTimeoutRetainsOriginalFailure(bool content)
    {
        var l=Started(); l.Handle(new RecordingEvent.WindowStreamFailed(Failure));
        Assert.Equal([content?Reveal:Delete,new RecordingEffect.ShowError(Failure),Ended],l.Handle(new RecordingEvent.StopTimedOut(content)));
    }
    [Theory, InlineData(true), InlineData(false)] public void TimeoutPreservesOnlyContent(bool content)
    {
        var l=Started(); l.Handle(new RecordingEvent.StopRequested());
        Assert.Equal([content?Reveal:Delete,new RecordingEffect.ShowError(RecordingFailure.Timeout),Ended],l.Handle(new RecordingEvent.StopTimedOut(content)));
        Assert.Equal(content ? (RecordingPhase)new RecordingPhase.Finished(Path) : new RecordingPhase.Failed(RecordingFailure.Timeout),l.Phase);
    }
    [Theory, InlineData(true), InlineData(false)] public void WriterFailureDuringRecording(bool content)
    {
        var l=Started(); Assert.Equal([StopCapture,content?Reveal:Delete,new RecordingEffect.ShowError(Failure),Ended],l.Handle(new RecordingEvent.OutputFailed(Failure,content)));
        Assert.Equal(content ? (RecordingPhase)new RecordingPhase.Finished(Path) : new RecordingPhase.Failed(Failure),l.Phase);
    }
    [Theory, InlineData(true), InlineData(false)] public void WriterFailureDuringStop(bool content)
    {
        var l=Started(); l.Handle(new RecordingEvent.StopRequested());
        Assert.Equal([content?Reveal:Delete,new RecordingEffect.ShowError(Failure),Ended],l.Handle(new RecordingEvent.OutputFailed(Failure,content)));
    }
    [Theory, InlineData(true), InlineData(false)] public void CancellationDiscardsEveryCompletion(bool content)
    {
        foreach(var e in new RecordingEvent[]{new RecordingEvent.OutputFinished(),new RecordingEvent.OutputFailed(Failure,content),new RecordingEvent.StopTimedOut(content)})
        {
            var l=Started(); l.Handle(new RecordingEvent.CancelRequested()); Assert.Equal([Delete,Ended],l.Handle(e)); Assert.IsType<RecordingPhase.Cancelled>(l.Phase);
        }
    }
    private static RecordingEvent[] LateEvents => [new RecordingEvent.Start(Path),new RecordingEvent.CaptureStarted(),new RecordingEvent.CaptureFailedToStart(Failure),
        new RecordingEvent.StopRequested(),new RecordingEvent.CancelRequested(),new RecordingEvent.OutputFinished(),new RecordingEvent.OutputFailed(Failure,true),
        new RecordingEvent.StreamStopped(Failure),new RecordingEvent.WindowStreamFailed(Failure),new RecordingEvent.StopTimedOut(false)];
    [Theory, InlineData(0), InlineData(1), InlineData(2)] public void TerminalCallbacksAreIdempotent(int ending)
    {
        var l=Started(); l.Handle(ending==2?new RecordingEvent.CancelRequested():new RecordingEvent.StopRequested());
        l.Handle(ending==1?new RecordingEvent.OutputFailed(Failure,false):new RecordingEvent.OutputFinished()); var phase=l.Phase;
        foreach(var e in LateEvents) { Assert.Empty(l.Handle(e)); Assert.Equal(phase,l.Phase); }
    }
    [Fact] public void IdleIgnoresCallbacks()
    {
        var l=new RecordingLifecycle(); foreach(var e in LateEvents.Skip(1)) { Assert.Empty(l.Handle(e)); Assert.IsType<RecordingPhase.Idle>(l.Phase); }
    }
    [Fact] public void DisplayDisconnection()
    {
        Assert.False(RecordingLifecycle.IsRecordedDisplayGone("display2",["display1","display2"]));
        Assert.True(RecordingLifecycle.IsRecordedDisplayGone("display2",["display1"])); Assert.True(RecordingLifecycle.IsRecordedDisplayGone("display2",[]));
    }
    [Fact] public void DisplayChangesStopOnlyRecordedBoundsAndDpi()
    {
        var d=new DisplayInfo("display",new(-1000,0,1000,800),new(-1000,0,1000,760),144,false);
        Assert.False(RecordingLifecycle.IsRecordedDisplayChanged(d,[d]));
        Assert.False(RecordingLifecycle.IsRecordedDisplayChanged(d,[d with { WorkArea=new(-1000,0,1000,700),IsPrimary=true }]));
        Assert.True(RecordingLifecycle.IsRecordedDisplayChanged(d,[d with { Bounds=new(0,0,1000,800) }]));
        Assert.True(RecordingLifecycle.IsRecordedDisplayChanged(d,[d with { Dpi=192 }])); Assert.True(RecordingLifecycle.IsRecordedDisplayChanged(d,[]));
    }
}
