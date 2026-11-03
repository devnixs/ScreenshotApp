using Snapper.Core.Geometry;
namespace Snapper.Core.Capture;

public abstract record SelectionResult;
/// <summary>Rectangle confined to Display, in global physical pixels.</summary>
public sealed record RectSelection(DisplayInfo Display, PixelRect Rect) : SelectionResult;
/// <summary>Whole extended window frame in global physical pixels; Display has the largest overlap.</summary>
public sealed record WindowSelectionResult(nint Handle, PixelRect Frame, DisplayInfo Display) : SelectionResult;
