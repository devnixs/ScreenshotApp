namespace Snapper.Core.Recording;

public sealed class GifFrameCursor(IReadOnlyList<GifFrame> plan)
{
    private int index;
    private long? lastTimestamp;
    public bool IsComplete => index == plan.Count;
    public IReadOnlyList<GifFrame> Take(long timestamp)
    {
        if (lastTimestamp is { } previous && timestamp < previous) return [];
        lastTimestamp = timestamp;
        var frames = new List<GifFrame>();
        while (index < plan.Count && (Int128)plan[index].SourceTimestamp <= (Int128)timestamp + GifFrameSampler.SlotToleranceTicks)
            frames.Add(plan[index++]);
        return frames;
    }
    public IReadOnlyList<GifFrame> Remaining()
    { var remaining = plan.Skip(index).ToArray(); index = plan.Count; return remaining; }
}
