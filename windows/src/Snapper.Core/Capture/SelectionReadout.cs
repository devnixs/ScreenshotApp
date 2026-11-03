using System.Globalization;
using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public static class SelectionReadout
{
    public const double Offset = 12, Margin = 4;
    public static string Text(SizeD dips) => $"{Math.Round(dips.Width, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)} × {Math.Round(dips.Height, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}";
    public static PointD Origin(SizeD labelSize, PointD cursor, RectD display)
    {
        var left = display.Left + Margin; var top = display.Top + Margin;
        var right = display.Right - Margin; var bottom = display.Bottom - Margin;
        var x = cursor.X + Offset; var y = cursor.Y + Offset;
        if (x + labelSize.Width > right) x = cursor.X - Offset - labelSize.Width;
        if (y + labelSize.Height > bottom) y = cursor.Y - Offset - labelSize.Height;
        return new(Math.Max(left, Math.Min(x, right - labelSize.Width)), Math.Max(top, Math.Min(y, bottom - labelSize.Height)));
    }
}
