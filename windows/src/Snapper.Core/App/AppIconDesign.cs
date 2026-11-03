using Snapper.Core.Support.Imaging;
namespace Snapper.Core.App;

public static class AppIconDesign
{
    public static IReadOnlyList<int> Sizes { get; } = Array.AsReadOnly(new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
    public const uint TileArgb = 0xFF0F6CBD, GlyphArgb = 0xFFFFFFFF;
    public static int CornerRadius(int size) => (int)Math.Round(size * 0.1875, MidpointRounding.AwayFromZero);
    public static int GlyphInset(int size) => (int)Math.Round(size * 0.125, MidpointRounding.AwayFromZero);
    public static RgbaImage Render(int size)
    {
        if (size is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(size));
        var image = new RgbaImage(size, size);
        CoverageRasterizer.Paint(image, CoverageRasterizer.Coverage(size, size, CoverageRasterizer.RoundedRect(0, 0, size, size, CornerRadius(size))), TileArgb);
        var inset = GlyphInset(size); var glyph = TrayGlyphGeometry.Viewfinder(size - 2 * inset);
        var masks = glyph.Lines.Select(l => CoverageRasterizer.Segment(l.X1 + inset, l.Y1 + inset, l.X2 + inset, l.Y2 + inset, glyph.StrokeWidth)).ToList();
        masks.AddRange(glyph.Circles.Select(c => c.Filled
            ? CoverageRasterizer.Disc(c.CenterX + inset, c.CenterY + inset, c.Radius)
            : CoverageRasterizer.Ring(c.CenterX + inset, c.CenterY + inset, c.Radius, glyph.StrokeWidth)));
        CoverageRasterizer.Paint(image, CoverageRasterizer.Coverage(size, size, CoverageRasterizer.Union(masks.ToArray())), GlyphArgb);
        return image;
    }
    public static byte[] WriteIco() => IcoWriter.Write(Sizes.Select(size => (size, PngWriter.Encode(Render(size)))).ToArray());
}
