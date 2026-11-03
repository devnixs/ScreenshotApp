using System.Buffers.Binary;
using Snapper.Core.Support.Imaging;
namespace Snapper.Core.Tests;
public sealed class IcoWriterTests
{
    [Fact] public void DirectoryOffsetsAndPayloads()
    {
        byte[] first = [1, 2, 3], last = [4, 5];
        var bytes = IcoWriter.Write([(16, first), (256, last)]);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 2, 0 }, bytes[..6]);
        Assert.Equal(new byte[] { 16, 16, 0, 0, 1, 0, 32, 0 }, bytes[6..14]);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14)));
        Assert.Equal(38, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 0, 32, 0 }, bytes[22..30]);
        Assert.Equal(41, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(34)));
        Assert.Equal(first, bytes[38..41]); Assert.Equal(last, bytes[41..]);
    }
    [Fact] public void InvalidDirectories()
    {
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([(0, [])]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([(257, [])]));
        Assert.Throws<ArgumentException>(() => IcoWriter.Write([(16, []), (16, [])]));
    }
}
