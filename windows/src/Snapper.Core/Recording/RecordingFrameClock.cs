namespace Snapper.Core.Recording;

/// <summary>Slot pacing retains real QPC capture times without accumulating refresh-rate jitter.</summary>
public sealed class RecordingFrameClock
{
    private readonly double interval;
    private readonly long origin;
    private long lastSlot, lastAccepted;
    public RecordingFrameClock(double frameIntervalTicks, long originTicks)
    {
        if (!double.IsFinite(frameIntervalTicks) || frameIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(frameIntervalTicks));
        interval = frameIntervalTicks; origin = originTicks;
    }
    public long AcceptedCount { get; private set; }
    public long? LastSampleTime => AcceptedCount == 0 ? null : lastAccepted - origin;
    public long? Accept(long captureTicks)
    {
        if (captureTicks < origin || AcceptedCount > 0 && captureTicks <= lastAccepted) return null;
        var slot = (long)Math.Floor((captureTicks - origin) / interval);
        if (AcceptedCount > 0 && slot <= lastSlot) return null;
        lastSlot = slot; lastAccepted = captureTicks; AcceptedCount++;
        return captureTicks - origin;
    }
    public long? FinalFrameTime(long stopTicks) => AcceptedCount > 0 && stopTicks - lastAccepted >= interval ? stopTicks - origin : null;
}
