namespace Snapper.Core.Geometry;

public static class WindowDisplayRule
{
    public static DisplayInfo? Display(RectD windowFrame, IReadOnlyList<DisplayInfo> displays)
    {
        if (DisplayGeometry.DisplayOverlapping(windowFrame, displays) is { } overlapping) return overlapping;
        var center = windowFrame.Standardized().Center;
        DisplayInfo? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var display in displays)
        {
            var distance = DisplayGeometry.SquaredDistance(center, display.Bounds);
            if (distance < bestDistance) { best = display; bestDistance = distance; }
        }
        return best;
    }
}
