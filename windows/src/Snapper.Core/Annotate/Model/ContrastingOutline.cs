namespace Snapper.Core.Annotate.Model;

public static class ContrastingOutline
{
    public const double OutlineFractionOfFontSize = 0.0625;
    public const double StrokeWidthPercent = 12.5;
    public static double RelativeLuminance(RgbaColor color)
    {
        static double Linear(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
    public static RgbaColor ColorFor(RgbaColor color)
    {
        var channel = RelativeLuminance(color) >= 0.5 ? 0 : 1;
        return new(channel, channel, channel, color.Alpha);
    }
    public static double PenThickness(double fontPixelSize) => fontPixelSize * StrokeWidthPercent / 100;
}
