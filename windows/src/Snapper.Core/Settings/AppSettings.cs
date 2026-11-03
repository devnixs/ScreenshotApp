using System.ComponentModel;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Support;
namespace Snapper.Core.Settings;

/// <summary>Dispatcher-thread-only settings. Background work receives an immutable Snapshot, never this object.
/// LaunchAtLogin mirrors the OS registry; loading it never registers or unregisters startup.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private readonly ISettingsStore store;
    private readonly string defaultSaveFolder;
    private SettingsValues values;
    public AppSettings(ISettingsStore store, string defaultSaveFolder)
    {
        this.store = store; this.defaultSaveFolder = defaultSaveFolder; values = store.Load();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed;
    public SettingsValues Snapshot() => values;
    public bool HasCustomSaveFolder => values.SaveFolder is not null;
    public string SaveFolder
    {
        get => values.SaveFolder ?? defaultSaveFolder;
        set
        {
            if (!JsonSettingsStore.IsValidSaveFolderPath(value)) throw new ArgumentException("Save folder must be an absolute Windows path.", nameof(value));
            Update(values with { SaveFolder = value }, nameof(SaveFolder));
        }
    }
    public bool LaunchAtLogin { get => values.LaunchAtLogin; set => Update(values with { LaunchAtLogin = value }, nameof(LaunchAtLogin)); }
    public ScreenshotDestination ScreenshotDestination { get => values.ScreenshotDestination; set => Update(values with { ScreenshotDestination = value }, nameof(ScreenshotDestination)); }
    public bool PlayShutterSound { get => values.PlayShutterSound; set => Update(values with { PlayShutterSound = value }, nameof(PlayShutterSound)); }
    public bool RecordMicrophone { get => values.RecordMicrophone; set => Update(values with { RecordMicrophone = value }, nameof(RecordMicrophone)); }
    public RecordingFormat RecordingFormat { get => values.RecordingFormat; set => Update(values with { RecordingFormat = value }, nameof(RecordingFormat)); }
    public Mp4Quality Mp4Quality { get => values.Mp4Quality; set => Update(values with { Mp4Quality = value }, nameof(Mp4Quality)); }
    public GifQuality GifQuality { get => values.GifQuality; set => Update(values with { GifQuality = value }, nameof(GifQuality)); }
    public RgbaColor AnnotationColor { get => values.AnnotationColor; set => Update(values with { AnnotationColor = value }, nameof(AnnotationColor)); }
    public StrokeWidth AnnotationStrokeWidth { get => values.AnnotationStrokeWidth; set => Update(values with { AnnotationStrokeWidth = value }, nameof(AnnotationStrokeWidth)); }
    public KeyChord? ScreenshotShortcut { get => values.ScreenshotShortcut; set => Update(values with { ScreenshotShortcut = value }, nameof(ScreenshotShortcut)); }
    public KeyChord? RecordShortcut { get => values.RecordShortcut; set => Update(values with { RecordShortcut = value }, nameof(RecordShortcut)); }
    public KeyChord? AnnotateShortcut { get => values.AnnotateShortcut; set => Update(values with { AnnotateShortcut = value }, nameof(AnnotateShortcut)); }

    private void Update(SettingsValues next, string property)
    {
        if (next == values) return;
        values = next;
        try { store.Save(values); }
        catch (SettingsSaveException error) { SaveFailed?.Invoke(this, new SettingsSaveFailedEventArgs(error)); }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
public sealed class SettingsSaveFailedEventArgs(SettingsSaveException exception) : EventArgs
{
    public SettingsSaveException Exception { get; } = exception;
}
