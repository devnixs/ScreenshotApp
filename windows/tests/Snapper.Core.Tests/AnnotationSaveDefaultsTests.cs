using Snapper.Core.Annotate;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;

public sealed class AnnotationSaveDefaultsTests
{
    private readonly FileNamer namer = new(TimeZoneInfo.Utc);
    private readonly DateTime capture = new(2026, 11, 2, 19, 18, 4, DateTimeKind.Utc);
    [Theory] [InlineData(0, "")] [InlineData(1, " (2)")] [InlineData(2, " (3)")]
    public void CaptureDateAndCollisions(int taken, string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapper", "Screenshots");
        var probes = new List<string>();
        var name = AnnotationSaveDefaults.SuggestedFileName(capture, directory, path =>
        { Assert.Equal(directory, Path.GetDirectoryName(path)); probes.Add(path); return probes.Count <= taken; }, namer);
        Assert.Equal($"Screenshot 2026-11-02 at 19.18.04{suffix}.png", name);
        Assert.Equal(taken + 1, probes.Count);
    }
    [Fact] public void NullDirectoryNeverProbes() => Assert.Equal("Screenshot 2026-11-02 at 19.18.04.png",
        AnnotationSaveDefaults.SuggestedFileName(capture, null, _ => throw new InvalidOperationException("Must not probe"), namer));
    [Fact] public void UsesCaptureTimeNotNow() => Assert.Equal("Screenshot 2020-01-02 at 03.04.05.png",
        AnnotationSaveDefaults.SuggestedFileName(new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), null, _ => false, namer));
}
