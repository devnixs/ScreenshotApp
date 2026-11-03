namespace Snapper.Core.Geometry;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + Width / 2, Y + Height / 2);
    public SizeD Size => new(Width, Height);
    public RectD Standardized() => FromEdges(Math.Min(Left, Right), Math.Min(Top, Bottom), Math.Max(Left, Right), Math.Max(Top, Bottom));
    public static RectD FromEdges(double left, double top, double right, double bottom) => new(left, top, right - left, bottom - top);
    public static RectD FromPixelRect(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
