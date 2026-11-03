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
    [Fact] public void SwatchesAndWidthsMatchProduct()
    {
        Assert.Equal(new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White" }, AnnotationToolbarLayout.SwatchToolTips);
        Assert.Equal(new[] { "S", "M", "L" }, AnnotationToolbarLayout.StrokeLabels); Assert.Equal(new[] { "Small", "Medium", "Large" }, AnnotationToolbarLayout.StrokeToolTips);
    }
    [Fact] public void TrailingActionsUseProductOrderAndDescriptors()
    {
        Assert.Equal(new[] { AnnotationToolbarAction.Save, AnnotationToolbarAction.Copy, AnnotationToolbarAction.CopyAndClose },
            AnnotationToolbarLayout.TrailingActions.Select(a => a.Action));
        Assert.Equal(AnnotationToolbarAction.CopyAndClose, AnnotationToolbarLayout.TrailingActions[^1].Action);
        Assert.Equal(new[] { "Save", "Copy", "Copy & Close" }, AnnotationToolbarLayout.TrailingActions.Select(a => a.Label));
        Assert.Equal(new[] { "Save (Ctrl+S)", "Copy (Ctrl+C)", "Copy & Close (Ctrl+Enter)" }, AnnotationToolbarLayout.TrailingActions.Select(a => a.ToolTip));
        Assert.Equal(new[] { "\uE74E", "\uE8C8", "\uE73E" }, AnnotationToolbarLayout.TrailingActions.Select(a => a.Glyph));
        Assert.Equal("Copy & Close", AnnotationToolbarLayout.CopyAndCloseLabel);
        Assert.Equal("Copy & Close (Ctrl+Enter)", AnnotationToolbarLayout.CopyAndCloseToolTip);
        Assert.Equal("\uE73E", AnnotationToolbarLayout.CopyAndCloseGlyph);
    }
}
