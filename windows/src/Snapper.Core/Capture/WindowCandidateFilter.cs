using System.Collections.Frozen;
namespace Snapper.Core.Capture;

public static class WindowCandidateFilter
{
    public static readonly IReadOnlySet<string> ExcludedClassNames = new[]
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public const int MinimumSize = 4;
    public static bool IsSelectable(WindowCandidate c, uint ownProcessId, IReadOnlySet<nint> excludedHandles) =>
        c.IsVisible && !c.IsMinimized && !c.IsCloaked && !c.IsToolWindow && c.ProcessId != ownProcessId &&
        !excludedHandles.Contains(c.Handle) && !ExcludedClassNames.Contains(c.ClassName) &&
        c.Frame.Width >= MinimumSize && c.Frame.Height >= MinimumSize;
}
