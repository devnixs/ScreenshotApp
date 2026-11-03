using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Snapper.Core.App;
using Snapper.Core.Capture;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Snapper.Windows.App;
using Snapper.Windows.Support;
namespace Snapper.Windows.Settings;

internal sealed class ShortcutRowViewModel(CapturePurpose purpose, AppSettings settings, HotkeyManager hotkeys) : INotifyPropertyChanged
{
    public CapturePurpose Purpose { get; } = purpose;
    public string Label => Purpose + ":";
    public KeyChord? Binding
    {
        get => HotkeyBindings.From(settings)[Purpose];
        set
        {
            switch (Purpose)
            {
                case CapturePurpose.Screenshot: settings.ScreenshotShortcut = value; break;
                case CapturePurpose.Record: settings.RecordShortcut = value; break;
                case CapturePurpose.Annotate: settings.AnnotateShortcut = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(Purpose));
            }
        }
    }
    public string? HintText { get; set; }
    public bool ShowsConflictWarning => hotkeys.StatusOf(Purpose) == HotkeyStatus.InUseByAnotherApp;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
internal sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettings settings;
    private readonly HotkeyManager hotkeys;
    private readonly Action suspend;
    private readonly Action resume;
    private LaunchAtLoginState launchAtLogin = new(false, null);
    private readonly MicrophoneRequestState microphoneRequest = new();
    private bool showsMicrophoneDenied, disposed;
    public Window? Window { get; set; }
    public IReadOnlyList<ShortcutRowViewModel> ShortcutRows { get; }
    public SettingsViewModel(AppSettings settings, HotkeyManager hotkeys, Action suspend, Action resume)
    {
        this.settings = settings; this.hotkeys = hotkeys; this.suspend = suspend; this.resume = resume;
        ShortcutRows = CapturePurposes.All.Select(p => new ShortcutRowViewModel(p, settings, hotkeys)).ToArray();
        settings.PropertyChanged += SettingsChanged;
        hotkeys.StatusesChanged += StatusesChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public bool LaunchAtLoginEnabled { get => launchAtLogin.IsOn; set => SetLaunchAtLogin(value); }
    public string? LaunchAtLoginNoteText => launchAtLogin.NoteText;
    public bool ShowsOpenStartupApps => launchAtLogin.Note == LaunchAtLoginNote.DisabledInStartupApps;
    public string SaveFolderPath => settings.SaveFolder;
    public bool PlayShutterSound { get => settings.PlayShutterSound; set => settings.PlayShutterSound = value; }
    public ScreenshotDestination ScreenshotDestination { get => settings.ScreenshotDestination; set => settings.ScreenshotDestination = value; }
    public bool DestinationFile { get => ScreenshotDestination == ScreenshotDestination.File; set { if (value) ScreenshotDestination = ScreenshotDestination.File; } }
    public bool DestinationClipboard { get => ScreenshotDestination == ScreenshotDestination.Clipboard; set { if (value) ScreenshotDestination = ScreenshotDestination.Clipboard; } }
    public bool DestinationBoth { get => ScreenshotDestination == ScreenshotDestination.Both; set { if (value) ScreenshotDestination = ScreenshotDestination.Both; } }
    public IReadOnlyDictionary<CapturePurpose, KeyChord?> CurrentBindings() => HotkeyBindings.From(settings);
    public void SuspendHotkeys() => suspend();
    public void ResumeHotkeys() => resume();
    public void SetLaunchAtLogin(bool on) => ReadLaunchAtLogin(LaunchAtLogin.SetEnabled(on));
    private void ReadLaunchAtLogin(string? error = null)
    {
        launchAtLogin = LaunchAtLogin.Read(error);
        settings.LaunchAtLogin = launchAtLogin.IsOn;
        Refresh();
    }
    public void RefreshOnActivate() { ReadLaunchAtLogin(); ReconcileMicrophone(); }
    public IReadOnlyList<(Mp4Quality Value, string Title)> Mp4QualityOptions => RecordingSettingsPresentation.Mp4QualityOptions;
    public string Mp4HighTitle => Mp4QualityOptions[0].Title;
    public string Mp4MediumTitle => Mp4QualityOptions[1].Title;
    public string Mp4LowTitle => Mp4QualityOptions[2].Title;
    public string Mp4QualityCaption => RecordingSettingsPresentation.Mp4QualityCaption;
    public bool Mp4High { get => settings.Mp4Quality == Mp4Quality.High; set { if (value) settings.Mp4Quality = Mp4Quality.High; } }
    public bool Mp4Medium { get => settings.Mp4Quality == Mp4Quality.Medium; set { if (value) settings.Mp4Quality = Mp4Quality.Medium; } }
    public bool Mp4Low { get => settings.Mp4Quality == Mp4Quality.Low; set { if (value) settings.Mp4Quality = Mp4Quality.Low; } }
    public bool RecordMicrophone
    {
        get => microphoneRequest.IsPending && microphoneRequest.WantsMicrophone || settings.RecordMicrophone;
        set => ToggleMicrophone(value);
    }
    public bool ShowsMicrophoneDenied => showsMicrophoneDenied;
    private void ApplyMicrophone(MicrophoneToggleOutcome outcome)
    {
        showsMicrophoneDenied = outcome.ShowsDeniedNote;
        settings.RecordMicrophone = outcome.StoredValue;
        Refresh();
    }
    private async void ToggleMicrophone(bool requested)
    {
        microphoneRequest.UserToggled(requested);
        var outcome = MicrophoneToggleDecision.UserToggled(requested, MicrophonePermission.Authorization);
        // Mark pending before publishing the stored-false decision, keeping the checkbox on for the user's intent.
        var probe = outcome.ShouldRequestAccess && microphoneRequest.BeginRequest();
        ApplyMicrophone(outcome);
        if (!probe) return;
        var result = await MicrophonePermission.ProbeAsync();
        var apply = microphoneRequest.FinishRequest();
        if (!disposed && apply) ApplyMicrophone(MicrophoneToggleDecision.AccessRequestFinished(MicrophoneAccessMapping.ProbeGrantsAccess(result)));
        else Refresh();
    }
    public void ReconcileMicrophone()
    {
        if (microphoneRequest.IsPending || disposed) return;
        var status = MicrophonePermission.Authorization;
        var outcome = MicrophoneToggleDecision.ReconcileOnAppear(settings.RecordMicrophone, status);
        // Opening/activation can reconcile twice. Retain a known denial until access changes or the user toggles off.
        if (showsMicrophoneDenied && status is MicrophoneAuthorization.Denied or MicrophoneAuthorization.Restricted)
            outcome = MicrophoneToggleDecision.AccessRequestFinished(false);
        ApplyMicrophone(outcome);
    }
    public void SettingsHidden() { microphoneRequest.UserToggled(false); Refresh(); }
    public static void OpenMicrophonePrivacy()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); }
        catch (Exception error) { ErrorAlerts.ShowError("Couldn't open Privacy Settings", error.Message); }
    }
    public void ChooseSaveFolder()
    {
        var picker = new OpenFolderDialog
        {
            Title = "Choose Save Folder", Multiselect = false,
            InitialDirectory = ScreenshotOutputPlan.NearestExistingFolder(SaveFolderPath, Directory.Exists)
        };
        if (picker.ShowDialog(Window) == true) settings.SaveFolder = picker.FolderName;
    }
    public void OpenSaveFolder()
    {
        try
        {
            var path = SaveFolder.CreatePolicy().Prepare(SaveFolderPath);
            if (!ExplorerReveal.OpenFolder(path)) ErrorAlerts.ShowError("Couldn't open folder", SaveFolderPath);
        }
        catch (Exception e) { _ = ErrorAlerts.ShowSaveFolderFailure(SaveFolderPath, e, settings); }
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Refresh();
        foreach (var row in ShortcutRows) row.Refresh();
    }
    private void StatusesChanged() { foreach (var row in ShortcutRows) row.Refresh(); }
    public void Dispose()
    {
        disposed = true;
        microphoneRequest.UserToggled(false);
        settings.PropertyChanged -= SettingsChanged;
        hotkeys.StatusesChanged -= StatusesChanged;
    }
}
