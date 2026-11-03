using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class GifFrameSamplerTests
{
    private static long T(double seconds) => (long)Math.Round(seconds*10_000_000,MidpointRounding.AwayFromZero);
    [Theory,InlineData(10,3,10),InlineData(15,2,15)]
    public void UniformFramesHaveCorrectIndicesAndDuration(int fps,int stride,int count)
    {
        var plan=GifFrameSampler.Plan(Enumerable.Range(0,30).Select(i=>T(i/30.0)).ToArray(),T(1),fps);
        Assert.Equal(Enumerable.Range(0,count).Select(i=>i*stride),plan.Select(f=>f.SourceIndex));
        Assert.Equal(100,plan.Sum(f=>f.DelayCentiseconds));
        Assert.All(plan,f=>Assert.Contains(f.DelayCentiseconds,fps==10 ? new[]{10} : new[]{6,7}));
    }
    [Fact] public void TruncatedTimestampsHitSlots()
    {
        var plan=GifFrameSampler.Plan(Enumerable.Range(0,30).Select(i=>i*10_000_000L/30).ToArray(),T(1),15);
        Assert.Equal(Enumerable.Range(0,15).Select(i=>i*2),plan.Select(f=>f.SourceIndex)); Assert.Equal(100,plan.Sum(f=>f.DelayCentiseconds));
    }
    [Fact] public void VfrPreservesGaps() => Assert.Equal(new[]{50,150,100},GifFrameSampler.Plan([0,T(.5),T(2)],T(3),10).Select(f=>f.DelayCentiseconds));
    [Fact] public void BurstSelectsSlotsAndHoldsLast()
    {
        var plan=GifFrameSampler.Plan([0,T(.01),T(.02),T(.03),T(.2)],T(.2),10);
        Assert.Equal(new[]{0,4},plan.Select(f=>f.SourceIndex)); Assert.Equal(new[]{20,10},plan.Select(f=>f.DelayCentiseconds));
    }
    [Fact] public void NonzeroStartUsesAbsoluteSlotsRelativeDelays() => Assert.Equal(new[]{5,15,25},GifFrameSampler.Plan([T(.35),T(.40),T(.55)],T(.8),10).Select(f=>f.DelayCentiseconds));
    [Fact] public void ClampDebtIsCarried() => Assert.Equal(new[]{2,19},GifFrameSampler.Plan([T(.095),T(.1)],T(.3),10).Select(f=>f.DelayCentiseconds));
    [Fact] public void LongUniformSequenceDoesNotAccumulateRounding()
    {
        var plan=GifFrameSampler.Plan(Enumerable.Range(0,1800).Select(i=>T(i/30.0)).ToArray(),T(60),15);
        Assert.Equal(900,plan.Count); Assert.Equal(6000,plan.Sum(f=>f.DelayCentiseconds)); Assert.All(plan,f=>Assert.Contains(f.DelayCentiseconds,new[]{6,7}));
    }
    [Fact] public void SingleUnknownFrameRoundsHalfAway() => Assert.Equal(new GifFrame(0,0,13),Assert.Single(GifFrameSampler.Plan([0],0,8)));
    [Theory,InlineData(700,65535,4465),InlineData(655.36,65534,2)]
    public void LongDelaysSplitWithoutTinyRemainders(double duration,int first,int second)
    {
        var plan=GifFrameSampler.Plan([0],T(duration),10);
        Assert.Equal(new[]{first,second},plan.Select(f=>f.DelayCentiseconds)); Assert.All(plan,f=>Assert.Equal(0,f.SourceIndex));
    }
    [Fact] public void SortsAndDeduplicatesBeforeIndexing()
    {
        var plan=GifFrameSampler.Plan([T(.2),0,T(.1),T(.1)],T(.3),10);
        Assert.Equal(new[]{0,1,2},plan.Select(f=>f.SourceIndex)); Assert.Equal(new[]{10,10,10},plan.Select(f=>f.DelayCentiseconds));
    }
    [Fact] public void EmptyAndInvalidFps() { Assert.Empty(GifFrameSampler.Plan([],0,10)); Assert.Throws<ArgumentOutOfRangeException>(()=>GifFrameSampler.Plan([],0,0)); }
    [Fact] public void LargeAbsoluteTimeDoesNotOverflow()
    {
        var first=long.MaxValue-20_000_000;
        var plan=GifFrameSampler.Plan([first,first+10_000_000],long.MaxValue,15);
        Assert.Equal(200,plan.Sum(f=>f.DelayCentiseconds));
    }
}
