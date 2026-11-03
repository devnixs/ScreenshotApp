namespace Snapper.Core.Settings;
public static class RecordingSettingsPresentation
{
    public static IReadOnlyList<(Mp4Quality Value, string Title)> Mp4QualityOptions { get; } =
        Array.AsReadOnly(Enum.GetValues<Mp4Quality>().Select(q => (q, q.Title())).ToArray());
    public static IReadOnlyList<(RecordingFormat Value, string Title)> FormatOptions { get; } =
        Array.AsReadOnly(Enum.GetValues<RecordingFormat>().Select(f => (f, f.Title())).ToArray());
    public static IReadOnlyList<string> QualityTitles { get; } = Array.AsReadOnly(new[] { "High", "Medium", "Low" });
    public static int QualityIndex(RecordingFormat format, Mp4Quality mp4, GifQuality gif) => format switch
    { RecordingFormat.Mp4 => (int)mp4, RecordingFormat.Gif => (int)gif, _ => throw new ArgumentOutOfRangeException(nameof(format)) };
    public static (Mp4Quality Mp4, GifQuality Gif) WithQualityIndex(RecordingFormat format, int index, Mp4Quality mp4, GifQuality gif)
    {
        if (index is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(index));
        return format switch
        { RecordingFormat.Mp4 => ((Mp4Quality)index, gif), RecordingFormat.Gif => (mp4, (GifQuality)index), _ => throw new ArgumentOutOfRangeException(nameof(format)) };
    }
    public static bool MicrophoneToggleEnabled(RecordingFormat format) => format == RecordingFormat.Mp4;
    public static string? MicrophoneNote(RecordingFormat format) => format == RecordingFormat.Gif ? "GIFs don't include sound." : null;
    public static string QualityCaption(RecordingFormat format) => format switch
    {
        RecordingFormat.Mp4 => Mp4QualityCaption,
        RecordingFormat.Gif => "High: 15 fps, full size. Medium: 10 fps, 1×. Low: 8 fps, up to 640 px.",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    public const string Mp4QualityCaption = "Medium and Low re-encode after you stop, for smaller files.";
}
