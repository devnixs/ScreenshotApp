using Snapper.Core.App;

namespace Snapper.Core.Tests;

public class TrayIconStyleTests
{
    [Theory]
    [InlineData(false, 0xFFFFFFFFu)]
    [InlineData(true, 0xFF000000u)]
    public void IdleMatchesTaskbarTheme(bool light, uint color) =>
        Assert.Equal(new(TrayGlyph.Viewfinder, color, "Snapper"), TrayIconStyle.Resolve(TrayIconState.Idle, light));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingAndConvertingHaveSameAppearance(bool light)
    {
        var appearance = TrayIconStyle.Resolve(TrayIconState.Recording, light);
        Assert.Equal(new(TrayGlyph.RecordDot, 0xFFE81123u, "Snapper — Recording"), appearance);
        Assert.Contains('—', appearance.Tooltip);
        Assert.Equal(appearance, TrayIconStyle.Resolve(TrayIconState.Converting, light));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void MissingThemeDefaultsToDark(int? value, bool expected) => Assert.Equal(expected, TrayIconStyle.TaskbarIsLight(value));

    [Theory]
    [InlineData(96, 16)]
    [InlineData(120, 20)]
    [InlineData(144, 24)]
    [InlineData(168, 28)]
    [InlineData(192, 32)]
    [InlineData(288, 48)]
    [InlineData(0, 16)]
    [InlineData(-96, 16)]
    public void IconHasNativePixelSize(int dpi, int expected) => Assert.Equal(expected, TrayIconStyle.IconPixelSize(dpi));
}
