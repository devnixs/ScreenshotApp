using Snapper.Core.Capture;
using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public static class WindowFrameCrop
{
    public static PixelRect SourceRect(PixelSize contentSize, PixelRect windowRect, PixelRect extendedFrame) =>
        WindowCaptureCrop.Compute(windowRect, extendedFrame, contentSize) ?? new PixelRect(0, 0, contentSize.Width, contentSize.Height);
}
