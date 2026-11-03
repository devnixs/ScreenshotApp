namespace Snapper.Core.Support;

public enum CaptureKind { Screenshot, RecordingMp4, RecordingGif }

public static class CaptureKindExtensions
{
    public static string NamePrefix(this CaptureKind kind) => kind switch
    {
        CaptureKind.Screenshot => "Screenshot",
        CaptureKind.RecordingMp4 or CaptureKind.RecordingGif => "Recording",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string FileExtension(this CaptureKind kind) => kind switch
    {
        CaptureKind.Screenshot => "png",
        CaptureKind.RecordingMp4 => "mp4",
        CaptureKind.RecordingGif => "gif",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
