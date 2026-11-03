namespace Snapper.Core.Settings;
public enum GifQuality { High, Medium, Low }
public static class GifQualityExtensions
{
    public static string Title(this GifQuality value) => value switch
    {
        GifQuality.High => "High",
        GifQuality.Medium => "Medium",
        GifQuality.Low => "Low",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
