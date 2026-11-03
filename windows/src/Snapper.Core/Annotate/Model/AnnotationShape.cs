using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

public abstract record ShapeKind
{
    private ShapeKind() { }
    public sealed record Rectangle : ShapeKind
    {
        private RectD rect;
        public RectD Rect { get => rect; init => rect = value.Standardized(); }
        public Rectangle(RectD rect) => Rect = rect;
        public void Deconstruct(out RectD rect) => rect = Rect;
    }
    public sealed record Arrow(PointD Start, PointD End) : ShapeKind;
    public sealed record Text(TextBox Box) : ShapeKind;
}

/// <summary>Value geometry in image pixels. Widths use image DIPs times capture scale;
/// callers convert view-DIP tolerances to image pixels at the current zoom.</summary>
public sealed record AnnotationShape(Guid Id, ShapeKind Kind, RgbaColor Color, StrokeWidth StrokeWidth)
{
    public AnnotationShape(ShapeKind kind, RgbaColor color, StrokeWidth width) : this(Guid.NewGuid(), kind, color, width) { }
    public const double HitSlopViewDips = 4, HandleRadiusViewDips = 6, MinimumDragViewDips = 3;
    public double StrokePixels(double pixelsPerDip) => StrokeWidth.Points() * pixelsPerDip;
    public double FontPixelSize(double pixelsPerDip) => StrokeWidth.TextPointSize() * pixelsPerDip;
    public ArrowGeometry? GetArrowGeometry(double pixelsPerDip) => Kind is ShapeKind.Arrow(var start, var end)
        ? ArrowGeometry.Make(start, end, StrokeWidth, pixelsPerDip) : null;

    public RectD Bounds(double pixelsPerDip)
    {
        switch (Kind)
        {
            case ShapeKind.Rectangle(var r):
                var half = StrokePixels(pixelsPerDip) / 2;
                return new(r.X - half, r.Y - half, r.Width + 2 * half, r.Height + 2 * half);
            case ShapeKind.Arrow(var start, var end): return ArrowGeometry.Make(start, end, StrokeWidth, pixelsPerDip).Bounds;
            case ShapeKind.Text(var box): return new(box.Origin.X, box.Origin.Y, box.MeasuredSize.Width, box.MeasuredSize.Height);
            default: throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }

    public bool HitTest(PointD p, double pixelsPerDip, double slop)
    {
        var threshold = StrokePixels(pixelsPerDip) / 2 + slop;
        switch (Kind)
        {
            case ShapeKind.Rectangle(var r): return ShapeGeometry.DistanceToOutline(p, r) <= threshold;
            case ShapeKind.Arrow(var start, var end):
                var head = ArrowGeometry.Make(start, end, StrokeWidth, pixelsPerDip);
                return ShapeGeometry.DistanceToSegment(p, start, end) <= threshold ||
                    ShapeGeometry.TriangleContains(p, head.Tip, head.BaseLeft, head.BaseRight);
            case ShapeKind.Text(var box):
                return p.X >= box.Origin.X && p.X <= box.Origin.X + box.MeasuredSize.Width &&
                    p.Y >= box.Origin.Y && p.Y <= box.Origin.Y + box.MeasuredSize.Height;
            default: throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }

    public IReadOnlyList<(ShapeHandle Handle, PointD Position)> Handles => Kind switch
    {
        ShapeKind.Rectangle(var r) => [(ShapeHandle.TopLeft, new(r.Left, r.Top)), (ShapeHandle.TopRight, new(r.Right, r.Top)),
            (ShapeHandle.BottomLeft, new(r.Left, r.Bottom)), (ShapeHandle.BottomRight, new(r.Right, r.Bottom))],
        ShapeKind.Arrow(var start, var end) => [(ShapeHandle.Start, start), (ShapeHandle.End, end)],
        ShapeKind.Text => [],
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };

    public ShapeHandle? HandleHitTest(PointD p, double radius)
    {
        ShapeHandle? nearest = null;
        var minimum = double.PositiveInfinity;
        foreach (var (handle, position) in Handles)
        {
            var distance = double.Hypot(p.X - position.X, p.Y - position.Y);
            if (distance <= radius && distance < minimum) { nearest = handle; minimum = distance; }
        }
        return nearest;
    }

    public AnnotationShape Moved(double dx, double dy)
    {
        PointD Translate(PointD p) => new(p.X + dx, p.Y + dy);
        return this with { Kind = Kind switch
        {
            ShapeKind.Rectangle(var r) => new ShapeKind.Rectangle(new(r.X + dx, r.Y + dy, r.Width, r.Height)),
            ShapeKind.Arrow(var start, var end) => new ShapeKind.Arrow(Translate(start), Translate(end)),
            ShapeKind.Text(var box) => new ShapeKind.Text(box with { Origin = Translate(box.Origin) }),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        } };
    }

    /// <summary>Pass the returned handle into the next event to keep the opposite corner fixed.
    /// A handle belonging to another kind is a programmer error; safely leaves the shape unchanged.</summary>
    public (AnnotationShape Shape, ShapeHandle Handle) Resized(ShapeHandle handle, PointD p, bool constrained)
    {
        switch (Kind)
        {
            case ShapeKind.Rectangle:
                if (handle is not (ShapeHandle.TopLeft or ShapeHandle.TopRight or ShapeHandle.BottomLeft or ShapeHandle.BottomRight)) return (this, handle);
                var anchor = Handles.First(item => item.Handle == handle.Opposite()).Position;
                var corner = constrained ? ShapeGeometry.SquareCorner(anchor, p) : p;
                var left = corner.X < anchor.X;
                var top = corner.Y < anchor.Y;
                var next = top ? left ? ShapeHandle.TopLeft : ShapeHandle.TopRight : left ? ShapeHandle.BottomLeft : ShapeHandle.BottomRight;
                return (this with { Kind = new ShapeKind.Rectangle(ShapeGeometry.NormalizedRect(anchor, corner)) }, next);
            case ShapeKind.Arrow(var start, var end):
                if (handle is not (ShapeHandle.Start or ShapeHandle.End)) return (this, handle);
                var fixedPoint = handle == ShapeHandle.Start ? end : start;
                var moving = constrained ? ShapeGeometry.SnappedEndpoint(fixedPoint, p) : p;
                return (this with { Kind = handle == ShapeHandle.Start ? new ShapeKind.Arrow(moving, end) : new ShapeKind.Arrow(start, moving) }, handle);
            case ShapeKind.Text: return (this, handle);
            default: throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }

    public static RectD RectangleFromDrag(PointD start, PointD current, bool constrained) =>
        ShapeGeometry.NormalizedRect(start, constrained ? ShapeGeometry.SquareCorner(start, current) : current);
    public static (PointD Start, PointD End) ArrowFromDrag(PointD start, PointD current, bool constrained) =>
        (start, constrained ? ShapeGeometry.SnappedEndpoint(start, current) : current);
    public static AnnotationShape? MakeRectangle(PointD dragFrom, PointD to, bool constrained, RgbaColor color, StrokeWidth width, double minimumDrag)
    {
        var rect = RectangleFromDrag(dragFrom, to, constrained);
        return rect.Width >= minimumDrag && rect.Height >= minimumDrag && rect.Width > 0 && rect.Height > 0
            ? new(new ShapeKind.Rectangle(rect), color, width) : null;
    }
    public static AnnotationShape? MakeArrow(PointD dragFrom, PointD to, bool constrained, RgbaColor color, StrokeWidth width, double minimumDrag)
    {
        var (start, end) = ArrowFromDrag(dragFrom, to, constrained);
        var length = double.Hypot(end.X - start.X, end.Y - start.Y);
        return length >= minimumDrag && length > 0 ? new(new ShapeKind.Arrow(start, end), color, width) : null;
    }
    public static AnnotationShape MakeText(PointD origin, RgbaColor color, StrokeWidth width) =>
        new(new ShapeKind.Text(new(origin, "", new(0, 0))), color, width);
    public AnnotationShape WithColor(RgbaColor color) => this with { Color = color };
    /// <summary>Text callers must also supply a newly measured size in the same document edit.</summary>
    public AnnotationShape WithStrokeWidth(StrokeWidth width) => this with { StrokeWidth = width };
    /// <summary>Text callers must also supply a newly measured size in the same document edit.</summary>
    public AnnotationShape WithText(string text) => Kind is ShapeKind.Text(var box)
        ? this with { Kind = new ShapeKind.Text(box with { Text = text }) } : this;
    public AnnotationShape WithMeasuredSize(SizeD size) => Kind is ShapeKind.Text(var box)
        ? this with { Kind = new ShapeKind.Text(box with { MeasuredSize = size }) } : this;
    public AnnotationShape WithStrokeWidth(StrokeWidth width, double pixelsPerDip, ITextMeasurer measurer)
    {
        var copy = WithStrokeWidth(width);
        return Kind is ShapeKind.Text(var box)
            ? copy.WithMeasuredSize(measurer.LayoutSize(box.Text, TextMetrics.FontPixelSize(width, pixelsPerDip))) : copy;
    }
}
