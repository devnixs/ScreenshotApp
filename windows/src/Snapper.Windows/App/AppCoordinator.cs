using System.Diagnostics;
using System.ComponentModel;
using Snapper.Core.App;
using Snapper.Core.Support;
using Snapper.Core.Capture;
using System.Windows;
using System.Windows.Controls;
using Snapper.Core.Settings;
using Snapper.Windows.Support;
using Snapper.Windows.Capture;
using System.Windows.Threading;

namespace Snapper.Windows.App;

internal sealed class AppCoordinator : IDisposable
{
    private TrayIconController? tray;
    private Window? settingsWindow;
    private readonly AppModeMachine modeMachine = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private HotkeyManager? hotkeys;
    private bool disposed;
    private readonly SelectionOverlayController overlay;
    public AppMode Mode => modeMachine.Mode;
    internal HotkeyManager? Hotkeys => hotkeys;

    public AppSettings Settings { get; } = new(
        new JsonSettingsStore(JsonSettingsStore.DefaultFilePath(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))), SaveFolder.DefaultPath);

    internal DisplayChangeWatcher Displays { get; }

    public AppCoordinator()
    {
        Settings.SaveFailed += SettingsSaveFailed;
        Displays = new DisplayChangeWatcher(Dispatcher.CurrentDispatcher);
        overlay = new SelectionOverlayController(Displays);
    }
    private static void SettingsSaveFailed(object? sender, SettingsSaveFailedEventArgs args) => Debug.WriteLine($"Snapper: {args.Exception}");
    public void Start()
    {
        if (tray is not null) return;
        tray = new TrayIconController(this);
        hotkeys = new HotkeyManager();
        hotkeys.Pressed += HotkeyPressed;
        hotkeys.RegistrationFailed += HotkeyFailed;
        hotkeys.StatusesChanged += HotkeyStatusesChanged;
        Settings.PropertyChanged += SettingsChanged;
        SyncSettings();
    }
    public void StartScreenshot() => Send(new AppModeEvent.MenuItem(CapturePurpose.Screenshot));
    public void StartRecording() => Send(new AppModeEvent.MenuItem(CapturePurpose.Record));
    public void StartAnnotate() => Send(new AppModeEvent.MenuItem(CapturePurpose.Annotate));

    public void Send(AppModeEvent e)
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        var old = Mode;
        var action = modeMachine.Handle(e);
        Debug.WriteLine($"[AppMode] {e}: {old} → {Mode}, action: {action}");
        Perform(action);
    }
    private void Perform(AppModeAction action)
    {
        switch (action)
        {
            case AppModeAction.ShowOverlay(var purpose): BeginSelection(purpose); break;
            case AppModeAction.CompleteSelection: break; // BeginSelection owns the returned selection.
            case AppModeAction.StopRecording: Debug.WriteLine("Snapper: Stop recording (not implemented until 0034)"); break;
            case AppModeAction.OpenMenu: tray?.ShowMenu(); break;
            case AppModeAction.None: break;
            default: throw new UnreachableException();
        }
    }
    private async void BeginSelection(CapturePurpose purpose)
    {
        tray?.CloseMenu();
        try
        {
            var result = await overlay.SelectRegionAsync(purpose);
            if (disposed) return;
            if (result is null) { Send(new AppModeEvent.SelectionCancelled()); return; }
            Send(new AppModeEvent.SelectionFinished());
            switch (result)
            {
                case RectSelection(var display, var rect):
                    Trace.WriteLine($"Selection for {purpose}: display {display.DeviceName}, rect ({rect.X}, {rect.Y}, {rect.Width}, {rect.Height}) px");
                    break;
                default: throw new UnreachableException();
            }
            // TODO(0034): start RecordingSession here.
            if (purpose == CapturePurpose.Record) Send(new AppModeEvent.RecordingEnded());
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Snapper: Couldn't show selection: {error}");
            Send(new AppModeEvent.SelectionCancelled());
        }
    }
    private void HotkeyPressed(CapturePurpose purpose) => Send(new AppModeEvent.Shortcut(purpose));
    private void HotkeyFailed(IReadOnlyList<KeyChord> chords) => tray?.ShowWarningBalloon(
        HotkeyFailureMessage.Title, HotkeyFailureMessage.Body(chords), () => OpenSettings(SettingsTab.Shortcuts));
    private void HotkeyStatusesChanged() => tray?.UpdateShortcutTexts(HotkeyBindings.From(Settings));
    private void SettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (dispatcher.CheckAccess()) SyncSettings();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(SyncSettings);
    }
    private void SyncSettings()
    {
        if (disposed) return;
        hotkeys?.Sync(Settings);
    }

    public void OpenSettings(SettingsTab tab = SettingsTab.General)
    {
        Debug.WriteLine($"Snapper: OpenSettings({tab})");
        if (settingsWindow is null)
        {
            settingsWindow = new Window
            {
                Title = "Snapper Settings", Width = 420, Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = true,
                Content = new TextBlock
                {
                    Text = "Settings will appear here.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        settingsWindow.Show();
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Settings.PropertyChanged -= SettingsChanged;
        Settings.SaveFailed -= SettingsSaveFailed;
        if (hotkeys is not null)
        {
            hotkeys.Pressed -= HotkeyPressed;
            hotkeys.RegistrationFailed -= HotkeyFailed;
            hotkeys.StatusesChanged -= HotkeyStatusesChanged;
            hotkeys.Dispose();
            hotkeys = null;
        }
        overlay.Cancel();
        Displays.Dispose();
        tray?.Dispose();
        tray = null;
    }
}
