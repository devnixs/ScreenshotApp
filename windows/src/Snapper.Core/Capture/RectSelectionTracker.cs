using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public enum RectSelectionOutcomeKind { Ignored, TooSmall, Selected }
public readonly record struct RectSelectionOutcome(RectSelectionOutcomeKind Kind, SelectionResult? Result = null);

/// <summary>Drag state in physical virtual-screen pixels, confined to its starting monitor.</summary>
public struct RectSelectionTracker
{
    public const double MinimumSizeDips = 4;
    private DisplayInfo? display;
    private PointD start, current;
    public DisplayInfo? StartDisplay => display;
    public PointD? CurrentPoint => display is null ? null : current;
    public RectD? CurrentRect => display is null ? null : DisplayGeometry.DragRect(start, current, display);
    public void MouseDown(PointD point, IReadOnlyList<DisplayInfo> displays)
    {
        display = DisplayGeometry.NearestDisplay(point, displays);
        if (display is null) { Reset(); return; }
        current = start = DisplayGeometry.Clamp(point, display.Bounds);
    }
    public RectD? Drag(PointD point)
    {
        if (display is null) return null;
        current = DisplayGeometry.Clamp(point, display.Bounds);
        return CurrentRect;
    }
    public RectSelectionOutcome MouseUp(PointD point)
    {
        Drag(point);
        try
        {
            if (display is null || CurrentRect is not { } rect) return new(RectSelectionOutcomeKind.Ignored);
            var size = DisplayGeometry.DipSize(rect, display);
            if (size.Width < MinimumSizeDips - 1e-9 || size.Height < MinimumSizeDips - 1e-9)
                return new(RectSelectionOutcomeKind.TooSmall);
            return new(RectSelectionOutcomeKind.Selected, new RectSelection(display, DisplayGeometry.CaptureRect(rect, display)));
        }
        finally { Reset(); }
    }
    public void Reset() { display = null; start = current = default; }
}
