namespace Snapper.Core.Support.Imaging;

/// <summary>Top-down, straight-alpha RGBA8 pixels.</summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width; Height = height; Pixels = new byte[checked(width * height * 4)];
    }
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public uint GetPixel(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
        var i = (y * Width + x) * 4;
        return (uint)(Pixels[i + 3] << 24 | Pixels[i] << 16 | Pixels[i + 1] << 8 | Pixels[i + 2]);
    }
}
