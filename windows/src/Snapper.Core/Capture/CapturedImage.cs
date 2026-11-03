using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public sealed class CapturedImage
{
    public CapturedImage(int width, int height, byte[] bgra, double scaleFactor)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.LongLength)
            throw new ArgumentException("The pixel buffer must match the positive image dimensions.");
        Width = width; Height = height; Bgra = bgra; ScaleFactor = scaleFactor;
    }
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }
    public double ScaleFactor { get; }
    public PixelSize Size => new(Width, Height);
}
