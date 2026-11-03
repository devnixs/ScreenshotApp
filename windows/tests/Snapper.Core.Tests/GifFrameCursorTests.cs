using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class GifFrameCursorTests
{
    private static readonly GifFrame[] Plan=[new(0,0,10),new(1,1_000_000,10),new(2,2_000_000,10)];
    [Fact] public void PairsDecodedTimesWithTolerance()
    {
        var cursor=new GifFrameCursor(Plan);
        Assert.Equal(Plan.Take(1),cursor.Take(0)); Assert.Empty(cursor.Take(333_333));
        Assert.Equal(Plan.Skip(1).Take(1),cursor.Take(999_990)); Assert.Equal(Plan.Skip(2),cursor.Take(2_000_000)); Assert.True(cursor.IsComplete);
    }
    [Fact] public void SkippedDecodeReturnsEveryOwedFrame()
    { var cursor=new GifFrameCursor(Plan); cursor.Take(0); Assert.Equal(Plan.Skip(1),cursor.Take(2_500_000)); }
    [Fact] public void SplitDelayReturnsTogether()
    {
        GifFrame[] frames=[new(0,0,65535),new(0,0,100)]; var cursor=new GifFrameCursor(frames);
        Assert.Equal(frames,cursor.Take(0)); Assert.True(cursor.IsComplete);
    }
    [Fact] public void BackwardsDoesNotConsumeAndRemainingDrains()
    {
        var cursor=new GifFrameCursor(Plan); cursor.Take(500_000); Assert.Empty(cursor.Take(0));
        Assert.Equal(Plan.Skip(1),cursor.Remaining()); Assert.Empty(cursor.Remaining()); Assert.True(cursor.IsComplete);
    }
    [Fact] public void ToleranceAdditionDoesNotOverflow()
    { var frame=new GifFrame(0,long.MaxValue,2); Assert.Equal(new[]{frame},new GifFrameCursor([frame]).Take(long.MaxValue)); }
}
