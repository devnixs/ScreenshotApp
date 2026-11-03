namespace Snapper.Core.Recording;

public enum TranscodeStream { Video, Audio }
public enum TranscodeStep { WriteSample, SendStreamTick, Continue, Finish }
public sealed class TranscodeProgress(bool hasAudio)
{
    private bool videoDone, audioDone = !hasAudio, finished;
    public int VideoFramesWritten { get; private set; }
    public string? FailureMessage { get; private set; }
    public TranscodeStep OnRead(TranscodeStream stream, bool endOfStream, bool streamTick, bool hasSample)
    {
        if (finished) return TranscodeStep.Finish;
        if (endOfStream) { if (stream == TranscodeStream.Video) videoDone = true; else audioDone = true; }
        // EOS may carry the final sample. Emit it before the next call observes completion.
        if (hasSample) { if (stream == TranscodeStream.Video) VideoFramesWritten++; return TranscodeStep.WriteSample; }
        if (streamTick) return TranscodeStep.SendStreamTick;
        if (!videoDone || !audioDone) return TranscodeStep.Continue;
        finished = true;
        if (VideoFramesWritten == 0) FailureMessage = "The recording has no video frames.";
        return TranscodeStep.Finish;
    }
}
