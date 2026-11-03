using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Recording;
using System.ComponentModel;
using Snapper.Core.App;
using Snapper.Core.Support;
using Snapper.Core.Capture;
using Snapper.Windows.Interop;
using System.Windows;
using Snapper.Windows.Settings;
using Snapper.Core.Settings;
using Snapper.Windows.Support;
using Snapper.Windows.Capture;
using System.Windows.Threading;

namespace Snapper.Windows.App;

internal sealed class AppCoordinator : IDisposable
{
    private TrayIconController? tray;
    private SettingsWindowController? settingsWindow;
    private readonly AppModeMachine modeMachine = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private HotkeyManager? hotkeys;
    private bool disposed;
    private RecordingSession? recording;
    private RecordingOverlayController? recordingOverlay;
    private WindowFrameTracker? windowTracker;
    private readonly RecordingTerminationDecision termination = new();
    private ShutdownBlocker? shutdownBlocker;
    private readonly SelectionOverlayController overlay;
    private readonly Direct3D11Helper d3d = new();
    private readonly ShutterSound shutterSound = new();
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
        shutdownBlocker = new ShutdownBlocker(OnQueryEndSession, OnEndSession);
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
            case AppModeAction.StopRecording: StopRecording(); break;
            case AppModeAction.OpenMenu: tray?.ShowMenu(); break;
            case AppModeAction.None: break;
            default: throw new UnreachableException();
        }
    }
    private async void BeginSelection(CapturePurpose purpose)
    {
        tray?.CloseMenu();
        var annotateForeground = new ForegroundRestorer();
        if (purpose == CapturePurpose.Annotate) annotateForeground.Remember();
        try
        {
            var result = await overlay.SelectRegionAsync(purpose);
            if (disposed) return;
            if (result is null) { Send(new AppModeEvent.SelectionCancelled()); return; }
            Send(new AppModeEvent.SelectionFinished());
            if (purpose == CapturePurpose.Record) BeginRecording(result);
            else if (purpose == CapturePurpose.Screenshot) await TakeScreenshot(result);
            else
            {
                switch (result)
                {
                    case RectSelection(var display, var rect):
                        Trace.WriteLine($"Selection for {purpose}: display {display.DeviceName}, rect {rect} px"); break;
                    case WindowSelectionResult(var handle, var frame, var display):
                        Trace.WriteLine($"Selection for {purpose}: window {handle}, display {display.DeviceName}, frame {frame} px"); break;
                    default: throw new UnreachableException();
                }
                if (purpose == CapturePurpose.Annotate)
                {
                    Debug.WriteLine("Snapper: Annotate isn't available yet");
                    annotateForeground.Restore();
                }
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Snapper: Couldn't show selection: {error}");
            Send(new AppModeEvent.SelectionCancelled());
        }
    }
    private void BeginRecording(SelectionResult selection)
    {
        var startDate = DateTimeOffset.Now;
        DisplayInfo display;
        RecordingConfiguration configuration;
        WindowRecordingSource? window = null;
        try
        {
            switch (selection)
            {
                case RectSelection(var selectedDisplay, var rect):
                    display = selectedDisplay;
                    configuration = RecordingConfigurationBuilder.Build(display, RectD.FromPixelRect(rect));
                    break;
                case WindowSelectionResult(var handle, _, _):
                    var hwnd = new HWND(handle);
                    var thread = PInvoke.GetWindowThreadProcessId(hwnd, out _);
                    if (!PInvoke.IsWindow(hwnd) || thread == 0 || !WindowFrameTracker.TryGetFrames(hwnd, out var rawFrame, out var frame))
                        throw new WindowUnavailableException();
                    var region = RectD.FromPixelRect(frame);
                    display = WindowDisplayRule.Display(region, Displays.Current)
                        ?? throw new InvalidOperationException("The window is no longer on a display.");
                    configuration = RecordingConfigurationBuilder.BuildWindow(frame, display.Scale);
                    window = new(handle, thread, rawFrame, frame);
                    break;
                default: throw new UnreachableException();
            }
            string path;
            try { path = RecordingFilePath.Reserve(Settings.SaveFolder, startDate); }
            catch (Exception error) when (ScreenshotOutputPlan.ClassifySaveFailure(error) == SaveFailureAlert.SaveFolder)
            {
                Send(new AppModeEvent.RecordingEnded());
                ErrorAlerts.ShowSaveFolderFailure(Settings.SaveFolder, error, Settings);
                return;
            }
            // 0045/0046: format and quality are deliberately MP4 High until export conversion lands.
            recording = new RecordingSession(configuration, display, path, d3d, Displays, Settings,
                onStarted: () =>
                {
                    var recordedRect = window?.ExtendedFrame ?? new PixelRect(display.Bounds.X + configuration.SourceRect.X,
                        display.Bounds.Y + configuration.SourceRect.Y, configuration.PixelWidth, configuration.PixelHeight);
                    // Pill and border set WDA_EXCLUDEFROMCAPTURE before first show; Windows has no app-level filter.
                    recordingOverlay = new RecordingOverlayController(display, recordedRect, followsWindow: window is not null,
                        Stopwatch.GetTimestamp(), StopRecording, CancelRecording, Displays);
                    recordingOverlay.Show();
                    if (window is not null)
                    {
                        windowTracker = new WindowFrameTracker(new HWND(window.Handle), window.ThreadId, window.ExtendedFrame, WindowTrackingChanged);
                        windowTracker.Start();
                    }
                }, onStopping: DismissRecordingOverlay, onEnded: RecordingEnded, window: window);
            tray?.SetRecording(true);
            shutdownBlocker?.SetReason(RecordingTerminationDecision.ShutdownBlockReason);
            recording.Start();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Snapper: Starting recording: {error}");
            DismissRecordingOverlay();
            recording = null;
            Send(new AppModeEvent.RecordingEnded());
            tray?.SetRecording(false); shutdownBlocker?.SetReason(null);
            ErrorAlerts.ShowError("Couldn't record the screen", error.Message);
        }
    }
    private void WindowTrackingChanged(WindowTrackingEvent e)
    {
        switch (e)
        {
            case WindowTrackingEvent.Moved(var frame): UpdateWindowOverlay(frame); break;
            case WindowTrackingEvent.Shown(var frame): UpdateWindowOverlay(frame); break;
            case WindowTrackingEvent.Hidden: recordingOverlay?.SetBorderVisible(false); break;
            case WindowTrackingEvent.Closed: StopRecording(); break;
        }
    }
    private void UpdateWindowOverlay(PixelRect frame)
    {
        var region = RectD.FromPixelRect(frame);
        // Display assignment has a nearest-display fallback; visibility requires positive overlap.
        if (DisplayGeometry.DisplayOverlapping(region, Displays.Current) is null)
        { recordingOverlay?.SetBorderVisible(false); return; }
        var display = WindowDisplayRule.Display(region, Displays.Current);
        if (display is null) { recordingOverlay?.SetBorderVisible(false); return; }
        recordingOverlay?.Update(frame, display);
        recordingOverlay?.SetBorderVisible(true);
    }
    private void DismissRecordingOverlay()
    {
        windowTracker?.Dispose(); windowTracker = null;
        recordingOverlay?.Dismiss();
        recordingOverlay = null;
    }
    public void StopRecording() { DismissRecordingOverlay(); recording?.Stop(); }
    public void CancelRecording() { DismissRecordingOverlay(); recording?.Cancel(); }
    private void RecordingEnded()
    {
        DismissRecordingOverlay();
        recording = null;
        Send(new AppModeEvent.RecordingEnded());
        tray?.SetRecording(false); shutdownBlocker?.SetReason(null);
        if (termination.RecordingEnded()) FinishExit();
    }
    private bool OnQueryEndSession()
    {
        if (recording is null) return true;
        termination.Request(true); StopRecording(); return false;
    }
    private void OnEndSession()
    {
        if (recording is null) return;
        StopRecording();
        recording.WaitForWriter(TimeSpan.FromSeconds(5));
    }

    private async Task TakeScreenshot(SelectionResult selection)
    {
        var date = DateTime.Now;
        try
        {
            var capturer = new ScreenshotCapturer(d3d);
            var image = selection switch
            {
                RectSelection(var display, var rect) => await capturer.CaptureMonitorRegionAsync(display, rect),
                WindowSelectionResult(var handle, _, var display) => await capturer.CaptureWindowAsync(handle, scaleFactor: display.Scale),
                _ => throw new UnreachableException()
            };
            if (disposed) return;
            if (Settings.PlayShutterSound) shutterSound.Play();
            var (png, dib) = await Task.Run(() => (PngEncoder.Encode(image), DibEncoder.FromBgra(image.Bgra, image.Width, image.Height)));
            if (disposed) return;
            var output = ScreenshotOutputRunner.Run(new ScreenshotOutputPlan(Settings.ScreenshotDestination), Settings.SaveFolder,
                new ScreenshotEnvironment(Settings, png, dib, date));
            Debug.WriteLine($"Snapper: Screenshot copied: {output.Copied}, saved: {output.SavedPath ?? "(none)"}");
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Snapper: Screenshot failed: {error}");
            if (!disposed) ErrorAlerts.ShowCaptureFailure(error);
        }
    }
    private sealed class ScreenshotEnvironment(AppSettings settings, byte[] png, byte[] dib, DateTime date) : IScreenshotOutputEnvironment
    {
        public bool CopyToClipboard() => Support.Clipboard.Copy(png, dib);
        public string Save(string folder) => ScreenshotFileWriter.Save(png, folder, date);
        public string? ShowSaveFolderFailure(string folder, Exception error) => ErrorAlerts.ShowSaveFolderFailure(folder, error, settings);
        public void ShowError(string title, string message) => ErrorAlerts.ShowError(title, message);
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
        tray?.UpdateShortcutTexts(HotkeyBindings.From(Settings));
    }

    public void SuspendHotkeys() { if (!disposed) hotkeys?.Suspend(); }
    public void ResumeHotkeys() { if (!disposed) hotkeys?.Resume(Settings); }
    public void OpenSettings(SettingsTab? tab = null)
    {
        if (disposed || hotkeys is null) return;
        settingsWindow ??= new SettingsWindowController(new SettingsViewModel(Settings, hotkeys, SuspendHotkeys, ResumeHotkeys));
        settingsWindow.Show(tab);
    }

    public void Exit()
    {
        if (termination.Request(recording is not null)) { StopRecording(); return; }
        FinishExit();
    }
    private void FinishExit()
    {
        settingsWindow?.AllowClose();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DismissRecordingOverlay();
        settingsWindow?.Dispose();
        settingsWindow = null;
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
        shutdownBlocker?.Dispose(); shutdownBlocker = null;
        overlay.Cancel();
        Displays.Dispose();
        d3d.Dispose();
        shutterSound.Dispose();
        tray?.Dispose();
        tray = null;
    }
}
