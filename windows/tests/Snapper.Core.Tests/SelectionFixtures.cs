using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
internal static class SelectionFixtures
{
    public static readonly DisplayInfo A = Display(@"\\.\DISPLAY1", new(0, 0, 1920, 1080), 96, true);
    public static readonly DisplayInfo B = Display(@"\\.\DISPLAY2", new(1920, 0, 2880, 1620), 144);
    public static readonly DisplayInfo C = Display(@"\\.\DISPLAY3", new(-3840, -500, 3840, 2160), 192);
    public static DisplayInfo Display(string name, PixelRect bounds, int dpi, bool primary = false) => new(name, bounds, bounds, dpi, primary);
}
