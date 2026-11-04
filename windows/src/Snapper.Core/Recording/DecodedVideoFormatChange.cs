using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

/// <summary>Accept initial decoder padding, without accepting a changing visible recording.</summary>
public static class DecodedVideoFormatChange
{
    public static bool CanAccept(bool hasProcessedSamples, PixelSize recorded, PixelSize decoded, PixelRect? aperture)
    {
        if (hasProcessedSamples || recorded.Width <= 0 || recorded.Height <= 0 || decoded.Width <= 0 || decoded.Height <= 0) return false;
        var visible = GifFrameCrop.CropFor(decoded.Width, decoded.Height, aperture, recorded.Width, recorded.Height);
        return visible.Width == recorded.Width && visible.Height == recorded.Height;
    }
}
