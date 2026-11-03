using Snapper.Core.Geometry;

namespace Snapper.Core.Tests;

internal static class DisplayFixtures
{
    internal static readonly DisplayInfo P = new(@"\\.\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), 96, true);
    internal static readonly DisplayInfo R = new(@"\\.\DISPLAY2", new(1920, 0, 2880, 1620), new(1920, 0, 2880, 1548), 144, false);
    internal static readonly DisplayInfo L = new(@"\\.\DISPLAY3", new(-3840, -540, 3840, 2160), new(-3840, -540, 3840, 2160), 192, false);
    internal static readonly DisplayInfo A = new(@"\\.\DISPLAY4", new(0, -1440, 2560, 1440), new(0, -1440, 2560, 1440), 120, false);
    internal static readonly DisplayInfo[] All = [P, R, L, A];
    internal static DisplayInfo Single(int dpi) => new($"S{dpi}", new(0, 0, 20 * dpi, 1080 * dpi / 96), new(0, 0, 20 * dpi, 1080 * dpi / 96), dpi, true);
    internal static void Equal(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-9);
        Assert.Equal(expected.Y, actual.Y, 1e-9);
    }
    internal static void Equal(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-9);
        Assert.Equal(expected.Y, actual.Y, 1e-9);
        Assert.Equal(expected.Width, actual.Width, 1e-9);
        Assert.Equal(expected.Height, actual.Height, 1e-9);
    }
}
