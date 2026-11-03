using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Snapper.Windows.Capture;
using Snapper.Windows.Interop;
using Snapper.Windows.Support;
namespace Snapper.Windows.Recording;

/// <summary>Dispatcher facade. Writer callbacks use the same dispatcher priority, preserving FIFO startup ordering.</summary>
internal sealed class RecordingSession
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly RecordingLifecycle lifecycle = new();
    private readonly Direct3D11Helper helper;
    private readonly DisplayChangeWatcher displays;
    private readonly RecordingConfiguration configuration;
    private readonly DisplayInfo display;
    private readonly AppSettings settings;
    private MicrophoneAudioSession? audio;
    private readonly WindowRecordingSource? window;
    private readonly string outputPath;
    private readonly Action onStarted, onStopping, onEnded;
    private readonly DispatcherTimer startTimer, stopTimer;
    private readonly CancellationTokenSource startupCancellation = new();
    private Direct3D11Helper.DeviceLease? device;
    private Mp4Writer? writer;
    private CaptureFrameSource? source;
    private Task? cleanup, startup, deletion;
    private bool ended;
    public RecordingSession(RecordingConfiguration configuration, DisplayInfo display, string outputPath,
        Direct3D11Helper helper, DisplayChangeWatcher displays, AppSettings settings, Action onStarted, Action onStopping, Action onEnded, WindowRecordingSource? window = null)
    {
        this.configuration = configuration; this.display = display; this.outputPath = outputPath; this.window = window;
        this.helper = helper; this.displays = displays; this.settings = settings; this.onStarted = onStarted; this.onStopping = onStopping; this.onEnded = onEnded;
        startTimer = new DispatcherTimer { Interval = RecordingLifecycle.StartTimeout };
        startTimer.Tick += StartTimedOut;
        stopTimer = new DispatcherTimer { Interval = RecordingLifecycle.StopTimeout };
        stopTimer.Tick += StopTimedOut;
    }
    public bool IsActive => !ended;
    public void Start() => Send(new RecordingEvent.Start(outputPath));
    public void Stop() => Send(new RecordingEvent.StopRequested());
    public void Cancel() => Send(new RecordingEvent.CancelRequested());
    public bool WaitForWriter(TimeSpan timeout) => writer?.Completed.Wait(timeout) ?? true;
    private void Post(Action callback) { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(callback); }
    private void Send(RecordingEvent e)
    {
        dispatcher.VerifyAccess();
        if (ended) return;
        var previous = lifecycle.Phase;
        var effects = lifecycle.Handle(e);
        if (lifecycle.Phase is RecordingPhase.Recording && previous is not RecordingPhase.Recording) { startTimer.Stop(); onStarted(); }
        if (lifecycle.Phase is not (RecordingPhase.Starting or RecordingPhase.Recording)) onStopping();
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case RecordingEffect.StartCapture:
                    displays.Changed += DisplaysChanged; startTimer.Start(); startup = StartCaptureAsync(); break;
                case RecordingEffect.StopCapture: StopCapture(); break;
                case RecordingEffect.ScheduleStopTimeout: stopTimer.Start(); break;
                case RecordingEffect.Reveal(var path): ExplorerReveal.RevealFile(path); break;
                case RecordingEffect.DeleteFile(var path): deletion = DeleteFileAsync(path); break;
                case RecordingEffect.ShowError(var failure): ErrorAlerts.ShowError("Couldn't record the screen", failure.Message); break;
                case RecordingEffect.Ended: _ = EndAsync(); break;
            }
        }
    }
    private async Task StartCaptureAsync()
    {
        Direct3D11Helper.DeviceLease? acquired = null;
        Mp4Writer? createdWriter = null;
        CaptureFrameSource? createdSource = null;
        MicrophoneSource? openedMicrophone = null;
        MicrophoneAudioSession? createdAudio = null;
        try
        {
            // Snapshot intent on the dispatcher, but check current privacy/device state on the MTA pool at every start.
            var microphoneSetting = settings.RecordMicrophone;
            var microphoneTask = Task.Run(() =>
            {
                var inputs = new MicrophoneCaptureInputs(microphoneSetting, MicrophonePermission.Authorization,
                    MicrophonePermission.HasAudioInputDevice, RecordingFormat.Mp4); // 0046 passes the snapshotted export format.
                if (!MicrophoneCapturePolicy.ShouldAttemptMicrophone(inputs)) return (Source: (MicrophoneSource?)null, Result: MicrophoneOpenResult.NoDevice);
                var result = MicrophoneSource.TryOpen(out var microphone, validateFirstRead: true);
                return (Source: microphone, Result: result);
            });
            var deviceTask = Task.Run(() => helper.Acquire(videoSupport: true));
            try { await Task.WhenAll(microphoneTask, deviceTask); }
            finally
            {
                if (deviceTask.IsCompletedSuccessfully) acquired = deviceTask.Result;
                if (microphoneTask.IsCompletedSuccessfully) openedMicrophone = microphoneTask.Result.Source;
            }
            Debug.WriteLine($"Snapper: Microphone start: {microphoneTask.Result.Result}");
            startupCancellation.Token.ThrowIfCancellationRequested();
            createdWriter = await Mp4Writer.CreateAsync(outputPath, configuration, acquired ?? throw new InvalidOperationException("Missing recording device."), openedMicrophone?.Format);
            createdWriter.FirstSampleWritten += FirstSampleWritten;
            createdWriter.Finished += WriterFinished;
            createdWriter.Failed += WriterFailed;
            startupCancellation.Token.ThrowIfCancellationRequested();
            await createdWriter.BeginWritingAsync();
            if (openedMicrophone is not null && createdWriter.HasAudio)
            {
                createdAudio = new(openedMicrophone, createdWriter);
                createdAudio.Start();
            }
            startupCancellation.Token.ThrowIfCancellationRequested();
            createdSource = await Task.Run(() => CaptureFrameSource.CreateAsync(acquired, configuration, display, createdWriter, window));
            startupCancellation.Token.ThrowIfCancellationRequested();
            device = acquired; writer = createdWriter; source = createdSource; audio = createdAudio;
            createdSource.SourceClosed += SourceClosed; createdSource.Failed += SourceFailed;
            await Task.Run(createdSource.Start);
        }
        catch (Exception error)
        {
            createdWriter?.Abort();
            await Task.Run(() =>
            {
                createdSource?.Dispose();
                createdAudio?.Dispose();
                if (createdAudio is null) openedMicrophone?.Dispose();
            });
            if (acquired is not null)
            {
                var toRelease = acquired; var pending = createdWriter;
                await Task.Run(() => { pending?.Completed.Wait(); toRelease.Dispose(); });
            }
            if (error is not OperationCanceledException && !ended) Send(new RecordingEvent.CaptureFailedToStart(new RecordingFailure(error.Message)));
        }
    }
    private void FirstSampleWritten() => Post(() => { startTimer.Stop(); Send(new RecordingEvent.CaptureStarted()); });
    private void WriterFinished() => Post(() => Send(new RecordingEvent.OutputFinished()));
    private void WriterFailed(RecordingFailure failure, bool content) => Post(() =>
        Send(lifecycle.Phase is RecordingPhase.Starting ? new RecordingEvent.CaptureFailedToStart(failure) : new RecordingEvent.OutputFailed(failure, content)));
    private void SourceClosed() => Post(() =>
    {
        if (window is null || WindowFrameTracker.PollOnce(new(window.Handle), window.ThreadId) is WindowPollResult.Gone) Stop();
        else SourceFailed(new InvalidOperationException("The window capture stopped unexpectedly."));
    });
    private void SourceFailed(Exception error) => Post(() =>
    {
        if (lifecycle.Phase is RecordingPhase.Starting) Send(new RecordingEvent.CaptureFailedToStart(new RecordingFailure(error.Message)));
        else if (lifecycle.Phase is RecordingPhase.Recording)
        {
            Send(new RecordingEvent.WindowStreamFailed(new RecordingFailure(error.Message)));
            StopCapture();
        }
    });
    private void StopCapture()
    {
        var activeSource = source; var activeWriter = writer; var activeAudio = audio;
        var stop = MediaFoundationRuntime.QpcTicks;
        activeAudio?.SetEnd(stop);
        _ = Task.Run(() =>
        {
            try { var hold = activeSource?.Stop(stop); activeAudio?.Stop(stop); activeWriter?.RequestFinish(hold); }
            catch (Exception error) { Post(() => Send(new RecordingEvent.OutputFailed(new RecordingFailure(error.Message), activeWriter?.FileHasContent ?? false))); }
        });
    }
    private void DisplaysChanged(object? sender, DisplayConfigurationChangedEventArgs args)
    { if (window is null && RecordingLifecycle.IsRecordedDisplayChanged(display, args.New)) Stop(); }
    private void StartTimedOut(object? sender, EventArgs args)
    { startTimer.Stop(); if (lifecycle.Phase is RecordingPhase.Starting) Send(new RecordingEvent.CaptureFailedToStart(RecordingFailure.NoFrames)); }
    private void StopTimedOut(object? sender, EventArgs args)
    { stopTimer.Stop(); Send(new RecordingEvent.StopTimedOut(writer?.FileHasContent ?? false)); }
    private Task CleanupAsync()
    {
        if (cleanup is not null) return cleanup;
        startupCancellation.Cancel();
        startTimer.Stop(); stopTimer.Stop(); displays.Changed -= DisplaysChanged;
        var activeSource = source; var activeWriter = writer; var activeDevice = device; var activeAudio = audio;
        source = null; device = null; audio = null;
        if (activeSource is not null) { activeSource.SourceClosed -= SourceClosed; activeSource.Failed -= SourceFailed; }
        if (activeWriter is not null)
        {
            activeWriter.FirstSampleWritten -= FirstSampleWritten; activeWriter.Finished -= WriterFinished; activeWriter.Failed -= WriterFailed;
        }
        var pendingStartup = startup;
        cleanup = Task.Run(async () =>
        {
            if (pendingStartup is not null) await pendingStartup;
            activeSource?.Dispose();
            activeAudio?.Dispose();
            activeWriter?.Dispose();
            activeWriter?.Completed.Wait();
            activeDevice?.Dispose();
        });
        return cleanup;
    }
    private async Task DeleteFileAsync(string path)
    {
        // A cancelled file is deleted only after native finalization/abort actually releases its handle.
        // This continues even if the lifecycle stop timeout has already returned the app to idle.
        await CleanupAsync();
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine($"Snapper: Deleting recording: {error.Message}"); }
    }
    private async Task EndAsync()
    {
        if (ended) return;
        ended = true;
        try { await (deletion ?? CleanupAsync()).WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (TimeoutException) { Debug.WriteLine("Snapper: Native writer remains busy after stop timeout."); }
        startTimer.Tick -= StartTimedOut; stopTimer.Tick -= StopTimedOut;
        onEnded();
    }
}
