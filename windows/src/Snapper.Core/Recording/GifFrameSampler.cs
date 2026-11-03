namespace Snapper.Core.Recording;

public readonly record struct GifFrame(int SourceIndex, long SourceTimestamp, int DelayCentiseconds);
public static class GifFrameSampler
{
    public const long TicksPerSecond = 10_000_000, SlotToleranceTicks = 10_000;
    public const int MinDelayCentiseconds = 2, MaxDelayCentiseconds = 65_535;
    public static IReadOnlyList<GifFrame> Plan(IReadOnlyList<long> timestamps, long durationTicks, int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
        var times = timestamps.Distinct().Order().ToArray();
        if (times.Length == 0) return [];
        var selected = new List<int> { 0 };
        // Int128 keeps absolute slot comparisons safe even for the full Int64 timestamp domain.
        Int128 Slot(long t) => Floor(((Int128)t + SlotToleranceTicks) * framesPerSecond, TicksPerSecond) + 1;
        var next = Slot(times[0]);
        for (var i = 1; i < times.Length; i++)
            if (((Int128)times[i] + SlotToleranceTicks) * framesPerSecond >= next * TicksPerSecond)
            { selected.Add(i); next = Slot(times[i]); }
        var result = new List<GifFrame>();
        Int128 emitted = 0;
        for (var k = 0; k < selected.Count; k++)
        {
            var index = selected[k];
            var end = k + 1 < selected.Count ? times[selected[k + 1]]
                : Int128.Max(Math.Max(0, durationTicks), (Int128)times[index] + TicksPerSecond / framesPerSecond);
            var span = end - times[0];
            var target = RoundCentiseconds(span);
            var delay = Int128.Max(MinDelayCentiseconds, target - emitted);
            emitted += delay;
            while (delay > MaxDelayCentiseconds)
            {
                var piece = delay == MaxDelayCentiseconds + 1 ? MaxDelayCentiseconds - 1 : MaxDelayCentiseconds;
                result.Add(new(index, times[index], piece)); delay -= piece;
            }
            result.Add(new(index, times[index], (int)delay));
        }
        return result;
    }
    private static Int128 Floor(Int128 n, Int128 d) => n >= 0 ? n / d : -((-n + d - 1) / d);
    private static Int128 RoundCentiseconds(Int128 ticks) => ticks >= 0 ? (ticks + 50_000) / 100_000 : -((-ticks + 50_000) / 100_000);
}
