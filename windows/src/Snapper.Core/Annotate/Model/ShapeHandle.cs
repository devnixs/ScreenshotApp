namespace Snapper.Core.Annotate.Model;

/// <summary>Corners are named in top-left-origin, y-down image pixel space.</summary>
public enum ShapeHandle { TopLeft, TopRight, BottomLeft, BottomRight, Start, End }

public static class ShapeHandleExtensions
{
    public static ShapeHandle Opposite(this ShapeHandle handle) => handle switch
    {
        ShapeHandle.TopLeft => ShapeHandle.BottomRight, ShapeHandle.TopRight => ShapeHandle.BottomLeft,
        ShapeHandle.BottomLeft => ShapeHandle.TopRight, ShapeHandle.BottomRight => ShapeHandle.TopLeft,
        ShapeHandle.Start => ShapeHandle.End, ShapeHandle.End => ShapeHandle.Start,
        _ => throw new ArgumentOutOfRangeException(nameof(handle))
    };
}
