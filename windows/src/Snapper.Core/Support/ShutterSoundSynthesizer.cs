using System.Buffers.Binary;
using System.Text;
namespace Snapper.Core.Support;
public static class ShutterSoundSynthesizer
{
    public const int SampleRate = 44100, DurationMs = 120, SampleCount = 5292, PeakAmplitude = 16384, SecondTransientMs = 45;
    public static byte[] CreateWav()
    {
        var signal = new double[SampleCount];
        uint noise = 0x5EED1234;
        double Noise() { noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5; return noise / 2147483648.0 - 1; }
        double Transient(double time, double frequency, double tau, double amplitude)
        {
            if (time < 0) return 0;
            return amplitude * (Noise() * 0.6 + Math.Sin(2 * Math.PI * frequency * time) * 0.4) *
                Math.Min(1, time / 0.001) * Math.Exp(-time / tau);
        }
        double peak = 0;
        for (var n = 0; n < SampleCount; n++)
        {
            var time = n / (double)SampleRate;
            var fade = Math.Min(1, (SampleCount - 1 - n) / (0.005 * SampleRate));
            signal[n] = (Transient(time, 2000, 0.006, 1) + Transient(time - 0.045, 1400, 0.008, 0.7)) * fade;
            peak = Math.Max(peak, Math.Abs(signal[n]));
        }
        var bytes = new byte[44 + SampleCount * 2];
        using var writer = new BinaryWriter(new MemoryStream(bytes), Encoding.ASCII);
        writer.Write("RIFF"u8); writer.Write(bytes.Length - 8); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate); writer.Write(SampleRate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(SampleCount * 2);
        for (var n = 0; n < SampleCount; n++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + n * 2),
                (short)Math.Clamp((int)Math.Round(signal[n] / peak * PeakAmplitude), -PeakAmplitude, PeakAmplitude));
        return bytes;
    }
}
