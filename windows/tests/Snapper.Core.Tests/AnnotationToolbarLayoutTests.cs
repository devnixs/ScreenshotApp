using Snapper.Core.Annotate;
namespace Snapper.Core.Tests;
public sealed class AnnotationToolbarLayoutTests
{
    [Fact] public void ToolsFollowProductOrderAndLabels()
    {
        Assert.Equal(new[] { AnnotationTool.Select, AnnotationTool.Rectangle, AnnotationTool.Arrow, AnnotationTool.Text }, AnnotationToolbarLayout.Tools.Select(t => t.Tool));
        Assert.Equal(new[] { "Select (V)", "Rectangle (R)", "Arrow (A)", "Text (T)" }, AnnotationToolbarLayout.Tools.Select(t => t.ToolTip));
        Assert.Equal(new[] { "Select", "Rectangle", "Arrow", "Text" }, AnnotationToolbarLayout.Tools.Select(t => t.Label));
    }
    [Fact] public void GlyphsMatchMicrosoftReference()
    {
        Assert.Equal(new[] { "\uE7C9", "\uE739", "\uE72A", "\uE8D2" }, AnnotationToolbarLayout.Tools.Select(t => t.Glyph));
        foreach (var d in AnnotationToolbarLayout.Tools) { Assert.Single(d.Glyph); Assert.InRange(d.Glyph[0], '\uE000', '\uF8FF'); }
        Assert.Equal(-45, AnnotationToolbarLayout.Tools.Single(t => t.Tool == AnnotationTool.Arrow).GlyphRotation);
        Assert.Equal("\uE710", AnnotationToolbarLayout.CustomColorGlyph); Assert.Equal("Custom color…", AnnotationToolbarLayout.CustomColorToolTip);
    }
    [Fact] public void SwatchesWidthsAndActionsMatchThisSlice()
    {
        Assert.Equal(new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White" }, AnnotationToolbarLayout.SwatchToolTips);
        Assert.Equal(new[] { "S", "M", "L" }, AnnotationToolbarLayout.StrokeLabels); Assert.Equal(new[] { "Small", "Medium", "Large" }, AnnotationToolbarLayout.StrokeToolTips);
        Assert.Empty(AnnotationToolbarLayout.TrailingActions);
    }
}
