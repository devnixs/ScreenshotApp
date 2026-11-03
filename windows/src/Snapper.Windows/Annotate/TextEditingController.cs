using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Windows.Annotate;

/// <summary>One draft and one native undo stack per session. The document changes only at commit.</summary>
internal sealed class TextEditingController(AnnotationCanvas canvas, Canvas layer) : IDisposable
{
    public WpfTextMeasurer Measurer { get; } = new();
    private TextEditDraft? draft;
    private AnnotationTextEditor? editor;
    private bool startedWithSelect;
    private Guid draftId;
    private PointD inset;
    public event Action? Changed;
    public bool IsEditing => draft is not null;
    public Guid? EditingShapeId => draft?.Original?.Id;
    public ShapeStyle? Style => draft is { } d ? new(d.Color, d.StrokeWidth) : null;
    public AnnotationShape? DraftShape => draft is { } d ? new(draftId, new ShapeKind.Text(new(d.Origin, d.Text, d.MeasuredSize)), d.Color, d.StrokeWidth) : null;
    public void BeginNew(PointD imagePoint)
    {
        var size = TextMetrics.FontPixelSize(canvas.CurrentStrokeWidth, canvas.Document.PixelsPerDip);
        var image = canvas.Document.ImagePixelSize;
        var origin = TextMetrics.NewBoxOrigin(imagePoint, Measurer.LineHeight(size), new(image.Width, image.Height));
        Start(new(null, "", origin, canvas.CurrentColor, canvas.CurrentStrokeWidth, Measurer.LayoutSize("", size)), null);
    }
    public void BeginEditing(Guid id, PointD caretImagePoint)
    {
        if (canvas.Document.ShapeWithId(id) is not { Kind: ShapeKind.Text(var box) } shape) return;
        Start(new(shape, box.Text, box.Origin, shape.Color, shape.StrokeWidth, box.MeasuredSize), caretImagePoint);
    }
    private void Start(TextEditDraft value, PointD? caretImagePoint)
    {
        if (IsEditing) return;
        canvas.AbortDrag(); draft = value; draftId = value.Original?.Id ?? Guid.NewGuid();
        startedWithSelect = canvas.CurrentTool == AnnotationTool.Select;
        editor = new AnnotationTextEditor { Text = EditorNewlines.ToEditor(value.Text) };
        TextStyle.ApplyTo(editor, FontSize, value.Color);
        editor.TextChanged += TextChanged;
        layer.Children.Add(editor);
        Layout(canvas.Transform); PositionEditor(); RestoreFocus();
        if (caretImagePoint is { } point)
        {
            var local = TextEditorLayout.EditorLocalPoint(point, value.Origin, inset);
            var hit = editor.GetCharacterIndexFromPoint(new(local.X, local.Y), true);
            var first = hit >= 0 && hit < editor.Text.Length ? editor.GetRectFromCharacterIndex(hit) : Rect.Empty;
            var last = hit >= 0 && hit < editor.Text.Length ? editor.GetRectFromCharacterIndex(hit, true) : Rect.Empty;
            editor.CaretIndex = CaretPlacement.InsertionIndex(editor.Text, hit, local.X, first.IsEmpty ? 0 : first.X, last.IsEmpty ? 0 : last.X);
        }
        else editor.CaretIndex = 0;
        Notify();
    }
    private double FontSize => draft is { } d ? TextMetrics.FontPixelSize(d.StrokeWidth, canvas.Document.PixelsPerDip) : 0;
    private void TextChanged(object sender, TextChangedEventArgs args)
    {
        if (draft is not { } d || editor is null) return;
        draft = d with { Text = EditorNewlines.ToModel(editor.Text) };
        Remeasure(); PositionEditor(); Notify();
    }
    private void Remeasure()
    { if (draft is { } d) draft = d with { MeasuredSize = Measurer.LayoutSize(d.Text, FontSize) }; }
    public void ApplyColor(RgbaColor color)
    { if (draft is { } d) { draft = d with { Color = color }; ApplyStyle(); } }
    public void ApplyStrokeWidth(StrokeWidth width)
    { if (draft is { } d) { draft = d with { StrokeWidth = width }; Remeasure(); ApplyStyle(); } }
    private void ApplyStyle()
    {
        if (draft is not { } d || editor is null) return;
        TextStyle.ApplyTo(editor, FontSize, d.Color); PositionEditor(); RestoreFocus(); Notify();
    }
    public void Layout(CanvasTransform transform)
    {
        var (scale, x, y) = TextEditorLayout.LayerTransform(transform);
        layer.RenderTransform = new MatrixTransform(scale, 0, 0, scale, x, y);
        layer.Clip = new RectangleGeometry(new Rect(0, 0, canvas.Document.ImagePixelSize.Width, canvas.Document.ImagePixelSize.Height));
        if (editor is not null) PositionEditor();
    }
    private void PositionEditor()
    {
        if (draft is not { } d || editor is null) return;
        editor.UpdateLayout();
        var caret = editor.GetRectFromCharacterIndex(0);
        inset = caret.IsEmpty ? new(0, 0) : new(caret.X, caret.Y);
        var position = TextEditorLayout.TextBoxPosition(d.Origin, inset);
        Canvas.SetLeft(editor, position.X); Canvas.SetTop(editor, position.Y);
        layer.UpdateLayout();
    }
    public void RestoreFocus() { if (editor is not null) { editor.Focus(); Keyboard.Focus(editor); } }
    public void Commit()
    {
        if (draft is not { } d) return;
        draft = d with { Text = EditorNewlines.ToModel(editor?.Text ?? d.Text) };
        Remeasure();
        var final = draft ?? d;
        draft = null;
        if (editor is not null) { editor.TextChanged -= TextChanged; layer.Children.Remove(editor); editor = null; }
        var decision = TextCommitDecision.Decide(final);
        canvas.Document.Apply(decision);
        Guid? selected = decision switch
        {
            TextCommitDecision.Add(var added) => added.Id,
            TextCommitDecision.Update(var updated) => updated.Id,
            TextCommitDecision.NoChange => final.Original?.Id,
            _ => null
        };
        canvas.Document.Select(startedWithSelect ? selected : null);
        canvas.Focus(); Keyboard.Focus(canvas); Notify();
    }
    private void Notify() { canvas.InvalidateVisual(); Changed?.Invoke(); }
    public void Dispose()
    {
        if (editor is not null) editor.TextChanged -= TextChanged;
        layer.Children.Clear(); editor = null; draft = null; Changed = null;
    }
}
