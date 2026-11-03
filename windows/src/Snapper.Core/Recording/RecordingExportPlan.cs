using Snapper.Core.Settings;
namespace Snapper.Core.Recording;

public abstract record RecordingTranscode
{
    public sealed record Mp4(int AverageBitRate) : RecordingTranscode;
}
public sealed record RecordingExportSettings(RecordingFormat Format, Mp4Quality Mp4Quality, GifQuality GifQuality)
{
    public RecordingExportPlan Plan(int width, int height, double scale) => RecordingExportPlan.Make(Format, Mp4Quality, GifQuality, width, height, scale);
}
public sealed record RecordingExportPlan(RecordingFormat Format, RecordingTranscode? Transcode)
{
    public bool Converts => Transcode is not null;
    public static RecordingExportPlan Make(RecordingFormat format, Mp4Quality mp4Quality, GifQuality gifQuality,
        int pixelWidth, int pixelHeight, double scaleFactor)
    {
        // 0046 adds GIF. Until then a persisted GIF preference uses the selected MP4 quality.
        var bitrate = Mp4Bitrate.AverageBitRate(pixelWidth, pixelHeight, mp4Quality);
        return new(RecordingFormat.Mp4, bitrate is { } value ? new RecordingTranscode.Mp4(value) : null);
    }
}
public static class Mp4Bitrate
{
    public const int FramesPerSecond = 30;
    public static double? BitsPerPixelPerFrame(Mp4Quality quality) => quality switch
    {
        Mp4Quality.High => null, Mp4Quality.Medium => 0.10, Mp4Quality.Low => 0.05,
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };
    public static int? AverageBitRate(int pixelWidth, int pixelHeight, Mp4Quality quality)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        return BitsPerPixelPerFrame(quality) is { } factor
            ? checked((int)Math.Round((double)pixelWidth * pixelHeight * FramesPerSecond * factor, MidpointRounding.AwayFromZero)) : null;
    }
}
