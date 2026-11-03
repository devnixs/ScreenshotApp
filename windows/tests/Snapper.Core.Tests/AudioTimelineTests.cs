using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class AudioTimelineTests
{
    private const long T0 = 1_000_000_000;
    private static AudioTimeline New(long origin = T0) { var t = new AudioTimeline(48000); t.SetOrigin(origin); return t; }
    [Fact] public void ThrowsBeforeOrigin() => Assert.Throws<InvalidOperationException>(() => new AudioTimeline(48000).Place(0, 480));
    [Fact] public void OriginCanOnlyBeSetOnce() => Assert.Throws<InvalidOperationException>(() => New().SetOrigin(T0));
    [Fact] public void ContiguousSamplesPlacedBackToBack()
    {
        var t = New(); Assert.Equal(new[] { new AudioPlacement(0, 0, 480, false) }, t.Place(T0, 480));
        Assert.Equal(new[] { new AudioPlacement(100000, 0, 480, false) }, t.Place(T0 + 100000, 480));
        Assert.Equal(960, t.FramesWritten); Assert.Equal(200000, t.CurrentEndTicks);
    }
    [Fact] public void JitterWithinToleranceAbsorbed()
    { var t = New(); t.Place(T0, 480); Assert.Equal(new[] { new AudioPlacement(100000, 0, 480, false) }, t.Place(T0 + 150000, 480)); }
    [Fact] public void GapInsertsSilence()
    {
        var t = New(); t.Place(T0, 480);
        Assert.Equal(new[] { new AudioPlacement(100000, 0, 2400, true), new AudioPlacement(600000, 0, 480, false) }, t.Place(T0 + 600000, 480));
    }
    [Fact] public void OverlapTrimsLeadingFrames()
    {
        var t = New(); t.Place(T0, 4800);
        Assert.Equal(new[] { new AudioPlacement(1000000, 1440, 3360, false) }, t.Place(T0 + 700000, 4800));
    }
    [Fact] public void PreOriginSampleTrimmed() => Assert.Equal(new[] { new AudioPlacement(0, 2400, 2400, false) }, New().Place(T0 - 500000, 4800));
    [Fact] public void EntirelyPreOriginDropped() { var t = New(); Assert.Empty(t.Place(T0 - 400000, 480)); Assert.Equal(0, t.FramesWritten); }
    [Fact] public void EndClipsStraddlingSample()
    {
        var t = New(); t.SetEnd(T0 + 10000000); t.Place(T0, 45600);
        Assert.Equal(new[] { new AudioPlacement(9500000, 0, 2400, false) }, t.Place(T0 + 9500000, 4800));
        Assert.Equal(48000, t.FramesWritten);
    }
    [Theory] [InlineData(10000000)] [InlineData(11000000)]
    public void SampleAfterEndDropped(long offset) { var t = New(); t.SetEnd(T0 + 10000000); Assert.Empty(t.Place(T0 + offset, 480)); }
    [Fact] public void EndMayBeSetBeforeOrigin()
    { var t = new AudioTimeline(48000); t.SetEnd(T0 + 100000); t.SetOrigin(T0); Assert.Equal(480, Assert.Single(t.Place(T0, 4800)).FrameCount); }
    [Fact] public void LargeQpcValuesDoNotOverflow()
    { const long origin = 900_000_000_000_000_000; var t = New(origin); t.Place(origin, 480); Assert.Equal(100000, Assert.Single(t.Place(origin + 100000, 480)).OutputTimeTicks); }
    [Fact] public void DriftCorrection()
    {
        var t = New(); long silence = 0;
        for (var i = 0; i < 100000; i++)
        {
            var before = t.CurrentEndTicks;
            foreach (var p in t.Place(T0 + i * 100020L, 480))
            { Assert.True(p.OutputTimeTicks >= before); before = p.OutputTimeTicks; if (p.IsSilence) silence += p.FrameCount; }
        }
        Assert.InRange(silence, 9600 - 960, 9600 + 960);
        Assert.InRange(t.FramesWritten - 480L * 100000, 8640, 10560);
    }
    [Fact] public void FractionalRateUsesCumulativeFrameTimes()
    {
        var t = new AudioTimeline(44100); t.SetOrigin(T0);
        t.Place(T0, 1); var p = Assert.Single(t.Place(T0, 1)); Assert.Equal(226, p.OutputTimeTicks); Assert.Equal(453, t.CurrentEndTicks);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] public void InvalidSampleRateRejected(int rate) => Assert.Throws<ArgumentOutOfRangeException>(() => new AudioTimeline(rate));
}
