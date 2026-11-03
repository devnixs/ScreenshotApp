namespace Snapper.Core.Support.Imaging;

public static class IcoWriter
{
    public static byte[] Write(IReadOnlyList<(int Size, byte[] Png)> entries)
    {
        if (entries.Count == 0 || entries.Count > ushort.MaxValue || entries.Any(e => e.Size is < 1 or > 256) || entries.Select(e => e.Size).Distinct().Count() != entries.Count)
            throw new ArgumentException("Icons require unique sizes from 1 through 256.", nameof(entries));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)entries.Count);
        var offset = checked(6 + entries.Count * 16);
        foreach (var (size, png) in entries)
        {
            writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(png.Length); writer.Write(offset); offset = checked(offset + png.Length);
        }
        foreach (var (_, png) in entries) writer.Write(png);
        return stream.ToArray();
    }
}
