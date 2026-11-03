using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

/// <summary>Top-level window metadata in physical virtual-screen pixels, in snapshot z-order.</summary>
public sealed record WindowCandidate(nint Handle, PixelRect Frame, uint ProcessId, string ClassName,
    bool IsVisible, bool IsMinimized, bool IsCloaked, bool IsToolWindow, string? Title);
