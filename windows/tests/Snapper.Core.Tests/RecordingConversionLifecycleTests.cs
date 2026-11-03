using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingConversionLifecycleTests
{
    private const string Capture="/temp/capture.mp4", Final="/saved/recording.mp4";
    private static readonly RecordingFailure Failure=new("Writer failed");
    private static readonly RecordingEffect Stop=new RecordingEffect.StopCapture(), Release=new RecordingEffect.ReleaseCapture(),
        Convert=new RecordingEffect.Convert(Capture), Ended=new RecordingEffect.Ended(), Delete=new RecordingEffect.DeleteFile(Capture),
        Save=new RecordingEffect.SaveOriginal(Capture), Error=new RecordingEffect.ShowError(Failure);
    private static RecordingLifecycle Started()
    {
        var l=new RecordingLifecycle(); l.Handle(new RecordingEvent.Start(Capture,true)); l.Handle(new RecordingEvent.CaptureStarted()); return l;
    }
    private static RecordingLifecycle Converting()
    {
        var l=Started(); l.Handle(new RecordingEvent.StopRequested());
        Assert.Equal([Release,Convert],l.Handle(new RecordingEvent.OutputFinished())); Assert.True(l.IsConverting); return l;
    }
    [Fact] public void StopReleasesCaptureAndConvertsWithoutEnding()
    {
        var l=Converting(); Assert.IsType<RecordingPhase.Converting>(l.Phase);
    }
    [Fact] public void SpontaneousCompletionStopsCapture() => Assert.Equal([Stop,Release,Convert],Started().Handle(new RecordingEvent.OutputFinished()));
    [Fact] public void ConversionSuccessDeletesSourceAndRevealsFinalOnce()
    {
        var l=Converting(); Assert.Equal([Delete,new RecordingEffect.Reveal(Final),Ended],l.Handle(new RecordingEvent.ConversionFinished(Final)));
        Assert.Equal(new RecordingPhase.Finished(Final),l.Phase); Assert.False(l.IsConverting);
        Assert.Empty(l.Handle(new RecordingEvent.ConversionFinished(Final))); Assert.Empty(l.Handle(new RecordingEvent.ConversionFailed(Failure,null)));
    }
    [Theory,InlineData(true),InlineData(false)] public void ConversionFailureRevealsSavedOriginalOrDeletesTemp(bool saved)
    {
        var l=Converting(); var error=RecordingFailure.Compression("bad media");
        Assert.Equal([saved?new RecordingEffect.Reveal(Final):Delete,new RecordingEffect.ShowError(error),Ended],
            l.Handle(new RecordingEvent.ConversionFailed(error,saved?Final:null)));
        Assert.Equal(saved?(RecordingPhase)new RecordingPhase.Finished(Final):new RecordingPhase.Failed(error),l.Phase);
    }
    [Fact] public void ConvertingIgnoresAllCaptureAndStopCallbacks()
    {
        var l=Converting();
        foreach(var e in new RecordingEvent[]{new RecordingEvent.StopRequested(),new RecordingEvent.CancelRequested(),new RecordingEvent.StopTimedOut(true),
            new RecordingEvent.StreamStopped(Failure),new RecordingEvent.WindowStreamFailed(Failure),new RecordingEvent.OutputFinished(),
            new RecordingEvent.OutputFailed(Failure,true),new RecordingEvent.CaptureStarted(),new RecordingEvent.CaptureFailedToStart(Failure)})
        { Assert.Empty(l.Handle(e)); Assert.True(l.IsConverting); }
    }
    [Fact] public void CancelDoesNotConvert()
    {
        var l=Started(); l.Handle(new RecordingEvent.CancelRequested()); Assert.Equal([Delete,Ended],l.Handle(new RecordingEvent.OutputFinished()));
        Assert.IsType<RecordingPhase.Cancelled>(l.Phase);
    }
    [Theory,InlineData(true),InlineData(false)] public void WriteFailureMovesOnlyNonemptyOriginal(bool content)
    { Assert.Equal([Stop,content?Save:Delete,Error,Ended],Started().Handle(new RecordingEvent.OutputFailed(Failure,content))); }
    [Fact] public void TimeoutMovesPartialWithoutConverting()
    {
        var l=Started(); l.Handle(new RecordingEvent.StopRequested());
        Assert.Equal([Save,new RecordingEffect.ShowError(RecordingFailure.Timeout),Ended],l.Handle(new RecordingEvent.StopTimedOut(true)));
    }
    [Fact] public void WindowFailureMovesPartialAndReportsOriginalError()
    {
        var l=Started(); l.Handle(new RecordingEvent.WindowStreamFailed(Failure)); Assert.Equal([Save,Error,Ended],l.Handle(new RecordingEvent.OutputFinished()));
    }
    [Theory,InlineData(true),InlineData(false)] public void CancelWinsFailuresWithoutSaving(bool content)
    {
        var l=Started(); l.Handle(new RecordingEvent.CancelRequested()); Assert.Equal([Delete,Ended],l.Handle(new RecordingEvent.OutputFailed(Failure,content)));
    }
    [Fact] public void FailureTitles()
    {
        Assert.Equal("Couldn't record the screen",Failure.Title); var f=RecordingFailure.Compression("x");
        Assert.Equal("Couldn't compress the recording",f.Title); Assert.Equal("x",f.Message);
    }
}
