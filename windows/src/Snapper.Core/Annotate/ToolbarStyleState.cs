using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Annotate;
public readonly record struct ShapeStyle(RgbaColor Color, StrokeWidth StrokeWidth);
public abstract record SwatchSelection
{
    public sealed record Preset(int Index) : SwatchSelection;
    public sealed record Custom : SwatchSelection;
}
public abstract record ToolbarStyleEvent
{
    public sealed record SelectedShapeStyleChanged(ShapeStyle? Style) : ToolbarStyleEvent;
    public sealed record ColorPicked(RgbaColor Color) : ToolbarStyleEvent;
    public sealed record StrokeWidthPicked(StrokeWidth Width) : ToolbarStyleEvent;
}
public readonly record struct ToolbarStyleEffects(RgbaColor? ApplyColorToSelection = null, StrokeWidth? ApplyStrokeWidthToSelection = null,
    RgbaColor? PersistColor = null, StrokeWidth? PersistStrokeWidth = null);
public record struct ToolbarStyleState(ShapeStyle CurrentStyle)
{
    public ShapeStyle? SelectedShapeStyle { get; private set; }
    public readonly ShapeStyle DisplayedStyle => SelectedShapeStyle ?? CurrentStyle;
    public readonly SwatchSelection SwatchSelection
    {
        get
        {
            for (var i = 0; i < RgbaColor.Presets.Count; i++) if (RgbaColor.Presets[i] == DisplayedStyle.Color) return new SwatchSelection.Preset(i);
            return new SwatchSelection.Custom();
        }
    }
    public ToolbarStyleEffects Reduce(ToolbarStyleEvent e)
    {
        switch (e)
        {
            case ToolbarStyleEvent.SelectedShapeStyleChanged(var style): SelectedShapeStyle = style; return new();
            case ToolbarStyleEvent.ColorPicked(var color):
                CurrentStyle = CurrentStyle with { Color = color };
                var changeColor = SelectedShapeStyle is { } selected && selected.Color != color;
                if (changeColor && SelectedShapeStyle is { } s) SelectedShapeStyle = s with { Color = color };
                return new(ApplyColorToSelection: changeColor ? color : null, PersistColor: color);
            case ToolbarStyleEvent.StrokeWidthPicked(var width):
                CurrentStyle = CurrentStyle with { StrokeWidth = width };
                var changeWidth = SelectedShapeStyle is { } chosen && chosen.StrokeWidth != width;
                if (changeWidth && SelectedShapeStyle is { } w) SelectedShapeStyle = w with { StrokeWidth = width };
                return new(ApplyStrokeWidthToSelection: changeWidth ? width : null, PersistStrokeWidth: width);
            default: throw new ArgumentOutOfRangeException(nameof(e));
        }
    }
}
public static class AnnotationShapeStyle
{
    public static ShapeStyle Style(this AnnotationShape shape) => new(shape.Color, shape.StrokeWidth);
}
