namespace Snapper.Core.Recording;
public static class GifMetadata
{
    public static ReadOnlySpan<byte> NetscapeApplicationId => "NETSCAPE2.0"u8;
    public static byte[] LoopForeverData() => [3, 1, 0, 0, 0];
    public const ushort DisposalDoNotDispose = 1;
}
