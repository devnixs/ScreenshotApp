using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Snapper.Core.App;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using Drawing = System.Drawing;

namespace Snapper.Windows.App;

internal static class TrayIconRenderer
{
    public static Drawing.Icon Render(TrayIconAppearance appearance, int sizePx)
    {
        var drawing = appearance.Glyph == TrayGlyph.Viewfinder
            ? TrayGlyphGeometry.Viewfinder(sizePx) : TrayGlyphGeometry.RecordDot(sizePx);
        using var bitmap = new Drawing.Bitmap(sizePx, sizePx, PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Transparent);
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            var color = Drawing.Color.FromArgb(unchecked((int)appearance.Argb));
            using var pen = new Drawing.Pen(color, (float)drawing.StrokeWidth)
            {
                StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Miter
            };
            // Each pair shares a corner. A joined path avoids drawing over that corner twice.
            for (var index = 0; index < drawing.Lines.Count; index += 2)
            {
                var horizontal = drawing.Lines[index];
                var vertical = drawing.Lines[index + 1];
                graphics.DrawLines(pen, new[]
                {
                    new Drawing.PointF((float)horizontal.X2, (float)horizontal.Y2),
                    new Drawing.PointF((float)horizontal.X1, (float)horizontal.Y1),
                    new Drawing.PointF((float)vertical.X2, (float)vertical.Y2)
                });
            }
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new Drawing.SolidBrush(color);
            foreach (var circle in drawing.Circles)
            {
                var bounds = new Drawing.RectangleF((float)(circle.CenterX - circle.Radius),
                    (float)(circle.CenterY - circle.Radius), (float)(circle.Radius * 2), (float)(circle.Radius * 2));
                if (circle.Filled) graphics.FillEllipse(brush, bounds);
                else graphics.DrawEllipse(pen, bounds);
            }
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)borrowed.Clone();
        }
        finally { PInvoke.DestroyIcon(new HICON(handle)); }
    }
}
