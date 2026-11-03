using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public sealed record OverlayFrame(RectD? SelectionDips, string? ReadoutText, PointD? ReadoutOriginDips)
{
    public static OverlayFrame Empty { get; } = new(null, null, null);
}
public static class SelectionOverlayRenderState
{
    public static OverlayFrame For(DisplayInfo overlayDisplay, in RectSelectionTracker tracker, Func<string, SizeD> measureLabel)
    {
        if (tracker.StartDisplay?.DeviceName != overlayDisplay.DeviceName || tracker.CurrentRect is not { } rect || tracker.CurrentPoint is not { } cursor)
            return OverlayFrame.Empty;
        var text = SelectionReadout.Text(DisplayGeometry.DipSize(rect, overlayDisplay));
        return new(DisplayGeometry.ToLocalDips(rect, overlayDisplay), text,
            SelectionReadout.Origin(measureLabel(text), DisplayGeometry.ToLocalDips(cursor, overlayDisplay),
                new(0, 0, overlayDisplay.DipSize.Width, overlayDisplay.DipSize.Height)));
    }
}
