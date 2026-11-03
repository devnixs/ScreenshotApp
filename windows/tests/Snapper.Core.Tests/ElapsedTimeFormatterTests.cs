using System.Globalization;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class ElapsedTimeFormatterTests
{
    [Theory, InlineData(0,"0:00"), InlineData(999,"0:00"), InlineData(59000,"0:59"), InlineData(60000,"1:00"),
        InlineData(61000,"1:01"), InlineData(3599000,"59:59"), InlineData(3600000,"1:00:00"),
        InlineData(36309000,"10:05:09"), InlineData(-5000,"0:00"), InlineData(-999,"0:00"), InlineData(360000000,"100:00:00")]
    public void TruncatesAndFormatsInvariantly(long milliseconds, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "ar-SA" })
            {
                CultureInfo.CurrentCulture = new(culture);
                Assert.Equal(expected,ElapsedTimeFormatter.Format(TimeSpan.FromMilliseconds(milliseconds)));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact] public void SubsecondTickBoundariesDoNotRoundUp()
    {
        Assert.Equal("0:00",ElapsedTimeFormatter.Format(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - 1)));
        Assert.Equal("0:01",ElapsedTimeFormatter.Format(TimeSpan.FromTicks(TimeSpan.TicksPerSecond)));
        Assert.Equal("0:00",ElapsedTimeFormatter.Format(TimeSpan.MinValue));
    }
}
