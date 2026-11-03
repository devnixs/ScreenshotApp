using System.Buffers.Binary;
using Snapper.Core.App;
using Snapper.Core.Tests.Fakes;
namespace Snapper.Core.Tests;
public sealed class AppIconAssetTests
{
    [Fact] public void CheckedInIconMatchesGeneratedPixels()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Snapper.ico"));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes)); Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        var expectedOffset = 6 + 16 * AppIconDesign.Sizes.Count;
        for (var i = 0; i < AppIconDesign.Sizes.Count; i++)
        {
            var entry = bytes.AsSpan(6 + i * 16, 16);
            var size = entry[0] == 0 ? 256 : entry[0]; Assert.Equal(AppIconDesign.Sizes[i], size);
            Assert.Equal(entry[0], entry[1]); Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]); var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.Equal(expectedOffset, offset); Assert.InRange(offset + length, offset, bytes.Length);
            var decoded = PngTestDecoder.Decode(bytes.AsSpan(offset, length).ToArray());
            Assert.Equal(size, decoded.Width); Assert.Equal(size, decoded.Height);
            Assert.True(AppIconDesign.Render(size).Pixels.SequenceEqual(decoded.Pixels), "Regenerate: dotnet run --project windows/tools/Snapper.IconGen -- windows/src/Snapper.Windows/Assets/Snapper.ico");
            expectedOffset += length;
        }
        Assert.Equal(bytes.Length, expectedOffset);
    }
}
