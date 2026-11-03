using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

public static class TextMetrics
{
    public static double FontPixelSize(StrokeWidth width, double pixelsPerDip) => width.TextPointSize() * pixelsPerDip;
    public static double EditorFontSize(double fontPixelSize, double canvasScale) => fontPixelSize * canvasScale;
    public static PointD NewBoxOrigin(PointD click, double lineHeight, SizeD imageSize) =>
        new(Math.Min(Math.Max(0, click.X), Math.Max(0, Math.BitDecrement(imageSize.Width))),
            Math.Min(Math.Max(0, click.Y - lineHeight / 2), Math.Max(0, imageSize.Height - lineHeight)));
}
