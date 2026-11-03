namespace Snapper.Core.Annotate;
public enum AnnotationTool { Select, Rectangle, Arrow, Text }
public static class AnnotationTools
{
    public const AnnotationTool DefaultTool = AnnotationTool.Rectangle;
    public static AnnotationTool? ForShortcut(string characters, bool hasCtrlAltOrWin, bool textToolEnabled) => hasCtrlAltOrWin ? null : characters.ToLowerInvariant() switch
    { "v" => AnnotationTool.Select, "r" => AnnotationTool.Rectangle, "a" => AnnotationTool.Arrow, "t" when textToolEnabled => AnnotationTool.Text, _ => null };
    public static int SegmentIndex(this AnnotationTool tool) => (int)tool;
    public static AnnotationTool? FromSegmentIndex(int index) => index is >= 0 and < 4 ? (AnnotationTool)index : null;
    public static bool CreatesShapesByDragging(this AnnotationTool tool) => tool is AnnotationTool.Rectangle or AnnotationTool.Arrow;
}
