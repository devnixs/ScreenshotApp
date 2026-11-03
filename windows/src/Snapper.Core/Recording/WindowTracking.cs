using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public readonly record struct WindowProbe(bool Exists, uint ThreadId, bool IsVisible, bool IsMinimized, bool IsCloaked, PixelRect? ExtendedFrame);
public abstract record WindowPollResult
{
    public sealed record Unavailable : WindowPollResult;
    public sealed record Gone : WindowPollResult;
    public sealed record Present(PixelRect Frame, bool IsOnscreen) : WindowPollResult;
}
public static class WindowPollClassifier
{
    public static WindowPollResult Classify(WindowProbe probe, uint expectedThreadId, IReadOnlyList<DisplayInfo> displays)
    {
        if (!probe.Exists || probe.ThreadId != expectedThreadId) return new WindowPollResult.Gone();
        if (probe.ExtendedFrame is not { } frame) return new WindowPollResult.Unavailable();
        return new WindowPollResult.Present(frame, probe.IsVisible && !probe.IsMinimized && !probe.IsCloaked &&
            DisplayGeometry.DisplayOverlapping(RectD.FromPixelRect(frame), displays) is not null);
    }
}
public readonly record struct WindowTrackingState(PixelRect Frame, bool IsVisible = true, bool IsClosed = false);
public abstract record WindowTrackingEvent
{
    public sealed record Unchanged : WindowTrackingEvent;
    public sealed record Moved(PixelRect Frame) : WindowTrackingEvent;
    public sealed record Hidden : WindowTrackingEvent;
    public sealed record Shown(PixelRect Frame) : WindowTrackingEvent;
    public sealed record Closed : WindowTrackingEvent;
}
public static class WindowTrackingReducer
{
    public static (WindowTrackingState State, WindowTrackingEvent Event) Reduce(WindowTrackingState state, WindowPollResult poll)
    {
        if (state.IsClosed || poll is WindowPollResult.Unavailable) return (state, new WindowTrackingEvent.Unchanged());
        if (poll is WindowPollResult.Gone) return (state with { IsClosed = true }, new WindowTrackingEvent.Closed());
        if (poll is WindowPollResult.Present(var frame, var onscreen))
        {
            var next = state with { Frame = frame, IsVisible = onscreen };
            WindowTrackingEvent result = !onscreen ? state.IsVisible ? new WindowTrackingEvent.Hidden() : new WindowTrackingEvent.Unchanged()
                : !state.IsVisible ? new WindowTrackingEvent.Shown(frame)
                : frame != state.Frame ? new WindowTrackingEvent.Moved(frame) : new WindowTrackingEvent.Unchanged();
            return (next, result);
        }
        throw new ArgumentOutOfRangeException(nameof(poll));
    }
}
