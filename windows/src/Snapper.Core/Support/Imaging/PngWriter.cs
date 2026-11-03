using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace Snapper.Core.Support.Imaging;

public static class PngWriter
{
    public static byte[] Encode(RgbaImage image)
    {
        using var result = new MemoryStream();
        result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; header[9] = 6;
        Chunk(result, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            for (var y = 0; y < image.Height; y++)
            { zlib.WriteByte(0); zlib.Write(image.Pixels.AsSpan(y * image.Width * 4, image.Width * 4)); }
        Chunk(result, "IDAT", compressed.ToArray());
        Chunk(result, "IEND", []);
        return result.ToArray();
    }
    private static void Chunk(Stream stream, string name, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); stream.Write(number);
        var body = new byte[checked(data.Length + 4)];
        Encoding.ASCII.GetBytes(name).CopyTo(body, 0); data.CopyTo(body, 4);
        stream.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Compute(body)); stream.Write(number);
    }
}
