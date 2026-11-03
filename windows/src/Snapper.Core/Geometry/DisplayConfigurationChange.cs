namespace Snapper.Core.Geometry;

public static class DisplayConfigurationChange
{
    public static bool HasChanged(IReadOnlyList<DisplayInfo> old, IReadOnlyList<DisplayInfo> current) =>
        !old.Select(Key).ToHashSet().SetEquals(current.Select(Key));

    /// <summary>Work-area changes reposition floating controls without cancelling capture selections.</summary>
    public static bool HasWorkAreaChanged(IReadOnlyList<DisplayInfo> old, IReadOnlyList<DisplayInfo> current) =>
        !old.Select(d => (d.DeviceName, d.WorkArea)).ToHashSet().SetEquals(current.Select(d => (d.DeviceName, d.WorkArea)));

    private static (string, PixelRect, int, bool) Key(DisplayInfo display) =>
        (display.DeviceName, display.Bounds, display.Dpi, display.IsPrimary);
}
