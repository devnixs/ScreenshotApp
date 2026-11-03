using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Annotate;
public sealed record AnnotationToolDescriptor(AnnotationTool Tool, string Label, string ToolTip, string Glyph, double GlyphRotation = 0);
public sealed record AnnotationActionDescriptor(string Label, string ToolTip, string Glyph);
public static class AnnotationToolbarLayout
{
    // Verified against Microsoft's Segoe Fluent Icons reference: TouchPointer, Checkbox, Forward, Font, Add.
    public static IReadOnlyList<AnnotationToolDescriptor> Tools { get; } = Array.AsReadOnly<AnnotationToolDescriptor>([
        new(AnnotationTool.Select, "Select", "Select (V)", "\uE7C9"), new(AnnotationTool.Rectangle, "Rectangle", "Rectangle (R)", "\uE739"),
        new(AnnotationTool.Arrow, "Arrow", "Arrow (A)", "\uE72A", -45), new(AnnotationTool.Text, "Text", "Text (T)", "\uE8D2")]);
    public static IReadOnlyList<string> SwatchToolTips { get; } = Array.AsReadOnly(Enum.GetValues<ColorPreset>().Select(p => p.DisplayName()).ToArray());
    public const string CustomColorToolTip = "Custom color…", CustomColorGlyph = "\uE710";
    public static IReadOnlyList<string> StrokeLabels { get; } = Array.AsReadOnly(new[] { "S", "M", "L" });
    public static IReadOnlyList<string> StrokeToolTips { get; } = Array.AsReadOnly(new[] { "Small", "Medium", "Large" });
    public static IReadOnlyList<AnnotationActionDescriptor> TrailingActions { get; } = Array.Empty<AnnotationActionDescriptor>();
}
