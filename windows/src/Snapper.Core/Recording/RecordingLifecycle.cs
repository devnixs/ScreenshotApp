using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public sealed record RecordingFailure(string Message)
{
    public static readonly RecordingFailure Timeout = new("The recording didn't finish saving in time.");
    public static readonly RecordingFailure NoFrames = new("The screen didn't send any frames.");
}
public abstract record RecordingPhase
{
    public sealed record Idle : RecordingPhase;
    public sealed record Starting : RecordingPhase;
    public sealed record Recording : RecordingPhase;
    public sealed record Stopping : RecordingPhase;
    public sealed record Finished(string Path) : RecordingPhase;
    public sealed record Failed(RecordingFailure Failure) : RecordingPhase;
    public sealed record Cancelled : RecordingPhase;
}
public abstract record RecordingEvent
{
    public sealed record Start(string Path) : RecordingEvent;
    public sealed record CaptureStarted : RecordingEvent;
    public sealed record CaptureFailedToStart(RecordingFailure Failure) : RecordingEvent;
    public sealed record StopRequested : RecordingEvent;
    public sealed record CancelRequested : RecordingEvent;
    public sealed record OutputFinished : RecordingEvent;
    public sealed record OutputFailed(RecordingFailure Failure, bool FileHasContent) : RecordingEvent;
    public sealed record StreamStopped(RecordingFailure Failure) : RecordingEvent;
    public sealed record WindowStreamFailed(RecordingFailure Failure) : RecordingEvent;
    public sealed record StopTimedOut(bool FileHasContent) : RecordingEvent;
}
public abstract record RecordingEffect
{
    public sealed record StartCapture : RecordingEffect;
    public sealed record StopCapture : RecordingEffect;
    public sealed record ScheduleStopTimeout : RecordingEffect;
    public sealed record Reveal(string Path) : RecordingEffect;
    public sealed record DeleteFile(string Path) : RecordingEffect;
    public sealed record ShowError(RecordingFailure Failure) : RecordingEffect;
    public sealed record Ended : RecordingEffect;
}
public sealed class RecordingLifecycle
{
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30), StartTimeout = TimeSpan.FromSeconds(5);
    public RecordingPhase Phase { get; private set; } = new RecordingPhase.Idle();
    private string? path;
    private bool pendingStop, pendingCancel;
    private RecordingFailure? streamFailure;
    public static bool IsRecordedDisplayGone(string recordedId, IEnumerable<string> currentIds) => !currentIds.Contains(recordedId);
    public static bool IsRecordedDisplayChanged(DisplayInfo start, IEnumerable<DisplayInfo> current)
    {
        var d = current.FirstOrDefault(d => d.DeviceName == start.DeviceName);
        return d is null || d.Bounds != start.Bounds || d.Dpi != start.Dpi;
    }
    public IReadOnlyList<RecordingEffect> Handle(RecordingEvent e)
    {
        if (Phase is RecordingPhase.Finished or RecordingPhase.Failed or RecordingPhase.Cancelled) return [];
        if (Phase is RecordingPhase.Idle && e is RecordingEvent.Start(var output))
        { path = output; Phase = new RecordingPhase.Starting(); return [new RecordingEffect.StartCapture()]; }
        if (path is null) return [];
        switch (Phase, e)
        {
            case (RecordingPhase.Starting, RecordingEvent.StopRequested): pendingStop = true; return [];
            case (RecordingPhase.Starting, RecordingEvent.CancelRequested): pendingCancel = true; return [];
            case (RecordingPhase.Starting, RecordingEvent.CaptureStarted):
                Phase = pendingStop || pendingCancel ? new RecordingPhase.Stopping() : new RecordingPhase.Recording();
                return Phase is RecordingPhase.Stopping ? [new RecordingEffect.StopCapture(), new RecordingEffect.ScheduleStopTimeout()] : [];
            case (RecordingPhase.Starting, RecordingEvent.CaptureFailedToStart(var failure)):
                Phase = new RecordingPhase.Failed(failure);
                return [new RecordingEffect.DeleteFile(path), new RecordingEffect.ShowError(failure), new RecordingEffect.Ended()];
            case (RecordingPhase.Recording, RecordingEvent.StopRequested or RecordingEvent.CancelRequested):
                pendingCancel = e is RecordingEvent.CancelRequested; Phase = new RecordingPhase.Stopping();
                return [new RecordingEffect.StopCapture(), new RecordingEffect.ScheduleStopTimeout()];
            case (RecordingPhase.Recording, RecordingEvent.WindowStreamFailed(var failure)):
                streamFailure = failure; Phase = new RecordingPhase.Stopping(); return [new RecordingEffect.ScheduleStopTimeout()];
            case (RecordingPhase.Recording, RecordingEvent.StreamStopped):
                Phase = new RecordingPhase.Stopping(); return [new RecordingEffect.ScheduleStopTimeout()];
            case (RecordingPhase.Recording, RecordingEvent.OutputFinished):
                Phase = new RecordingPhase.Finished(path);
                return [new RecordingEffect.StopCapture(), new RecordingEffect.Reveal(path), new RecordingEffect.Ended()];
            case (RecordingPhase.Stopping, RecordingEvent.OutputFinished):
                if (streamFailure is { } f) return ResolveFailure(f, true, path);
                Phase = pendingCancel ? new RecordingPhase.Cancelled() : new RecordingPhase.Finished(path);
                return [pendingCancel ? new RecordingEffect.DeleteFile(path) : new RecordingEffect.Reveal(path), new RecordingEffect.Ended()];
            case (RecordingPhase.Recording, RecordingEvent.OutputFailed(var failure, var content)):
                return new RecordingEffect[] { new RecordingEffect.StopCapture() }.Concat(ResolveFailure(failure, content, path)).ToArray();
            case (RecordingPhase.Stopping, RecordingEvent.OutputFailed(var failure, var content)): return ResolveFailure(failure, content, path);
            case (RecordingPhase.Stopping, RecordingEvent.StopTimedOut(var content)): return ResolveFailure(streamFailure ?? RecordingFailure.Timeout, content, path);
            default: return [];
        }
    }
    private IReadOnlyList<RecordingEffect> ResolveFailure(RecordingFailure failure, bool content, string output)
    {
        if (pendingCancel) { Phase = new RecordingPhase.Cancelled(); return [new RecordingEffect.DeleteFile(output), new RecordingEffect.Ended()]; }
        Phase = content ? new RecordingPhase.Finished(output) : new RecordingPhase.Failed(failure);
        return [content ? new RecordingEffect.Reveal(output) : new RecordingEffect.DeleteFile(output), new RecordingEffect.ShowError(failure), new RecordingEffect.Ended()];
    }
}
