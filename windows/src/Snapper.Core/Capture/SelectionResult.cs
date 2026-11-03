using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

// 0032 adds WindowSelection: find consumers' UnreachableException arms when extending this hierarchy.
public abstract record SelectionResult;
public sealed record RectSelection(DisplayInfo Display, PixelRect Rect) : SelectionResult;
