using Snapper.Core.Annotate.Model;
using Snapper.Core.Support;
namespace Snapper.Core.Settings;

public sealed record SettingsValues
{
    public bool LaunchAtLogin { get; init; } = false;
    public string? SaveFolder { get; init; }
    public ScreenshotDestination ScreenshotDestination { get; init; } = ScreenshotDestination.Both;
    public bool PlayShutterSound { get; init; } = true;
    public bool RecordMicrophone { get; init; } = false;
    public RecordingFormat RecordingFormat { get; init; } = RecordingFormat.Mp4;
    public Mp4Quality Mp4Quality { get; init; } = Mp4Quality.High;
    public GifQuality GifQuality { get; init; } = GifQuality.Medium;
    public RgbaColor AnnotationColor { get; init; } = RgbaColor.Red;
    public StrokeWidth AnnotationStrokeWidth { get; init; } = StrokeWidth.M;
    public KeyChord? ScreenshotShortcut { get; init; } = KeyChord.Create(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x34);
    public KeyChord? RecordShortcut { get; init; } = KeyChord.Create(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x35);
    public KeyChord? AnnotateShortcut { get; init; } = KeyChord.Create(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x36);
    public static SettingsValues Defaults { get; } = new();
}
