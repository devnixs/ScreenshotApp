using System.Diagnostics;

namespace Snapper.Core.App;

public abstract record AppMode
{
    private AppMode() { }
    public sealed record Idle : AppMode;
    public sealed record Selecting(CapturePurpose Purpose) : AppMode;
    /// <summary>Includes startup, recording, finalization and conversion until RecordingEnded.</summary>
    public sealed record Recording : AppMode;
}
public enum TrayButton { Left, Right }
public abstract record AppModeEvent
{
    private AppModeEvent() { }
    public sealed record Shortcut(CapturePurpose Purpose) : AppModeEvent;
    public sealed record MenuItem(CapturePurpose Purpose) : AppModeEvent;
    public sealed record TrayClicked(TrayButton Button) : AppModeEvent;
    public sealed record SelectionFinished : AppModeEvent;
    public sealed record SelectionCancelled : AppModeEvent;
    public sealed record RecordingEnded : AppModeEvent;
}
public abstract record AppModeAction
{
    private AppModeAction() { }
    public sealed record None : AppModeAction;
    public sealed record ShowOverlay(CapturePurpose Purpose) : AppModeAction;
    public sealed record CompleteSelection(CapturePurpose Purpose) : AppModeAction;
    /// <summary>Does not leave Recording. Session Stop must be idempotent and ignore conversion;
    /// pill Stop/Cancel call the session directly and finish with RecordingEnded.</summary>
    public sealed record StopRecording : AppModeAction;
    public sealed record OpenMenu : AppModeAction;
}

/// <summary>Capture routing independent of UI. Enters Recording before capture startup so another
/// selection cannot open; a failed startup must send RecordingEnded. Status clicks while selecting
/// are ignored defensively. Applies each transition before the caller performs its action.</summary>
public sealed class AppModeMachine
{
    public AppMode Mode { get; private set; } = new AppMode.Idle();
    public AppModeAction Handle(AppModeEvent e)
    {
        var (mode, action) = Mode switch
        {
            AppMode.Idle => e switch
            {
                AppModeEvent.Shortcut(var p) => ((AppMode)new AppMode.Selecting(p), (AppModeAction)new AppModeAction.ShowOverlay(p)),
                AppModeEvent.MenuItem(var p) => (new AppMode.Selecting(p), new AppModeAction.ShowOverlay(p)),
                AppModeEvent.TrayClicked => (Mode, new AppModeAction.OpenMenu()),
                AppModeEvent.SelectionFinished or AppModeEvent.SelectionCancelled or AppModeEvent.RecordingEnded => (Mode, new AppModeAction.None()),
                _ => throw new UnreachableException()
            },
            AppMode.Selecting(var p) => e switch
            {
                AppModeEvent.SelectionFinished => (p == CapturePurpose.Record ? new AppMode.Recording() : (AppMode)new AppMode.Idle(), (AppModeAction)new AppModeAction.CompleteSelection(p)),
                AppModeEvent.SelectionCancelled => (new AppMode.Idle(), new AppModeAction.None()),
                AppModeEvent.Shortcut or AppModeEvent.MenuItem or AppModeEvent.TrayClicked or AppModeEvent.RecordingEnded => (Mode, new AppModeAction.None()),
                _ => throw new UnreachableException()
            },
            AppMode.Recording => e switch
            {
                AppModeEvent.Shortcut(CapturePurpose.Record) or AppModeEvent.MenuItem(CapturePurpose.Record) or AppModeEvent.TrayClicked => (Mode, (AppModeAction)new AppModeAction.StopRecording()),
                AppModeEvent.RecordingEnded => (new AppMode.Idle(), new AppModeAction.None()),
                AppModeEvent.Shortcut or AppModeEvent.MenuItem or AppModeEvent.SelectionFinished or AppModeEvent.SelectionCancelled => (Mode, new AppModeAction.None()),
                _ => throw new UnreachableException()
            },
            _ => throw new UnreachableException()
        };
        Mode = mode;
        return action;
    }
}
