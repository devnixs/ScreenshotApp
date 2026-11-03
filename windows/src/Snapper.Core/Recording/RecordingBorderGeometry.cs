using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public static class RecordingBorderGeometry
{
    public const double OutsetDips = 3, StrokeInsetDips = 1.5;
    public const double UnderlayWidthDips = 2, DashWidthDips = 1, DashLengthDips = 4, DashGapDips = 4;
    public static int OutsetPixels(double scale) => (int)Math.Ceiling(DisplayGeometry.DipsToPixels(OutsetDips, scale));
    public static PixelRect WindowFrame(PixelRect recordedRect, PixelRect monitorBounds, double scale, bool followsWindow = false)
    {
        var outset = OutsetPixels(scale);
        var expanded = new PixelRect(recordedRect.X - outset, recordedRect.Y - outset,
            recordedRect.Width + 2 * outset, recordedRect.Height + 2 * outset);
        if (followsWindow) return expanded;
        var left = Math.Max(expanded.Left, monitorBounds.Left);
        var top = Math.Max(expanded.Top, monitorBounds.Top);
        return new(left, top, Math.Max(0, Math.Min(expanded.Right, monitorBounds.Right) - left),
            Math.Max(0, Math.Min(expanded.Bottom, monitorBounds.Bottom) - top));
    }
    public static RectD StrokeRect(SizeD windowSizeDips) => new(StrokeInsetDips, StrokeInsetDips,
        windowSizeDips.Width - 2 * StrokeInsetDips, windowSizeDips.Height - 2 * StrokeInsetDips);
}
