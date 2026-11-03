using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Snapper.Core.Support.Imaging;
namespace Snapper.Core.Tests.Fakes;

internal static class PngTestDecoder
{
    public static IReadOnlyList<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        var result = new List<(string, byte[], uint)>();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var name = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length).ToArray();
            result.Add((name, data, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length))));
            offset += 12 + length;
        }
        Assert.Equal(png.Length, offset);
        return result;
    }
    public static RgbaImage Decode(byte[] png)
    {
        var chunks = Chunks(png);
        var header = chunks.Single(c => c.Type == "IHDR").Data;
        var image = new RgbaImage(BinaryPrimitives.ReadInt32BigEndian(header), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, header[8..]);
        using var input = new MemoryStream(chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        for (var y = 0; y < image.Height; y++)
        {
            Assert.Equal(0, zlib.ReadByte());
            zlib.ReadExactly(image.Pixels.AsSpan(y * image.Width * 4, image.Width * 4));
        }
        Assert.Equal(-1, zlib.ReadByte());
        return image;
    }
}
