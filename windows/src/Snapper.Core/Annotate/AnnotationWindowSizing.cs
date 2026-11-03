using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;

public readonly record struct AnnotationWindowPlacement(SizeD CanvasDip, PixelRect WindowRect);
public static class AnnotationWindowSizing
{
    public static AnnotationWindowPlacement Frame(PixelSize imagePixelSize, double imagePixelsPerDip, PixelRect workArea, double monitorScale, SizeD chromeDip, SizeD minCanvasDip)
    {
        var width = DisplayGeometry.PixelsToDips(workArea.Width, monitorScale);
        var height = DisplayGeometry.PixelsToDips(workArea.Height, monitorScale);
        var iw = imagePixelSize.Width / imagePixelsPerDip; var ih = imagePixelSize.Height / imagePixelsPerDip;
        var fit = Math.Clamp(Math.Min(1, Math.Min((0.8 * width - chromeDip.Width) / iw, (0.8 * height - chromeDip.Height) / ih)), 0, 1);
        var aw = Math.Max(0, width - chromeDip.Width); var ah = Math.Max(0, height - chromeDip.Height);
        var canvas = new SizeD(Math.Floor(Math.Min(aw, Math.Max(Math.Min(minCanvasDip.Width, aw), iw * fit)) + 1e-6),
            Math.Floor(Math.Min(ah, Math.Max(Math.Min(minCanvasDip.Height, ah), ih * fit)) + 1e-6));
        var w = Math.Min(workArea.Width, Round(DisplayGeometry.DipsToPixels(canvas.Width + chromeDip.Width, monitorScale)));
        var h = Math.Min(workArea.Height, Round(DisplayGeometry.DipsToPixels(canvas.Height + chromeDip.Height, monitorScale)));
        return new(canvas, new(workArea.X + Round((workArea.Width - w) / 2.0), workArea.Y + Round((workArea.Height - h) / 2.0), w, h));
    }
    public static PixelRect Cascaded(PixelRect frame, IReadOnlyCollection<PixelPoint> existingTopLefts, PixelRect workArea, int stepPixels)
    {
        if (stepPixels <= 0) return frame;
        while (existingTopLefts.Contains(new(frame.X, frame.Y)))
        {
            var next = frame with { X = frame.X + stepPixels, Y = frame.Y + stepPixels };
            if (next.Left < workArea.Left || next.Top < workArea.Top || next.Right > workArea.Right || next.Bottom > workArea.Bottom) break;
            frame = next;
        }
        return frame;
    }
    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
