using Snapper.Core.Annotate;
namespace Snapper.Core.Tests;
public sealed class AnnotationToolTests
{
    [Theory] [InlineData("v", AnnotationTool.Select)] [InlineData("r", AnnotationTool.Rectangle)] [InlineData("a", AnnotationTool.Arrow)] [InlineData("t", AnnotationTool.Text)]
    public void RoutingAndSegments(string key, AnnotationTool tool)
    {
        foreach (var characters in new[] { key, key.ToUpperInvariant() })
        { Assert.Equal(tool, AnnotationTools.ForShortcut(characters, false, true)); Assert.Null(AnnotationTools.ForShortcut(characters, true, true)); }
        Assert.Equal(tool, AnnotationTools.FromSegmentIndex(tool.SegmentIndex()));
        Assert.Equal(tool is AnnotationTool.Rectangle or AnnotationTool.Arrow, tool.CreatesShapesByDragging());
    }
    [Theory] [InlineData("t")] [InlineData("x")] [InlineData("")] [InlineData("rr")]
    public void DisabledOrUnknownShortcuts(string key) => Assert.Null(AnnotationTools.ForShortcut(key, false, false));
    [Fact] public void DefaultsAndInvalidSegments()
    { Assert.Equal(AnnotationTool.Rectangle, AnnotationTools.DefaultTool); Assert.Null(AnnotationTools.FromSegmentIndex(-1)); Assert.Null(AnnotationTools.FromSegmentIndex(4)); }
}
