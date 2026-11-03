using System.Buffers.Binary;
namespace Snapper.Core.Support;
public static class DibEncoder
{
    public static byte[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.Length)
            throw new ArgumentException("The pixel buffer must match the positive image dimensions.");
        var stride = checked((width * 3 + 3) & ~3);
        var result = new byte[checked(40 + stride * height)];
        BinaryPrimitives.WriteInt32LittleEndian(result, 40);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 24);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), stride * height);
        for (var row = 0; row < height; row++)
            for (var x = 0; x < width; x++)
                bgra.Slice(((height - row - 1) * width + x) * 4, 3).CopyTo(result.AsSpan(40 + row * stride + x * 3, 3));
        return result;
    }
}
