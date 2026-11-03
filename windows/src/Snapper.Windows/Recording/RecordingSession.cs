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
    private readonly bool recordMicrophone;
    private readonly RecordingFormat requestedFormat;
    private MicrophoneAudioSession? audio;
    private readonly WindowRecordingSource? window;
    private string outputPath;
    private readonly string finalPath;
    private readonly DateTimeOffset startDate;
    private RecordingExportPlan exportPlan;
    private string? convertedPath;
    private readonly CancellationTokenSource conversionCancellation = new();
    private Task<ConversionResult>? conversionWork;
    private bool preparing, pendingPreparationStop, pendingPreparationCancel, forcedSessionEnd;
    private sealed record ConversionResult(string? FinalPath, string? OriginalPath, RecordingFailure? Failure);
    private readonly Action onStarted, onStopping, onEnded;
    private readonly DispatcherTimer startTimer, stopTimer;
    private readonly CancellationTokenSource startupCancellation = new();
    private Direct3D11Helper.DeviceLease? device;
    private Mp4Writer? writer;
    private CaptureFrameSource? source;
    private Task? cleanup, startup, deletion, terminalFileWork;
    private bool ended;
    public RecordingSession(RecordingConfiguration configuration, DisplayInfo display, string outputPath,
        Direct3D11Helper helper, DisplayChangeWatcher displays, AppSettings settings, Action onStarted, Action onStopping, Action onEnded, RecordingExportSettings exportSettings, DateTimeOffset startDate, WindowRecordingSource? window = null)
    {
        this.configuration = configuration; this.display = display; finalPath = this.outputPath = outputPath; this.window = window;
        this.startDate = startDate; requestedFormat = exportSettings.Format;
        exportPlan = exportSettings.Plan(configuration.PixelWidth, configuration.PixelHeight, configuration.Scale);
        this.helper = helper; this.displays = displays; recordMicrophone = settings.RecordMicrophone; this.onStarted = onStarted; this.onStopping = onStopping; this.onEnded = onEnded;
        startTimer = new DispatcherTimer { Interval = RecordingLifecycle.StartTimeout };
        startTimer.Tick += StartTimedOut;
        stopTimer = new DispatcherTimer { Interval = RecordingLifecycle.StopTimeout };
        stopTimer.Tick += StopTimedOut;
    }
    public bool IsActive => !ended;
    public bool IsConverting => lifecycle.IsConverting;
    public async void Start()
    {
        if (preparing || lifecycle.Phase is not RecordingPhase.Idle) return;
        preparing = true;
        if (exportPlan.Converts)
        {
            try
            {
                var id = Guid.NewGuid();
                var paths = RecordingTempFiles.For(Path.GetTempPath(), id);
                await Task.Run(() => Directory.CreateDirectory(RecordingTempFiles.Directory(Path.GetTempPath())));
                outputPath = paths.Capture; convertedPath = RecordingTempFiles.OutputFor(Path.GetTempPath(), id, exportPlan.Format);
            }
            catch (Exception error)
            {
                Debug.WriteLine($"Snapper: Temporary recording unavailable; using MP4 High: {error.Message}");
                exportPlan = new(RecordingFormat.Mp4, null);
                try { outputPath = await Task.Run(() => RecordingFilePath.Reserve(Path.GetDirectoryName(finalPath) ?? throw new IOException("Missing save folder."), startDate)); }
                catch (Exception failure) { preparing = false; ErrorAlerts.ShowError("Couldn't record the screen", failure.Message); ended = true; onEnded(); return; }
            }
        }
        preparing = false;
        if (forcedSessionEnd) { ended = true; return; }
        Send(new RecordingEvent.Start(outputPath, exportPlan.Converts));
        if (pendingPreparationCancel) Cancel(); else if (pendingPreparationStop) Stop();
    }
    public void Stop() { if (preparing) pendingPreparationStop = true; else Send(new RecordingEvent.StopRequested()); }
    public void Cancel() { if (preparing) pendingPreparationCancel = true; else Send(new RecordingEvent.CancelRequested()); }
    public void ForceSessionEnd()
    {
        forcedSessionEnd = true;
        if (ForcedRecordingEndDecision.Decide(IsConverting) == ForcedRecordingEndAction.ExistingStopBehavior)
        {
            Stop();
            if (WaitForWriter(TimeSpan.FromSeconds(5)) && startup?.IsCompleted != false)
            {
                // These tasks contain worker-only cleanup/I/O. Do not await a dispatcher reveal continuation here.
                try
                {
                    if (deletion is not null) deletion.GetAwaiter().GetResult();
                    else if (terminalFileWork is not null) terminalFileWork.GetAwaiter().GetResult();
                    else if (exportPlan.Converts && lifecycle.Phase is not (RecordingPhase.Cancelled or RecordingPhase.Failed) && File.Exists(outputPath))
                        Task.Run(() => TrySaveOriginal(outputPath)).GetAwaiter().GetResult();
                }
                catch (Exception error) { Debug.WriteLine($"Snapper: Forced recording save: {error.GetBaseException().Message}"); }
            }
            return;
        }
        conversionCancellation.Cancel();
        // WM_ENDSESSION is the only UI path allowed to wait. Conversion/fallback I/O never needs the dispatcher.
        try
        {
            var result = conversionWork?.GetAwaiter().GetResult();
            if (result?.FinalPath is not null)
            {
                // Conversion committed just before WM_ENDSESSION; it is already safely saved.
                // The normal dispatcher completion will not run while session ending, so remove its source here.
                RecordingOutputFiles.TryDelete(outputPath);
            }
        }
        catch (Exception error) { Debug.WriteLine($"Snapper: Forced conversion shutdown: {error.GetBaseException().Message}"); }
    }
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
        _ = ApplyEffectsAsync(effects);
    }
    private async Task ApplyEffectsAsync(IReadOnlyList<RecordingEffect> effects)
    {
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
                case RecordingEffect.ShowError(var failure): if (!forcedSessionEnd) ErrorAlerts.ShowError(failure.Title, failure.Message); break;
                case RecordingEffect.ReleaseCapture: _ = CleanupAsync(); break;
                case RecordingEffect.Convert(var path): BeginConversion(path); break;
                case RecordingEffect.SaveOriginal(var path):
                    var saving = SaveOriginalAndRevealAsync(path);
                    try { await saving.WaitAsync(TimeSpan.FromSeconds(1)); }
                    catch (TimeoutException) { Debug.WriteLine("Snapper: Saving partial recording continues after native release."); }
                    break;
                case RecordingEffect.Ended: await EndAsync(); break;
            }
        }
    }
    private void BeginConversion(string capture)
    {
        var released = CleanupAsync();
        var target = convertedPath ?? throw new InvalidOperationException("Missing conversion output.");
        var plan = exportPlan;
        conversionWork = Task.Run(async () =>
        {
            try
            {
                await released;
                switch (plan.Transcode)
                {
                    case RecordingTranscode.Mp4(var bitrate): await Mp4Transcoder.TranscodeAsync(capture, target, bitrate, conversionCancellation.Token); break;
                    case RecordingTranscode.Gif(var gif): await GifEncoder.EncodeAsync(capture, target, gif, conversionCancellation.Token); break;
                    default: throw new InvalidOperationException("Unsupported recording conversion.");
                }
                conversionCancellation.Token.ThrowIfCancellationRequested();
                var final = RecordingOutputFiles.SaveOriginal(target, SaveDirectory, startDate, finalPath, conversionCancellation.Token, plan.FinalCaptureKind);
                // Force may arrive during an atomic publish. Preserve the source recording instead of the converted file.
                if (conversionCancellation.IsCancellationRequested) { RecordingOutputFiles.TryDelete(final); throw new OperationCanceledException(); }
                return new ConversionResult(final, null, null);
            }
            catch (Exception error)
            {
                RecordingOutputFiles.TryDelete(target);
                var original = TrySaveOriginal(capture);
                return new ConversionResult(null, original, ConversionFailure(plan, error.Message));
            }
        });
        _ = ObserveConversionAsync(conversionWork);
    }
    private async Task ObserveConversionAsync(Task<ConversionResult> work)
    {
        var result = await work;
        if (forcedSessionEnd) return;
        if (result.FinalPath is { } final) Send(new RecordingEvent.ConversionFinished(final));
        else Send(new RecordingEvent.ConversionFailed(result.Failure ?? ConversionFailure(exportPlan, "The recording couldn't be converted."), result.OriginalPath));
    }
    private static RecordingFailure ConversionFailure(RecordingExportPlan plan, string message) =>
        plan.Format == RecordingFormat.Gif ? RecordingFailure.GifConversion(message) : RecordingFailure.Compression(message);
    private string SaveDirectory => Path.GetDirectoryName(finalPath) ?? throw new IOException("Missing recording save folder.");
    private string? TrySaveOriginal(string capture)
    {
        try { return RecordingOutputFiles.SaveOriginal(capture, SaveDirectory, startDate); }
        catch (Exception error)
        {
            Debug.WriteLine($"Snapper: Saving original recording: {error.Message}");
            RecordingOutputFiles.TryDelete(capture);
            return null;
        }
    }
    private async Task SaveOriginalAndRevealAsync(string capture)
    {
        var released = CleanupAsync();
        var work = Task.Run(async () => { await released; return TrySaveOriginal(capture); });
        terminalFileWork = work;
        var saved = await work;
        if (saved is not null && !forcedSessionEnd) ExplorerReveal.RevealFile(saved);
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
            var microphoneSetting = recordMicrophone;
            var microphoneTask = Task.Run(() =>
            {
                var inputs = new MicrophoneCaptureInputs(microphoneSetting, MicrophonePermission.Authorization,
                    MicrophonePermission.HasAudioInputDevice, requestedFormat);
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
    private void WriterFinished() => Post(async () =>
    {
        // A first frame can be encoded before StartCaptureAsync's final dispatcher continuation.
        // Enter conversion only after that continuation, so forced shutdown can wait on worker-only cleanup safely.
        if (startup is { } pending) await pending;
        if (!forcedSessionEnd) Send(new RecordingEvent.OutputFinished());
    });
    private void WriterFailed(RecordingFailure failure, bool content) => Post(async () =>
    {
        if (startup is { } pending) await pending;
        if (!forcedSessionEnd) Send(lifecycle.Phase is RecordingPhase.Starting
            ? new RecordingEvent.CaptureFailedToStart(failure) : new RecordingEvent.OutputFailed(failure, content));
    });
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
    private Task DeleteFileAsync(string path)
    {
        // Keep release and deletion worker-only so forced WM_ENDSESSION never waits for a dispatcher continuation.
        var released = CleanupAsync();
        return Task.Run(async () => { await released; RecordingOutputFiles.TryDelete(path); });
    }
    private async Task EndAsync()
    {
        if (ended) return;
        ended = true;
        // Deferred normal Exit must not terminate before original-saving or cancellation deletion finishes.
        // These waits are asynchronous; only a native-release-only timeout retains the existing bounded end path.
        if (terminalFileWork is not null || deletion is not null)
        {
            try { await (terminalFileWork ?? deletion ?? Task.CompletedTask); }
            catch (Exception error) { Debug.WriteLine($"Snapper: Finishing recording files: {error.Message}"); }
        }
        else
        {
            try { await CleanupAsync().WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { Debug.WriteLine("Snapper: Native writer remains busy after stop timeout."); }
        }
        startTimer.Tick -= StartTimedOut; stopTimer.Tick -= StopTimedOut;
        onEnded();
    }
}
