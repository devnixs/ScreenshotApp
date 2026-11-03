using System.ComponentModel;
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
    public void RefreshOnActivate() => ReadLaunchAtLogin();
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
        settings.PropertyChanged -= SettingsChanged;
        hotkeys.StatusesChanged -= StatusesChanged;
    }
}
