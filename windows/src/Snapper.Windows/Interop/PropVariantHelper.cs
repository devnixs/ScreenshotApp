using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
namespace Snapper.Windows.Interop;

internal static class PropVariantHelper
{
    public static void SetByte(IWICMetadataQueryWriter writer, string path, byte value)
    { var variant = new PROPVARIANT { vt = VARENUM.VT_UI1 }; variant.bVal = value; Set(writer, path, ref variant); }
    public static void SetUInt16(IWICMetadataQueryWriter writer, string path, ushort value)
    { var variant = new PROPVARIANT { vt = VARENUM.VT_UI2 }; variant.uiVal = value; Set(writer, path, ref variant); }
    public static unsafe void SetBytes(IWICMetadataQueryWriter writer, string path, ReadOnlySpan<byte> bytes)
    {
        var variant = new PROPVARIANT { vt = VARENUM.VT_UI1 | VARENUM.VT_VECTOR };
        variant.caub.cElems = checked((uint)bytes.Length);
        variant.caub.pElems = (byte*)Marshal.AllocCoTaskMem(bytes.Length);
        bytes.CopyTo(new Span<byte>(variant.caub.pElems, bytes.Length));
        Set(writer, path, ref variant);
    }
    private static unsafe void Set(IWICMetadataQueryWriter writer, string path, ref PROPVARIANT value)
    {
        try { fixed (char* name = path) writer.SetMetadataByName(new PCWSTR(name), value); }
        finally { _ = PInvoke.PropVariantClear(ref value); }
    }
}
