using System.Globalization;
using Snapper.Core.Support;

namespace Snapper.Core.Tests;

public sealed class FileNamerTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly DateTimeOffset Instant = new(2026, 11, 2, 18, 18, 4, TimeSpan.Zero);
    private readonly FileNamer namer = new(Paris);
    private const string Stem = "Screenshot 2026-11-02 at 19.18.04";

    [Theory]
    [InlineData(CaptureKind.Screenshot, "Screenshot 2026-11-02 at 19.18.04.png")]
    [InlineData(CaptureKind.RecordingMp4, "Recording 2026-11-02 at 19.18.04.mp4")]
    [InlineData(CaptureKind.RecordingGif, "Recording 2026-11-02 at 19.18.04.gif")]
    public void NamesMatchProductFormat(CaptureKind kind, string expected) => Assert.Equal(expected, namer.FileName(kind, Instant));

    [Fact] public void BaseNameHasNoExtension() => Assert.Equal(Stem, namer.BaseName(CaptureKind.Screenshot, Instant));

    [Theory]
    [InlineData(2027, 1, 5, 2, 4, 5, "Screenshot 2027-01-05 at 03.04.05.png")]
    [InlineData(2026, 11, 2, 23, 0, 0, "Screenshot 2026-11-03 at 00.00.00.png")]
    [InlineData(2026, 7, 2, 18, 18, 4, "Screenshot 2026-07-02 at 20.18.04.png")]
    public void PadsAndConvertsWallClock(int year, int month, int day, int hour, int minute, int second, string expected) =>
        Assert.Equal(expected, namer.FileName(CaptureKind.Screenshot, new(year, month, day, hour, minute, second, TimeSpan.Zero)));

    [Fact] public void RespectsInjectedTimeZone()
    {
        Assert.Equal(Stem + ".png", namer.FileName(CaptureKind.Screenshot, Instant));
        Assert.Equal("Screenshot 2026-11-02 at 18.18.04.png", new FileNamer(TimeZoneInfo.Utc).FileName(CaptureKind.Screenshot, Instant));
        Assert.Equal("Screenshot 2026-11-03 at 00.30.00.png", namer.FileName(CaptureKind.Screenshot, new(2026, 11, 2, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new FileNamer(TimeZoneInfo.Local).FileName(CaptureKind.Screenshot, Instant), new FileNamer().FileName(CaptureKind.Screenshot, Instant));
    }

    [Fact] public void DropsSubSecondPrecision() => Assert.Equal(Stem + ".png", namer.FileName(CaptureKind.Screenshot, Instant.AddMilliseconds(999)));

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void IgnoresCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(Stem + ".png", namer.FileName(CaptureKind.Screenshot, Instant));
            Assert.EndsWith("23.59.59.png", namer.FileName(CaptureKind.Screenshot, new(2026, 11, 2, 22, 59, 59, TimeSpan.Zero)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact] public void NoCollisionReturnsPlainName() => Assert.Equal(Stem + ".png", namer.UniqueFileName(CaptureKind.Screenshot, Instant, _ => false));
    [Fact] public void CollisionSequence()
    {
        var names = new HashSet<string>(FileNamer.NameComparer) { Stem + ".png" };
        for (var i = 2; i <= 4; i++)
        {
            var result = namer.UniqueFileName(CaptureKind.Screenshot, Instant, names.Contains);
            Assert.Equal($"{Stem} ({i}).png", result);
            names.Add(result);
        }
    }
    [Fact] public void CollisionFillsFirstGap()
    {
        var names = new HashSet<string>(FileNamer.NameComparer) { Stem + ".png", Stem + " (3).png" };
        Assert.Equal(Stem + " (2).png", namer.UniqueFileName(CaptureKind.Screenshot, Instant, names.Contains));
    }
    [Theory]
    [InlineData(CaptureKind.RecordingMp4, "mp4")]
    [InlineData(CaptureKind.RecordingGif, "gif")]
    public void CollisionPreservesExtension(CaptureKind kind, string extension)
    {
        var plain = namer.FileName(kind, Instant);
        Assert.Equal($"Recording 2026-11-02 at 19.18.04 (2).{extension}", namer.UniqueFileName(kind, Instant, name => name == plain));
    }
    [Fact] public void DifferentExtensionIsNotACollision() => Assert.Equal(Stem + ".png", namer.UniqueFileName(CaptureKind.Screenshot, Instant, name => name == Stem + ".mp4"));
    [Fact] public void PredicateReceivesFullNames()
    {
        var checkedNames = new List<string>();
        _ = namer.UniqueFileName(CaptureKind.Screenshot, Instant, name => { checkedNames.Add(name); return checkedNames.Count == 1; });
        Assert.Equal([Stem + ".png", Stem + " (2).png"], checkedNames);
    }
    [Fact] public void CaseInsensitiveSetCollides()
    {
        var names = new HashSet<string>(FileNamer.NameComparer) { (Stem + ".png").ToUpperInvariant() };
        Assert.Equal(Stem + " (2).png", namer.UniqueFileName(CaptureKind.Screenshot, Instant, names.Contains));
    }
    [Fact] public void NamesAreValidWindowsNames()
    {
        foreach (var kind in Enum.GetValues<CaptureKind>())
        {
            foreach (var name in new[] { namer.FileName(kind, Instant), namer.UniqueFileName(kind, Instant, name => !name.Contains("(12)", StringComparison.Ordinal)) })
            {
                Assert.DoesNotContain(name, c => c < 0x20 || "<>:\"/\\|?*".Contains(c));
                Assert.False(name.EndsWith('.') || name.EndsWith(' '));
                Assert.DoesNotContain(Path.GetFileNameWithoutExtension(name).ToUpperInvariant(), new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" });
            }
        }
    }
    [Fact] public void CaptureKindPrefixesAndExtensions()
    {
        Assert.Equal(["Screenshot", "Recording", "Recording"], Enum.GetValues<CaptureKind>().Select(k => k.NamePrefix()));
        Assert.Equal(["png", "mp4", "gif"], Enum.GetValues<CaptureKind>().Select(k => k.FileExtension()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((CaptureKind)99).NamePrefix());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((CaptureKind)99).FileExtension());
    }
}
