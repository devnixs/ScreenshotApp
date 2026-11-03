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
    public SelectionEditor Editor { get; }
    public BitmapSource Image => image ?? throw new ObjectDisposedException(nameof(AnnotationCanvas));
    public AnnotationTool CurrentTool
    {
        get => currentTool;
        set
        {
            if (currentTool != value) { Editor.ToolChanged(value); AbortDrag(); }
            currentTool = value; InvalidateVisual(); Mouse.UpdateCursor();
        }
    }
    public RgbaColor CurrentColor { get; set; }
    public StrokeWidth CurrentStrokeWidth { get; set; }
    public bool IsDragging => gesture is not null || Editor.IsDragging;
    public CanvasTransform Transform => new(Document.ImagePixelSize, Document.PixelsPerDip, new(ActualWidth, ActualHeight), VisualTreeHelper.GetDpi(this).DpiScaleX);
    public AnnotationCanvas(AnnotationDocument document, BitmapSource image)
    {
        Document = document; Editor = new(document); this.image = image;
        Focusable = true; FocusVisualStyle = null; ClipToBounds = true;
        Document.Changed += DocumentChanged;
        SizeChanged += CanvasSizeChanged;
    }
    private void DocumentChanged(object? sender, EventArgs args) { InvalidateVisual(); Mouse.UpdateCursor(); }
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
        ShapeRenderer.DrawImage(dc, image);
        ShapeRenderer.Draw(dc, Document.Shapes.Select(shape => Editor.DragPreview is { } drag && drag.Id == shape.Id ? drag : shape), Document.PixelsPerDip);
        if (preview is not null) ShapeRenderer.Draw(dc, preview, Document.PixelsPerDip);
        dc.Pop(); dc.Pop();
        DrawSelection(dc, t);
    }
    private void DrawSelection(DrawingContext dc, CanvasTransform transform)
    {
        if (CurrentTool != AnnotationTool.Select || Document.SelectedShape is not { } selected) return;
        var shape = Editor.DragPreview ?? selected;
        var accent = SystemParameters.WindowGlassBrush ?? SystemColors.HighlightBrush;
        var pen = new Pen(accent, 1);
        if (shape.Kind is ShapeKind.Text)
        {
            var bounds = transform.ViewRect(shape.Bounds(Document.PixelsPerDip));
            var outline = new Pen(accent, 1) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawRectangle(null, outline, ShapeRenderer.Rect(bounds));
        }
        foreach (var (_, point) in shape.Handles)
        {
            var center = transform.ViewPoint(point);
            // WPF snaps these geometry-space stroke edges through the current device transform.
            var rect = new Rect(center.X - 4, center.Y - 4, 8, 8);
            var guides = new GuidelineSet();
            guides.GuidelinesX.Add(rect.Left - .5); guides.GuidelinesX.Add(rect.Right + .5);
            guides.GuidelinesY.Add(rect.Top - .5); guides.GuidelinesY.Add(rect.Bottom + .5);
            dc.PushGuidelineSet(guides);
            if (shape.Kind is ShapeKind.Arrow) dc.DrawEllipse(Brushes.White, pen, new(center.X, center.Y), 4, 4);
            else dc.DrawRectangle(Brushes.White, pen, rect);
            dc.Pop();
        }
    }
    private PointD Position(MouseEventArgs e) { var p = e.GetPosition(this); return new(p.X, p.Y); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus(); AbortDrag();
        if (CurrentTool == AnnotationTool.Select)
        {
            if (Transform.ImagePoint(Position(e)) is { } point && Editor.PointerPressed(point, Transform) && !CaptureMouse()) Editor.AbortDrag();
            InvalidateVisual(); Mouse.UpdateCursor(); e.Handled = true; return;
        }
        gesture = AnnotationDrawingGesture.Begin(CurrentTool, Position(e), Transform, CurrentColor, CurrentStrokeWidth);
        if (gesture is null) return;
        if (!CaptureMouse()) { AbortDrag(); return; }
        RefreshPreview(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Editor.IsDragging && IsMouseCaptured && Transform.ImagePoint(Position(e)) is { } point)
        { Editor.PointerMoved(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); InvalidateVisual(); e.Handled = true; }
        else if (gesture is not null && IsMouseCaptured) { gesture.Update(Position(e)); RefreshPreview(); e.Handled = true; }
        Mouse.UpdateCursor();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Editor.IsDragging)
        {
            if (Transform.ImagePoint(Position(e)) is { } point) Editor.PointerReleased(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            else Editor.AbortDrag();
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual(); Mouse.UpdateCursor(); e.Handled = true; return;
        }
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
        var t = Transform; var position = Position(e);
        var hit = CurrentTool == AnnotationTool.Select && t.ImagePoint(position) is { } point
            ? CanvasHitTester.HitTest(point, Document, t) : new CanvasHit.None();
        e.Cursor = CanvasCursor.For(CurrentTool, t.IsInsideImage(position), hit, IsDragging, Editor.DragTarget) switch
        {
            CanvasCursorKind.Crosshair => Cursors.Cross, CanvasCursorKind.Move => Cursors.SizeAll,
            CanvasCursorKind.ResizeNwse => Cursors.SizeNWSE, CanvasCursorKind.ResizeNesw => Cursors.SizeNESW,
            CanvasCursorKind.ResizeEndpoint => Cursors.Hand, _ => Cursors.Arrow
        };
        e.Handled = true;
    }
    public bool Execute(CanvasKeyCommand command)
    {
        if (command == CanvasKeyCommand.Deselect) AbortDrag();
        if (gesture is not null) return true;
        var handled = Editor.Execute(command, CurrentTool);
        InvalidateVisual(); Mouse.UpdateCursor(); return handled;
    }
    public void RefreshPreview()
    {
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        Editor.ShiftChanged(shift); preview = gesture?.Shape(shift, completing: false); InvalidateVisual();
    }
    public void AbortDrag()
    {
        gesture = null; preview = null; Editor.AbortDrag();
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual(); Mouse.UpdateCursor();
    }
    public void Dispose()
    {
        AbortDrag(); Document.Changed -= DocumentChanged; SizeChanged -= CanvasSizeChanged; image = null;
    }
}
