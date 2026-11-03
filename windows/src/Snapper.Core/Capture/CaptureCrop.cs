using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public static class CaptureCrop
{
    public static PixelRect? MonitorLocal(PixelRect selection, PixelRect monitorBounds, PixelSize frameSize)
    {
        var display = new DisplayInfo("", monitorBounds, monitorBounds, 96, false);
        var local = DisplayGeometry.ToMonitorLocal(DisplayGeometry.CaptureRect(RectD.FromPixelRect(selection), display), display);
        var left = Math.Clamp(local.Left, 0, Math.Max(0, frameSize.Width));
        var top = Math.Clamp(local.Top, 0, Math.Max(0, frameSize.Height));
        var right = Math.Clamp(local.Right, left, Math.Max(left, frameSize.Width));
        var bottom = Math.Clamp(local.Bottom, top, Math.Max(top, frameSize.Height));
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }
    public static void CopyRows(ReadOnlySpan<byte> source, int sourceRowPitch, int width, int height, Span<byte> destination)
    {
        if (width <= 0 || height <= 0 || (long)width * 4 > sourceRowPitch ||
            (long)(height - 1) * sourceRowPitch + (long)width * 4 > source.Length ||
            (long)width * height * 4 > destination.Length)
            throw new ArgumentException("The pitched source and destination must contain the requested image.");
        var rowLength = checked(width * 4);
        for (var row = 0; row < height; row++)
        {
            var output = destination.Slice(row * rowLength, rowLength);
            source.Slice(row * sourceRowPitch, rowLength).CopyTo(output);
            for (var alpha = 3; alpha < output.Length; alpha += 4) output[alpha] = 255;
        }
    }
}
