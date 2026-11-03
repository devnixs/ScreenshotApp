using System.Buffers.Binary;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class DibEncoderTests
{
    [Fact] public void HeaderBottomUpRowsAndPadding()
    {
        byte[] input=[1,2,3,99,4,5,6,88,7,8,9,77,10,11,12,66,13,14,15,55,16,17,18,44];
        var dib=DibEncoder.FromBgra(input,3,2);
        Assert.Equal(64,dib.Length);Assert.Equal(40,Int(dib,0));Assert.Equal(3,Int(dib,4));Assert.Equal(2,Int(dib,8));
        Assert.Equal(1,BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(12)));Assert.Equal(24,BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(0,Int(dib,16));Assert.Equal(24,Int(dib,20));Assert.All(dib[24..40],b=>Assert.Equal(0,b));
        Assert.Equal(new byte[]{10,11,12,13,14,15,16,17,18,0,0,0,1,2,3,4,5,6,7,8,9,0,0,0},dib[40..]);
    }
    [Fact] public void AlignedRowHasNoPadding(){var dib=DibEncoder.FromBgra(new byte[16],4,1);Assert.Equal(52,dib.Length);Assert.Equal(12,Int(dib,20));}
    [Fact] public void BufferMismatchThrows()=>Assert.Throws<ArgumentException>(()=>DibEncoder.FromBgra(new byte[15],4,1));
    private static int Int(byte[] b,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset));
}
