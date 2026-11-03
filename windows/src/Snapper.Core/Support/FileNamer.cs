using System.Globalization;

namespace Snapper.Core.Support;

public sealed class FileNamer(TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo timeZone = timeZone ?? TimeZoneInfo.Local;
    public static StringComparer NameComparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Pass the capture time for screenshots or recording start time from an injected
    /// TimeProvider (TimeProvider.System in production). This type never reads the clock.</summary>
    public string BaseName(CaptureKind kind, DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        return string.Create(CultureInfo.InvariantCulture,
            $"{kind.NamePrefix()} {local.Year:D4}-{local.Month:D2}-{local.Day:D2} at {local.Hour:D2}.{local.Minute:D2}.{local.Second:D2}");
    }

    /// <summary>Uses the caller's capture or recording start time, obtained from an injected TimeProvider.</summary>
    public string FileName(CaptureKind kind, DateTimeOffset instant) => $"{BaseName(kind, instant)}.{kind.FileExtension()}";

    /// <summary>Returns the first unused full name, without reserving it. Pass the capture or recording
    /// start time from an injected TimeProvider. In-memory name sets must use NameComparer.</summary>
    public string UniqueFileName(CaptureKind kind, DateTimeOffset instant, Func<string, bool> exists)
    {
        var stem = BaseName(kind, instant);
        var extension = kind.FileExtension();
        var candidate = $"{stem}.{extension}";
        long suffix = 2;
        while (exists(candidate))
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({suffix}).{extension}");
            suffix++;
        }
        return candidate;
    }
}
