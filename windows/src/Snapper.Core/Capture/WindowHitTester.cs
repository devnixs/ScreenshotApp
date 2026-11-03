using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

/// <summary>Immutable snapshot; excluded windows are skipped, never treated as blockers.</summary>
public sealed class WindowHitTester
{
    private readonly WindowCandidate[] candidates;
    private readonly uint ownProcessId;
    private readonly HashSet<nint> excludedHandles;
    public WindowHitTester(IReadOnlyList<WindowCandidate> candidates, uint ownProcessId, IReadOnlySet<nint> excludedHandles)
    { this.candidates = candidates.ToArray(); this.ownProcessId = ownProcessId; this.excludedHandles = new(excludedHandles); }
    public bool IsSelectable(WindowCandidate c) => WindowCandidateFilter.IsSelectable(c, ownProcessId, excludedHandles);
    public WindowCandidate? WindowAt(PixelPoint p) => candidates.FirstOrDefault(c => IsSelectable(c) &&
        c.Frame.Left <= p.X && p.X < c.Frame.Right && c.Frame.Top <= p.Y && p.Y < c.Frame.Bottom);
}
