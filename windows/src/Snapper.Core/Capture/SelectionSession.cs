using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public abstract record SelectionSessionOutcome
{
    public sealed record None : SelectionSessionOutcome;
    public sealed record Rectangle(SelectionResult Result) : SelectionSessionOutcome;
}

public struct SelectionSession
{
    private RectSelectionTracker tracker;
    public RectSelectionTracker Tracker => tracker;
    public bool IsMouseDown { get; private set; }
    public void MouseDown(PointD point, IReadOnlyList<DisplayInfo> displays)
    { IsMouseDown = true; tracker.MouseDown(point, displays); }
    public void MouseDragged(PointD point) => tracker.Drag(point);
    public SelectionSessionOutcome MouseUp(PointD point)
    {
        IsMouseDown = false;
        var outcome = tracker.MouseUp(point);
        return outcome.Kind == RectSelectionOutcomeKind.Selected && outcome.Result is { } result
            ? new SelectionSessionOutcome.Rectangle(result) : new SelectionSessionOutcome.None();
    }
    public void Reset() { tracker.Reset(); IsMouseDown = false; }
}
