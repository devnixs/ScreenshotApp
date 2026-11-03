using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;
public static class WindowHighlightGeometry
{
    public static RectD? LocalRect(PixelRect windowFrame, DisplayInfo display)
    {
        var left = Math.Max(windowFrame.Left, display.Bounds.Left);
        var top = Math.Max(windowFrame.Top, display.Bounds.Top);
        var right = Math.Min(windowFrame.Right, display.Bounds.Right);
        var bottom = Math.Min(windowFrame.Bottom, display.Bounds.Bottom);
        return right <= left || bottom <= top ? null : DisplayGeometry.ToLocalDips(RectD.FromEdges(left, top, right, bottom), display);
    }
}
