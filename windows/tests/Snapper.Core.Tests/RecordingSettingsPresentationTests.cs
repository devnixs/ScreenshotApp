using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class RecordingSettingsPresentationTests
{
    [Fact] public void Mp4OptionsUseProductOrderAndTitles()
    {
        Assert.Equal(new[]{Mp4Quality.High,Mp4Quality.Medium,Mp4Quality.Low},RecordingSettingsPresentation.Mp4QualityOptions.Select(q=>q.Value));
        Assert.Equal(new[]{"High","Medium","Low"},RecordingSettingsPresentation.Mp4QualityOptions.Select(q=>q.Title));
        Assert.Equal("Medium and Low re-encode after you stop, for smaller files.",RecordingSettingsPresentation.Mp4QualityCaption);
    }
    [Fact] public void FormatsAndQualityUseProductOrder()
    {
        Assert.Equal(new[]{RecordingFormat.Mp4,RecordingFormat.Gif},RecordingSettingsPresentation.FormatOptions.Select(f=>f.Value));
        Assert.Equal(new[]{"MP4","GIF"},RecordingSettingsPresentation.FormatOptions.Select(f=>f.Title));
        Assert.Equal(new[]{"High","Medium","Low"},RecordingSettingsPresentation.QualityTitles);
    }
    [Theory,InlineData(RecordingFormat.Mp4,2),InlineData(RecordingFormat.Gif,0)]
    public void QualityReadsOnlySelectedFormat(RecordingFormat format,int expected) =>
        Assert.Equal(expected,RecordingSettingsPresentation.QualityIndex(format,Mp4Quality.Low,GifQuality.High));
    [Fact] public void UpdatingQualityPreservesOtherFormat()
    {
        Assert.Equal((Mp4Quality.High,GifQuality.Low),RecordingSettingsPresentation.WithQualityIndex(RecordingFormat.Gif,2,Mp4Quality.High,GifQuality.Medium));
        Assert.Equal((Mp4Quality.Medium,GifQuality.Medium),RecordingSettingsPresentation.WithQualityIndex(RecordingFormat.Mp4,1,Mp4Quality.High,GifQuality.Medium));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RecordingSettingsPresentation.WithQualityIndex(RecordingFormat.Gif,3,Mp4Quality.High,GifQuality.Medium));
    }
    [Fact] public void MicrophoneAndCaptionsFollowFormat()
    {
        Assert.True(RecordingSettingsPresentation.MicrophoneToggleEnabled(RecordingFormat.Mp4));
        Assert.False(RecordingSettingsPresentation.MicrophoneToggleEnabled(RecordingFormat.Gif));
        Assert.Null(RecordingSettingsPresentation.MicrophoneNote(RecordingFormat.Mp4));
        Assert.Equal("GIFs don't include sound.",RecordingSettingsPresentation.MicrophoneNote(RecordingFormat.Gif));
        Assert.Equal(RecordingSettingsPresentation.Mp4QualityCaption,RecordingSettingsPresentation.QualityCaption(RecordingFormat.Mp4));
        Assert.Equal("High: 15 fps, full size. Medium: 10 fps, 1×. Low: 8 fps, up to 640 px.",RecordingSettingsPresentation.QualityCaption(RecordingFormat.Gif));
    }
}
