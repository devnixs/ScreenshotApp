using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public sealed record RecordingConfiguration(string DisplayId, PixelRect SourceRect, int PixelWidth, int PixelHeight,
    int FramesPerSecond, bool ShowsCursor, int VideoBitrate, double Scale)
{
    public const string FileExtension = ".mp4", VideoCodec = "H.264";
    public double FrameIntervalTicks => 10_000_000.0 / FramesPerSecond;
    public bool FollowsWindow { get; init; }
    public bool ScalesToFit { get; init; }
    public bool PreservesAspectRatio { get; init; }
    public PixelRect? SourceBoxForFrame(int frameWidth, int frameHeight) => SourceRect.Left >= 0 && SourceRect.Top >= 0 &&
        SourceRect.Right <= frameWidth && SourceRect.Bottom <= frameHeight ? SourceRect : null;
}
public static class RecordingConfigurationBuilder
{
    public const double HighBitsPerPixel = 0.20;
    public static RecordingConfiguration Build(DisplayInfo display, RectD selection, bool showsCursor = true, int framesPerSecond = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
        if (display.Bounds.Width < 2 || display.Bounds.Height < 2) throw new ArgumentException("The display is too small.", nameof(display));
        var r = DisplayGeometry.Clamp(selection, display.Bounds);
        var width = EvenFloor(r.Width); var height = EvenFloor(r.Height);
        var x = Math.Clamp((int)Math.Round(r.X - display.Bounds.X, MidpointRounding.AwayFromZero), 0, display.Bounds.Width - width);
        var y = Math.Clamp((int)Math.Round(r.Y - display.Bounds.Y, MidpointRounding.AwayFromZero), 0, display.Bounds.Height - height);
        var bitrate = Math.Max(1_000_000, checked((int)Math.Round((double)width * height * 30 * HighBitsPerPixel)));
        return new(display.DeviceName, new(x, y, width, height), width, height, framesPerSecond, showsCursor, bitrate, display.Scale);
    }
    public static RecordingConfiguration BuildWindow(PixelRect startingExtendedFrame, double monitorScale)
    {
        var width = EvenFloor(startingExtendedFrame.Width);
        var height = EvenFloor(startingExtendedFrame.Height);
        var bitrate = Math.Max(1_000_000, checked((int)Math.Round((double)width * height * 30 * HighBitsPerPixel)));
        return new("", new(0, 0, startingExtendedFrame.Width, startingExtendedFrame.Height), width, height, 30, true, bitrate, monitorScale)
        { FollowsWindow = true, ScalesToFit = true, PreservesAspectRatio = true };
    }
    public static int EvenFloor(double value) => Math.Max(2, (int)Math.Floor(value) / 2 * 2);
}
