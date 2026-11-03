using Snapper.Core.Annotate.Model;

namespace Snapper.Core.Tests;

public sealed class ContrastingOutlineTests
{
    [Fact] public void LuminanceAndPresets()
    {
        foreach (var (color, luminance) in new[] { (RgbaColor.Black, 0.0), (RgbaColor.White, 1.0),
            (new RgbaColor(1, 0, 0), 0.2126), (new RgbaColor(0, 1, 0), 0.7152), (new RgbaColor(0, 0, 1), 0.0722) })
            Assert.InRange(Math.Abs(ContrastingOutline.RelativeLuminance(color) - luminance), 0, 1e-9);
        foreach (var preset in Enum.GetValues<ColorPreset>())
            Assert.Equal(preset is ColorPreset.Yellow or ColorPreset.White ? RgbaColor.Black : RgbaColor.White, ContrastingOutline.ColorFor(preset.Color()));
    }

    [Fact] public void ThresholdAlphaClampingAndThickness()
    {
        static RgbaColor Gray(double luminance)
        { var channel = 1.055 * Math.Pow(luminance, 1 / 2.4) - 0.055; return new(channel, channel, channel); }
        Assert.Equal(RgbaColor.White, ContrastingOutline.ColorFor(Gray(0.49999)));
        Assert.Equal(RgbaColor.Black, ContrastingOutline.ColorFor(Gray(0.50001)));
        Assert.Equal(new(1, 1, 1, 0.4), ContrastingOutline.ColorFor(new(1.2, -1, 0, 0.4)));
        Assert.Equal(2 * ContrastingOutline.OutlineFractionOfFontSize * 100, ContrastingOutline.StrokeWidthPercent);
    }

    [Theory] [InlineData(48, 6)] [InlineData(24, 3)]
    public void PenThicknessIsTwiceTheVisibleOutline(double size, double thickness)
    {
        Assert.Equal(thickness, ContrastingOutline.PenThickness(size));
        Assert.Equal(size * ContrastingOutline.OutlineFractionOfFontSize, ContrastingOutline.PenThickness(size) / 2);
    }
}
