using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;
public static class WindowCaptureCrop
{
    public static PixelRect? Compute(PixelRect windowRect, PixelRect extendedFrameBounds, PixelSize contentSize)
    {
        if (contentSize.Width <= 0 || contentSize.Height <= 0 || extendedFrameBounds.IsEmpty) return null;
        if (contentSize == extendedFrameBounds.Size) return new(0, 0, contentSize.Width, contentSize.Height);
        var left = Math.Max(0, extendedFrameBounds.Left - windowRect.Left);
        var top = Math.Max(0, extendedFrameBounds.Top - windowRect.Top);
        var right = Math.Min(contentSize.Width, extendedFrameBounds.Right - windowRect.Left);
        var bottom = Math.Min(contentSize.Height, extendedFrameBounds.Bottom - windowRect.Top);
        return right <= left || bottom <= top ? null : PixelRect.FromEdges(left, top, right, bottom);
    }
}
