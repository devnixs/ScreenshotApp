using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;

/// <summary>Canvas DIPs and image pixels both use top-left/y-down coordinates. This is not global display geometry.</summary>
public readonly record struct CanvasTransform
{
    public PixelSize ImagePixelSize { get; }
    public double Scale { get; }
    public RectD ImageRectInView { get; }
    public bool IsPixelExact { get; }
    public CanvasTransform(PixelSize imagePixelSize, double pixelsPerDip, SizeD viewSize, double viewScale)
    {
        ImagePixelSize = imagePixelSize;
        if (!new[] { imagePixelSize.Width, (double)imagePixelSize.Height, pixelsPerDip, viewSize.Width, viewSize.Height, viewScale }
            .All(v => double.IsFinite(v) && v > 0)) return;
        Scale = Math.Min(Math.Min(viewSize.Width / imagePixelSize.Width, viewSize.Height / imagePixelSize.Height), 1 / pixelsPerDip);
        var w = imagePixelSize.Width * Scale; var h = imagePixelSize.Height * Scale;
        ImageRectInView = new(Math.Round((viewSize.Width - w) / 2 * viewScale, MidpointRounding.AwayFromZero) / viewScale,
            Math.Round((viewSize.Height - h) / 2 * viewScale, MidpointRounding.AwayFromZero) / viewScale, w, h);
        IsPixelExact = Math.Abs(Scale * viewScale - 1) < 1e-9;
    }
    public PointD? ImagePoint(PointD p) => Scale > 0 ? new((p.X - ImageRectInView.X) / Scale, (p.Y - ImageRectInView.Y) / Scale) : null;
    public PointD ViewPoint(PointD p) => new(p.X * Scale + ImageRectInView.X, p.Y * Scale + ImageRectInView.Y);
    public RectD ViewRect(RectD r) { var p = ViewPoint(new(r.X, r.Y)); return new(p.X, p.Y, r.Width * Scale, r.Height * Scale); }
    public RectD? ImageRect(RectD r) => ImagePoint(new(r.X, r.Y)) is { } p ? new(p.X, p.Y, r.Width / Scale, r.Height / Scale) : null;
    public double ViewLength(double length) => length * Scale;
    public bool IsInsideImage(PointD p) => Scale > 0 && p.X >= ImageRectInView.Left && p.X <= ImageRectInView.Right && p.Y >= ImageRectInView.Top && p.Y <= ImageRectInView.Bottom;
    public PointD? ClampedImagePoint(PointD p) => ImagePoint(p) is { } v ? new(Math.Clamp(v.X, 0, ImagePixelSize.Width), Math.Clamp(v.Y, 0, ImagePixelSize.Height)) : null;
}
