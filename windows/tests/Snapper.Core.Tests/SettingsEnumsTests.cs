using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class SettingsEnumsTests
{
    [Fact] public void ScreenshotDestinationFlagsAndTitles()
    {
        var values = Enum.GetValues<ScreenshotDestination>();
        Assert.Equal(new[] { ScreenshotDestination.File, ScreenshotDestination.Clipboard, ScreenshotDestination.Both }, values);
        Assert.Equal(new[] { true,false,true }, values.Select(v => v.SavesToFile()));
        Assert.Equal(new[] { false,true,true }, values.Select(v => v.CopiesToClipboard()));
        Assert.Equal(new[] { "Save to file","Copy to clipboard","Both" }, values.Select(v => v.Title()));
    }
    [Fact] public void RecordingFormatAndQualityTitles()
    {
        Assert.Equal(new[] { RecordingFormat.Mp4, RecordingFormat.Gif }, Enum.GetValues<RecordingFormat>());
        Assert.Equal(new[] { "MP4","GIF" }, Enum.GetValues<RecordingFormat>().Select(v => v.Title()));
        Assert.Equal(new[] { "mp4","gif" }, Enum.GetValues<RecordingFormat>().Select(v => v.FileExtension()));
        Assert.Equal(new[] { Mp4Quality.High,Mp4Quality.Medium,Mp4Quality.Low }, Enum.GetValues<Mp4Quality>());
        Assert.Equal(new[] { GifQuality.High,GifQuality.Medium,GifQuality.Low }, Enum.GetValues<GifQuality>());
        Assert.Equal(new[] { "High","Medium","Low" }, Enum.GetValues<Mp4Quality>().Select(v => v.Title()));
        Assert.Equal(new[] { "High","Medium","Low" }, Enum.GetValues<GifQuality>().Select(v => v.Title()));
    }
}
