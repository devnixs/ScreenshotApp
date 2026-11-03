using System.Buffers.Binary;
using System.Text;
using Snapper.Core.Support.Imaging;
using Snapper.Core.Tests.Fakes;
namespace Snapper.Core.Tests;
public sealed class PngWriterTests
{
    [Fact] public void HeaderEndAndAllCrcs()
    {
        var bytes = PngWriter.Encode(new(3, 2)); var chunks = PngTestDecoder.Chunks(bytes);
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(c => c.Type));
        Assert.Equal(13, chunks[0].Data.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(chunks[0].Data));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(chunks[0].Data.AsSpan(4)));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }, bytes[^12..]);
        foreach (var (type, data, crc) in chunks) Assert.Equal(crc, Crc32.Compute(Encoding.ASCII.GetBytes(type).Concat(data).ToArray()));
    }
    [Fact] public void PixelRoundTripIncludesPartialAlpha()
    {
        var image = new RgbaImage(3, 2);
        for (var i = 0; i < image.Pixels.Length; i++) image.Pixels[i] = (byte)(i * 11);
        var decoded = PngTestDecoder.Decode(PngWriter.Encode(image));
        Assert.Equal(image.Width, decoded.Width); Assert.Equal(image.Height, decoded.Height); Assert.Equal(image.Pixels, decoded.Pixels);
    }
}
