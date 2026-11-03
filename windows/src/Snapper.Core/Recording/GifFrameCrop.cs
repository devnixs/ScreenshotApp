using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public static class GifFrameCrop
{
    public static PixelRect CropFor(int decodedWidth, int decodedHeight, PixelRect? aperture, int expectedWidth, int expectedHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodedWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodedHeight);
        if (aperture is { IsEmpty: false } rect && rect.X >= 0 && rect.Y >= 0 &&
            (long)rect.X + rect.Width <= decodedWidth && (long)rect.Y + rect.Height <= decodedHeight) return rect;
        return new(0, 0, Math.Clamp(expectedWidth, 1, decodedWidth), Math.Clamp(expectedHeight, 1, decodedHeight));
    }
}
