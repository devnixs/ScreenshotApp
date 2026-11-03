namespace Snapper.Core.Settings;
public enum Mp4Quality { High, Medium, Low }
public static class Mp4QualityExtensions
{
    public static string Title(this Mp4Quality value) => value switch
    {
        Mp4Quality.High => "High",
        Mp4Quality.Medium => "Medium",
        Mp4Quality.Low => "Low",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
