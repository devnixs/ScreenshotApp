using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public enum SelectionMode { Rectangle, Window }
public abstract record SelectionSessionOutcome
{
    public sealed record None : SelectionSessionOutcome;
    public sealed record Rectangle(SelectionResult Result) : SelectionSessionOutcome;
    public sealed record WindowClick(PixelPoint At) : SelectionSessionOutcome;
}

/// <summary>Interaction in global physical pixels. Mode changes abandon the current press.</summary>
public struct SelectionSession
{
    private RectSelectionTracker tracker;
    public RectSelectionTracker Tracker => tracker;
    public SelectionMode Mode { get; private set; }
    public bool IsMouseDown { get; private set; }
    public bool IsPressStale { get; private set; }
    public bool IsWindowClickArmed { get; private set; }
    public void ToggleMode()
    {
        Mode = Mode == SelectionMode.Rectangle ? SelectionMode.Window : SelectionMode.Rectangle;
        tracker = new(); IsWindowClickArmed = false; IsPressStale = IsMouseDown;
    }
    public void MouseDown(PointD point, IReadOnlyList<DisplayInfo> displays)
    {
        IsMouseDown = true; IsPressStale = false;
        if (Mode == SelectionMode.Rectangle) tracker.MouseDown(point, displays);
        else IsWindowClickArmed = true;
    }
    public void MouseDragged(PointD point)
    { if (Mode == SelectionMode.Rectangle && !IsPressStale) tracker.Drag(point); }
    public SelectionSessionOutcome MouseUp(PointD point)
    {
        IsMouseDown = false;
        try
        {
            if (IsPressStale) { IsPressStale = false; return new SelectionSessionOutcome.None(); }
            if (Mode == SelectionMode.Window) return IsWindowClickArmed
                ? new SelectionSessionOutcome.WindowClick(new((int)point.X, (int)point.Y)) : new SelectionSessionOutcome.None();
            var outcome = tracker.MouseUp(point);
            return outcome.Kind == RectSelectionOutcomeKind.Selected && outcome.Result is { } result
                ? new SelectionSessionOutcome.Rectangle(result) : new SelectionSessionOutcome.None();
        }
        finally { IsWindowClickArmed = false; }
    }
    // Capture loss abandons the press without changing the user's chosen mode.
    public void Reset() { tracker = new(); IsMouseDown = IsPressStale = IsWindowClickArmed = false; }
}
