using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class ToolbarStyleStateTests
{
    private static ShapeStyle Initial => new(RgbaColor.Red, StrokeWidth.M);
    [Fact] public void SelectionOnlyChangesDisplayedStyle()
    {
        var state = new ToolbarStyleState(Initial); var selected = new ShapeStyle(RgbaColor.Blue, StrokeWidth.L);
        Assert.Equal(new ToolbarStyleEffects(), state.Reduce(new ToolbarStyleEvent.SelectedShapeStyleChanged(selected)));
        Assert.Equal(selected, state.DisplayedStyle); Assert.Equal(Initial, state.CurrentStyle);
        state.Reduce(new ToolbarStyleEvent.SelectedShapeStyleChanged(null)); Assert.Equal(Initial, state.DisplayedStyle);
    }
    [Theory] [InlineData(false)] [InlineData(true)] public void ExplicitPicksPersistAndApplyOnlyChanges(bool selected)
    {
        var state = new ToolbarStyleState(Initial);
        if (selected) state.Reduce(new ToolbarStyleEvent.SelectedShapeStyleChanged(new(RgbaColor.Blue, StrokeWidth.L)));
        var effect = state.Reduce(new ToolbarStyleEvent.ColorPicked(RgbaColor.Green));
        Assert.Equal(RgbaColor.Green, effect.PersistColor); Assert.Null(effect.PersistStrokeWidth);
        Assert.Equal(selected ? RgbaColor.Green : (RgbaColor?)null, effect.ApplyColorToSelection);
        Assert.Equal(new(RgbaColor.Green, StrokeWidth.M), state.CurrentStyle);
        Assert.Null(state.Reduce(new ToolbarStyleEvent.ColorPicked(RgbaColor.Green)).ApplyColorToSelection);
        effect = state.Reduce(new ToolbarStyleEvent.StrokeWidthPicked(StrokeWidth.S));
        Assert.Equal(StrokeWidth.S, effect.PersistStrokeWidth); Assert.Null(effect.PersistColor);
        Assert.Equal(selected ? StrokeWidth.S : (StrokeWidth?)null, effect.ApplyStrokeWidthToSelection);
        Assert.Null(state.Reduce(new ToolbarStyleEvent.StrokeWidthPicked(StrokeWidth.S)).ApplyStrokeWidthToSelection);
        Assert.Equal(new(RgbaColor.Green, StrokeWidth.S), state.CurrentStyle);
    }
    [Fact] public void SwatchesAndIndependentWindows()
    {
        var state = new ToolbarStyleState(Initial); var other = state;
        for (var i = 0; i < RgbaColor.Presets.Count; i++) { state.Reduce(new ToolbarStyleEvent.ColorPicked(RgbaColor.Presets[i])); Assert.Equal(new SwatchSelection.Preset(i), state.SwatchSelection); }
        state.Reduce(new ToolbarStyleEvent.ColorPicked(new(.5, .5, .5))); Assert.IsType<SwatchSelection.Custom>(state.SwatchSelection); Assert.Equal(Initial, other.CurrentStyle);
    }
    [Fact] public void UndoRestylesSelectionWithoutRevertingPicks()
    {
        var state = new ToolbarStyleState(Initial); state.Reduce(new ToolbarStyleEvent.SelectedShapeStyleChanged(Initial));
        state.Reduce(new ToolbarStyleEvent.ColorPicked(RgbaColor.Blue)); state.Reduce(new ToolbarStyleEvent.SelectedShapeStyleChanged(Initial));
        Assert.Equal(Initial, state.DisplayedStyle); Assert.Equal(RgbaColor.Blue, state.CurrentStyle.Color);
    }
    [Fact] public void InitialDefaultsHighlightRedAndM()
    { var state = new ToolbarStyleState(Initial); Assert.Equal(new SwatchSelection.Preset(0), state.SwatchSelection); Assert.Equal(StrokeWidth.M, state.DisplayedStyle.StrokeWidth); }
}
