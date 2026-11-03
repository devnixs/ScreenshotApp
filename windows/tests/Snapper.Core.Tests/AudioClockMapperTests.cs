using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class AudioClockMapperTests
{
    [Fact] public void SystemTimeDomainPassesThrough()
    {
        var clock = new AudioClockMapper(); Assert.Null(clock.Domain);
        Assert.Equal(4_999_950_000, clock.ToQpc(4_999_950_000, 100_000, 5_000_000_000));
        Assert.Equal(AudioClockDomain.SystemTime, clock.Domain);
        Assert.Equal(5_000_050_000, clock.ToQpc(5_000_050_000, 100_000, 5_000_100_000));
    }
    [Fact] public void RelativeDomainAnchorsFirstSampleEndAtReceipt()
    {
        var clock = new AudioClockMapper();
        Assert.Equal(4_999_900_000, clock.ToQpc(0, 100_000, 5_000_000_000));
        Assert.Equal(AudioClockDomain.Relative, clock.Domain);
        Assert.Equal(5_000_000_000, clock.ToQpc(100_000, 100_000, 5_000_100_000));
    }
    [Fact] public void DomainDecidedOnce()
    {
        var clock = new AudioClockMapper(); clock.ToQpc(0, 100_000, 5_000_000_000);
        Assert.Equal(9_999_900_000, clock.ToQpc(5_000_000_000, 100_000, 5_000_000_000));
    }
    [Theory] [InlineData(10_000_000, AudioClockDomain.SystemTime)] [InlineData(10_000_001, AudioClockDomain.Relative)]
    [InlineData(-10_000_000, AudioClockDomain.SystemTime)] [InlineData(-10_000_001, AudioClockDomain.Relative)]
    public void ToleranceBoundary(long delta, AudioClockDomain domain)
    { var clock = new AudioClockMapper(); clock.ToQpc(5_000_000_000 + delta, 100_000, 5_000_000_000); Assert.Equal(domain, clock.Domain); }
    [Fact] public void DifferenceDoesNotOverflow()
    { var clock = new AudioClockMapper(); Assert.Equal(long.MaxValue - 100, clock.ToQpc(long.MinValue + 100, 100, long.MaxValue)); }
}
