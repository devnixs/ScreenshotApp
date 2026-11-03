namespace Snapper.Core.Recording;
public readonly record struct AudioPlacement(long OutputTimeTicks, int SkipFrames, int FrameCount, bool IsSilence);
/// <summary>Caller serializes access. Frame-count placement absorbs small jitter and reanchors gaps/overlap to QPC.</summary>
public sealed class AudioTimeline
{
    private readonly int rate, tolerance;
    private long? origin, end;
    public AudioTimeline(int sampleRate, int? jitterToleranceFrames = null)
    {
        if (sampleRate <= 0 || jitterToleranceFrames < 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        rate = sampleRate; tolerance = jitterToleranceFrames ?? sampleRate / 50;
    }
    public long FramesWritten { get; private set; }
    public long CurrentEndTicks => Ticks(FramesWritten);
    public void SetOrigin(long t0Qpc) { if (origin is not null) throw new InvalidOperationException("Audio origin already set."); origin = t0Qpc; }
    public void SetEnd(long endQpc) => end = endQpc;
    private long Frames(Int128 ticks)
    {
        var product = ticks * rate;
        return checked((long)(product >= 0 ? (product + 5_000_000) / 10_000_000 : -((-product + 5_000_000) / 10_000_000)));
    }
    private long Ticks(long frames) => checked((long)((Int128)frames * 10_000_000 / rate));
    public IReadOnlyList<AudioPlacement> Place(long sampleQpc, int frameCount)
    {
        if (origin is not { } t0) throw new InvalidOperationException("Audio origin isn't set.");
        if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (frameCount == 0) return [];
        var start = Frames((Int128)sampleQpc - t0);
        var endFrame = end is { } e ? Math.Max(0, Frames((Int128)e - t0)) : long.MaxValue;
        if (start >= endFrame || FramesWritten >= endFrame) return [];
        var result = new List<AudioPlacement>();
        var delta = start - FramesWritten;
        var skip = 0;
        if (delta > tolerance)
        {
            var gap = Math.Min(delta, endFrame - FramesWritten);
            while (gap > 0)
            {
                var count = (int)Math.Min(gap, int.MaxValue);
                result.Add(new(Ticks(FramesWritten), 0, count, true)); FramesWritten += count; gap -= count;
            }
        }
        else if (delta < -tolerance)
        {
            skip = (int)Math.Min(frameCount, -delta);
            frameCount -= skip;
        }
        frameCount = (int)Math.Min(frameCount, endFrame - FramesWritten);
        if (frameCount > 0)
        { result.Add(new(Ticks(FramesWritten), skip, frameCount, false)); FramesWritten += frameCount; }
        return result;
    }
}
