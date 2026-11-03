namespace Snapper.Core.Settings;
public enum RecordingFormat { Mp4, Gif }
public static class RecordingFormatExtensions
{
    public static string Title(this RecordingFormat value) => value switch
    {
        RecordingFormat.Mp4 => "MP4",
        RecordingFormat.Gif => "GIF",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static string FileExtension(this RecordingFormat value) => value switch { RecordingFormat.Mp4 => "mp4", RecordingFormat.Gif => "gif", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}
