using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Windows.Annotate;

/// <summary>Shared image-pixel renderer. The caller supplies the destination transform; export uses identity at 96 DPI.</summary>
internal static class ShapeRenderer
{
    public static void DrawImage(DrawingContext dc, BitmapSource image) => dc.DrawImage(image, new(0, 0, image.PixelWidth, image.PixelHeight));
    public static void Draw(DrawingContext dc, IEnumerable<AnnotationShape> shapes, double pixelsPerDip, double textPixelsPerDip = 1)
    { foreach (var shape in shapes) Draw(dc, shape, pixelsPerDip, textPixelsPerDip); }
    public static void Draw(DrawingContext dc, AnnotationShape shape, double pixelsPerDip, double textPixelsPerDip = 1)
    {
        if (shape.Kind is ShapeKind.Text(var box))
        {
            var size = TextMetrics.FontPixelSize(shape.StrokeWidth, pixelsPerDip);
            var text = TextStyle.CreateFormattedText(box.Text, size, Brush(shape.Color), textPixelsPerDip);
            var glyphs = text.BuildGeometry(Point(box.Origin)); glyphs.Freeze();
            var outline = new Pen(Brush(ContrastingOutline.ColorFor(shape.Color)), 2 * ContrastingOutline.OutlineFractionOfFontSize * size) { LineJoin = PenLineJoin.Round };
            outline.Freeze();
            dc.DrawGeometry(null, outline, glyphs);
            dc.DrawGeometry(Brush(shape.Color), null, glyphs);
            return;
        }
        foreach (var primitive in ShapeDrawingPlan.For(shape, pixelsPerDip))
        {
            switch (primitive)
            {
                case DrawPrimitive.StrokeRect(var rect, var thickness, var color): dc.DrawRectangle(null, Pen(color, thickness), Rect(rect)); break;
                case DrawPrimitive.StrokeLine(var from, var to, var thickness, var color): dc.DrawLine(Pen(color, thickness), Point(from), Point(to)); break;
                case DrawPrimitive.FillPolygon(var points, var color):
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        context.BeginFigure(Point(points[0]), true, true);
                        context.PolyLineTo(points.Skip(1).Select(Point).ToArray(), true, false);
                    }
                    geometry.Freeze(); dc.DrawGeometry(Brush(color), null, geometry); break;
            }
        }
    }
    internal static SolidColorBrush Brush(RgbaColor color)
    {
        var c = color.ToBytes(); var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)); brush.Freeze(); return brush;
    }
    private static Pen Pen(RgbaColor color, double thickness)
    {
        var pen = new Pen(Brush(color), thickness) { LineJoin = PenLineJoin.Miter, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        pen.Freeze(); return pen;
    }
    internal static Point Point(PointD p) => new(p.X, p.Y);
    internal static Rect Rect(RectD r) => new(r.X, r.Y, r.Width, r.Height);
}
