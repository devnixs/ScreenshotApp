namespace Snapper.Core.Geometry;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public PixelSize Size => new(Width, Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));
}
