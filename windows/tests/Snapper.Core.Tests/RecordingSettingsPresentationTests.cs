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
}
