using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;
public static class WindowSelection
{
    public static SelectionResult? Resolve(WindowHitTester? tester, PixelPoint releasePoint, IReadOnlyList<DisplayInfo> displays)
    {
        if (tester?.WindowAt(releasePoint) is not { } candidate ||
            WindowDisplayRule.Display(RectD.FromPixelRect(candidate.Frame), displays) is not { } display) return null;
        return new WindowSelectionResult(candidate.Handle, candidate.Frame, display);
    }
}
