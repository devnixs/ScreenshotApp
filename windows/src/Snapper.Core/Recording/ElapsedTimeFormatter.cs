using System.Globalization;
namespace Snapper.Core.Recording;

public static class ElapsedTimeFormatter
{
    public static string Format(TimeSpan elapsed)
    {
        var seconds = Math.Max(0, elapsed.Ticks / TimeSpan.TicksPerSecond);
        return seconds < 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }
}
