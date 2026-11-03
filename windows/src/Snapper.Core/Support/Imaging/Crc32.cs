namespace Snapper.Core.Support.Imaging;

public static class Crc32
{
    private static readonly uint[] Table = MakeTable();
    private static uint[] MakeTable()
    {
        var result = new uint[256];
        for (uint i = 0; i < result.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            result[i] = value;
        }
        return result;
    }
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var b in bytes) crc = Table[(crc ^ b) & 255] ^ (crc >> 8);
        return crc ^ uint.MaxValue;
    }
}
