using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingFrameClockTests
{
    private const double Interval = 10_000_000.0 / 30;
    [Fact] public void UsesSharedOriginAndDropsEarlierFrames()
    {
        var c = new RecordingFrameClock(Interval, 1_000_000);
        Assert.Null(c.Accept(900_000)); Assert.Equal(250_000, c.Accept(1_250_000));
        Assert.Equal(1, c.AcceptedCount); Assert.Equal(250_000, c.LastSampleTime);
    }
    [Theory, InlineData(60), InlineData(120), InlineData(144), InlineData(30)]
    public void HighRefreshSourcesYieldThirtyRealTimestamps(int refresh)
    {
        var c = new RecordingFrameClock(Interval, 0);
        var received = new List<long>();
        var times = Enumerable.Range(0, refresh).Select(k => (long)Math.Ceiling(k * 10_000_000.0 / refresh)).ToArray();
        foreach (var t in times) if (c.Accept(t) is { } sample) { Assert.Equal(t, sample); received.Add(sample); }
        Assert.Equal(30, received.Count); Assert.Equal(30, c.AcceptedCount);
        Assert.True(received.Zip(received.Skip(1), (a,b) => a < b).All(x => x));
    }
    [Fact] public void RoundedSixtyHertzAndThirtyHertz()
    {
        var sixty = new RecordingFrameClock(Interval, 0);
        Assert.Equal(30, Enumerable.Range(0,60).Count(k => sixty.Accept(k * 166_667L) is not null));
        var thirty = new RecordingFrameClock(Interval, 0);
        Assert.Equal(30, Enumerable.Range(0,30).Count(k => thirty.Accept(k * 333_334L) is not null));
    }
    [Fact] public void IrregularFramesKeepRealTimeAndRejectSameSlot()
    {
        var c = new RecordingFrameClock(Interval,0);
        Assert.Equal(0,c.Accept(0)); Assert.Equal(400_000,c.Accept(400_000)); Assert.Null(c.Accept(450_000)); Assert.Equal(700_000,c.Accept(700_000));
        Assert.Equal(3,c.AcceptedCount); Assert.Equal(700_000,c.LastSampleTime);
    }
    [Fact] public void DropsDuplicatesAndOutOfOrderFrames()
    {
        var c = new RecordingFrameClock(Interval,0); c.Accept(400_000);
        Assert.Null(c.Accept(400_000)); Assert.Null(c.Accept(300_000)); Assert.Null(c.Accept(-1));
        Assert.Equal(1,c.AcceptedCount); Assert.Equal(400_000,c.LastSampleTime);
    }
    [Theory, InlineData(0), InlineData(5_000_000)] public void StaticScreenHoldsUntilStop(long origin)
    {
        var c = new RecordingFrameClock(Interval, origin); c.Accept(origin);
        Assert.Equal(100_000_000,c.FinalFrameTime(origin + 100_000_000));
    }
    [Fact] public void DoesNotAddHoldWithoutFramesOrInsideInterval()
    {
        var c = new RecordingFrameClock(Interval,0); Assert.Null(c.FinalFrameTime(100_000_000)); Assert.Null(c.LastSampleTime);
        c.Accept(1_000_000); Assert.Null(c.FinalFrameTime(1_100_000)); Assert.Null(c.FinalFrameTime(900_000));
        Assert.Equal(1_333_334,c.FinalFrameTime(1_333_334));
    }
    [Theory, InlineData(0), InlineData(-1), InlineData(double.NaN), InlineData(double.PositiveInfinity)]
    public void RejectsInvalidInterval(double interval) => Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingFrameClock(interval,0));
}
