namespace Snapper.Core.Geometry;

public static class DisplayConfigurationChange
{
    public static bool HasChanged(IReadOnlyList<DisplayInfo> old, IReadOnlyList<DisplayInfo> current) =>
        !old.Select(Key).ToHashSet().SetEquals(current.Select(Key));

    private static (string, PixelRect, int, bool) Key(DisplayInfo display) =>
        (display.DeviceName, display.Bounds, display.Dpi, display.IsPrimary);
}
