using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Annotate;
public static class ColorRefConversion
{
    public static uint ToColorRef(RgbaColor color) { var c = color.ToBytes(); return (uint)(c.R | c.G << 8 | c.B << 16); }
    public static RgbaColor FromColorRef(uint colorRef) => RgbaColor.FromBytes((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));
}
