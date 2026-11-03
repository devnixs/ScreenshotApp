using Snapper.Core.Settings;
namespace Snapper.Core.Recording;

public readonly record struct GifEncodingSettings(int FramesPerSecond, bool UsesDipSize, int? MaxLongestSide)
{
    public static GifEncodingSettings ForQuality(GifQuality quality) => quality switch
    {
        GifQuality.High => new(15, false, null), GifQuality.Medium => new(10, true, null),
        GifQuality.Low => new(8, true, 640), _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };
    public (int Width, int Height) OutputPixelSize(int sourceWidth, int sourceHeight, double scaleFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        double width = sourceWidth, height = sourceHeight;
        if (UsesDipSize && double.IsFinite(scaleFactor) && scaleFactor > 1)
        { width = Round(width / scaleFactor); height = Round(height / scaleFactor); }
        if (MaxLongestSide is { } cap && Math.Max(width, height) > cap)
        { var ratio = cap / Math.Max(width, height); width = Round(width * ratio); height = Round(height * ratio); }
        return ((int)Math.Clamp(width, 1, sourceWidth), (int)Math.Clamp(height, 1, sourceHeight));
    }
    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
public sealed record GifConversionPlan(GifEncodingSettings Settings, int Width, int Height)
{
    public static GifConversionPlan Make(GifQuality quality, int width, int height, double scale)
    {
        var settings = GifEncodingSettings.ForQuality(quality);
        var size = settings.OutputPixelSize(width, height, scale);
        return new(settings, size.Width, size.Height);
    }
}
