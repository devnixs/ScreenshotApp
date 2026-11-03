using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

/// <summary>Copy cropped BGRX rows from a signed-stride decoder into a tight top-down buffer.</summary>
public static class GifFrameRows
{
    public static byte[] Copy(ReadOnlySpan<byte> source, int scanline0, int pitch, PixelRect crop)
    {
        if (crop.IsEmpty || crop.X < 0 || crop.Y < 0 || Math.Abs((long)pitch) < (long)crop.Right * 4)
            throw new ArgumentException("Invalid decoded crop or stride.");
        var rowBytes = checked(crop.Width * 4);
        var result = new byte[checked(rowBytes * crop.Height)];
        for (var y = 0; y < crop.Height; y++)
        {
            var offset = (long)scanline0 + (long)(crop.Y + y) * pitch + (long)crop.X * 4;
            if (offset < 0 || offset + rowBytes > source.Length) throw new ArgumentException("Decoded buffer is too small.");
            source.Slice((int)offset, rowBytes).CopyTo(result.AsSpan(y * rowBytes, rowBytes));
        }
        return result;
    }
}
