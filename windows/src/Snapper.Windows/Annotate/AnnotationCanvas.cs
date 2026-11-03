using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Windows.Annotate;

internal sealed class AnnotationCanvas : FrameworkElement, IDisposable
{
    private BitmapSource? image;
    private AnnotationDrawingGesture? gesture;
    private AnnotationShape? preview;
    private AnnotationTool currentTool = AnnotationTools.DefaultTool;
    public AnnotationDocument Document { get; }
    public BitmapSource Image => image ?? throw new ObjectDisposedException(nameof(AnnotationCanvas));
    public AnnotationTool CurrentTool
    {
        get => currentTool;
        set { if (currentTool != value) AbortDrag(); currentTool = value; Mouse.UpdateCursor(); }
    }
    public RgbaColor CurrentColor { get; set; }
    public StrokeWidth CurrentStrokeWidth { get; set; }
    public bool IsDragging => gesture is not null;
    public CanvasTransform Transform => new(Document.ImagePixelSize, Document.PixelsPerDip, new(ActualWidth, ActualHeight), VisualTreeHelper.GetDpi(this).DpiScaleX);
    public AnnotationCanvas(AnnotationDocument document, BitmapSource image)
    {
        Document = document; this.image = image;
        Focusable = true; FocusVisualStyle = null; ClipToBounds = true;
        Document.Changed += DocumentChanged;
        SizeChanged += CanvasSizeChanged;
    }
    private void DocumentChanged(object? sender, EventArgs args) => InvalidateVisual();
    private void CanvasSizeChanged(object sender, SizeChangedEventArgs args) { AbortDrag(); RefreshScaling(); }
    private void RefreshScaling()
    {
        RenderOptions.SetBitmapScalingMode(this, Transform.IsPixelExact ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        InvalidateVisual();
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); AbortDrag(); RefreshScaling(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(SystemColors.AppWorkspaceBrush, null, new Rect(RenderSize));
        var t = Transform;
        if (t.Scale <= 0 || image is null) return;
        dc.PushTransform(new MatrixTransform(t.Scale, 0, 0, t.Scale, t.ImageRectInView.X, t.ImageRectInView.Y));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Document.ImagePixelSize.Width, Document.ImagePixelSize.Height)));
        ShapeRenderer.DrawImage(dc, image); ShapeRenderer.Draw(dc, Document.Shapes, Document.PixelsPerDip);
        if (preview is not null) ShapeRenderer.Draw(dc, preview, Document.PixelsPerDip);
        dc.Pop(); dc.Pop();
    }
    private PointD Position(MouseEventArgs e) { var p = e.GetPosition(this); return new(p.X, p.Y); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus();
        AbortDrag();
        gesture = AnnotationDrawingGesture.Begin(CurrentTool, Position(e), Transform, CurrentColor, CurrentStrokeWidth);
        if (gesture is null) return;
        if (!CaptureMouse()) { AbortDrag(); return; }
        RefreshPreview(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (gesture is not null && IsMouseCaptured) { gesture.Update(Position(e)); RefreshPreview(); e.Handled = true; }
        Mouse.UpdateCursor();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (gesture is null) return;
        gesture.Update(Position(e));
        var shape = gesture.Shape(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), completing: true);
        AbortDrag();
        if (shape is not null) Document.Add(shape);
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); AbortDrag(); }
    protected override void OnQueryCursor(QueryCursorEventArgs e)
    {
        e.Cursor = CurrentTool.CreatesShapesByDragging() && Transform.IsInsideImage(Position(e)) ? Cursors.Cross : Cursors.Arrow;
        e.Handled = true;
    }
    public void RefreshPreview()
    { preview = gesture?.Shape(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), completing: false); InvalidateVisual(); }
    public void AbortDrag()
    {
        gesture = null; preview = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
    public void Dispose()
    {
        AbortDrag(); Document.Changed -= DocumentChanged; SizeChanged -= CanvasSizeChanged; image = null;
    }
}
