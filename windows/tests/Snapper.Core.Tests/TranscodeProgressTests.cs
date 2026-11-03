using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class TranscodeProgressTests
{
    [Fact] public void VideoOnlySampleTickAndEnd()
    {
        var p=new TranscodeProgress(false);
        Assert.Equal(TranscodeStep.WriteSample,p.OnRead(TranscodeStream.Video,false,false,true));
        Assert.Equal(TranscodeStep.SendStreamTick,p.OnRead(TranscodeStream.Video,false,true,false));
        Assert.Equal(TranscodeStep.Finish,p.OnRead(TranscodeStream.Video,true,false,false));
        Assert.Equal(1,p.VideoFramesWritten); Assert.Null(p.FailureMessage);
        Assert.Equal(TranscodeStep.Finish,p.OnRead(TranscodeStream.Video,false,false,true)); Assert.Equal(1,p.VideoFramesWritten);
    }
    [Fact] public void AudioMustDrainAfterVideo()
    {
        var p=new TranscodeProgress(true);
        p.OnRead(TranscodeStream.Video,false,false,true);
        Assert.Equal(TranscodeStep.Continue,p.OnRead(TranscodeStream.Video,true,false,false));
        Assert.Equal(TranscodeStep.WriteSample,p.OnRead(TranscodeStream.Audio,false,false,true));
        Assert.Equal(1,p.VideoFramesWritten);
        Assert.Equal(TranscodeStep.Finish,p.OnRead(TranscodeStream.Audio,true,false,false)); Assert.Null(p.FailureMessage);
    }
    [Fact] public void NoVideoFramesFailsAtFinish()
    {
        var p=new TranscodeProgress(false); Assert.Equal(TranscodeStep.Finish,p.OnRead(TranscodeStream.Video,true,false,false));
        Assert.Equal("The recording has no video frames.",p.FailureMessage);
    }
    [Fact] public void AudioDoesNotCountAsVideo()
    {
        var p=new TranscodeProgress(true); p.OnRead(TranscodeStream.Audio,true,false,true);
        Assert.Equal(0,p.VideoFramesWritten); p.OnRead(TranscodeStream.Video,true,false,false);
        Assert.Equal("The recording has no video frames.",p.FailureMessage);
    }
    [Fact] public void EosWithSampleWritesBeforeFinishing()
    {
        var p=new TranscodeProgress(false); Assert.Equal(TranscodeStep.WriteSample,p.OnRead(TranscodeStream.Video,true,false,true));
        Assert.Equal(1,p.VideoFramesWritten); Assert.Equal(TranscodeStep.Finish,p.OnRead(TranscodeStream.Video,false,false,false)); Assert.Null(p.FailureMessage);
    }
    [Fact] public void SampleTakesPriorityOverTick()
    { var p=new TranscodeProgress(false); Assert.Equal(TranscodeStep.WriteSample,p.OnRead(TranscodeStream.Video,false,true,true)); }
    [Fact] public void AudioMayFinishFirst()
    {
        var p=new TranscodeProgress(true); Assert.Equal(TranscodeStep.Continue,p.OnRead(TranscodeStream.Audio,true,false,false));
        p.OnRead(TranscodeStream.Video,false,false,true); Assert.Equal(TranscodeStep.Finish,p.OnRead(TranscodeStream.Video,true,false,false));
    }
}
