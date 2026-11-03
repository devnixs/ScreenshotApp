using System.Buffers.Binary;
using System.Text;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class ShutterSoundSynthesizerTests
{
    [Fact] public void ExactWaveHeader()
    {
        var b=ShutterSoundSynthesizer.CreateWav();Assert.Equal(10628,b.Length);
        Assert.Equal("RIFF",Encoding.ASCII.GetString(b,0,4));Assert.Equal("WAVEfmt ",Encoding.ASCII.GetString(b,8,8));Assert.Equal("data",Encoding.ASCII.GetString(b,36,4));
        Assert.Equal(10620,Int(b,4));Assert.Equal(16,Int(b,16));Assert.Equal(1,Short(b,20));Assert.Equal(1,Short(b,22));
        Assert.Equal(44100,Int(b,24));Assert.Equal(88200,Int(b,28));Assert.Equal(2,Short(b,32));Assert.Equal(16,Short(b,34));Assert.Equal(10584,Int(b,40));
    }
    [Fact] public void DeterministicNormalizedClickAndClack()
    {
        var b=ShutterSoundSynthesizer.CreateWav();Assert.Equal(b,ShutterSoundSynthesizer.CreateWav());
        var samples=Enumerable.Range(0,5292).Select(i=>(int)Short(b,44+i*2)).ToArray();
        Assert.Equal(16384,samples.Max(Math.Abs));Assert.Equal(0,samples[0]);Assert.Equal(0,samples[^1]);
        var first=samples.Take(441).Select(x=>(double)x*x).Average();Assert.True(Math.Sqrt(first)>2000);
        int Peak(double start,double end)=>samples.Skip((int)(start*44100)).Take((int)((end-start)*44100)).Max(Math.Abs);
        Assert.True(Peak(.045,.055)>=.3*16384);Assert.True(Peak(.045,.055)>Peak(.035,.044));
    }
    private static int Int(byte[] b,int i)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i));
    private static short Short(byte[] b,int i)=>BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i));
}
