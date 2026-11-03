using Snapper.Core.Annotate;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class CanvasKeyCommandTests
{
    [Theory] [InlineData(0x2E)] [InlineData(0x08)] [InlineData(0x1B)]
    public void PlainAndShiftCommands(int key)
    {
        var expected = key == 0x1B ? CanvasKeyCommand.Deselect : CanvasKeyCommand.DeleteSelection;
        Assert.Equal(expected, CanvasKeyMap.Map(key, KeyModifiers.None));
        Assert.Equal(expected, CanvasKeyMap.Map(key, KeyModifiers.Shift));
        foreach (var modifier in new[] { KeyModifiers.Ctrl, KeyModifiers.Alt, KeyModifiers.Win })
        { Assert.Null(CanvasKeyMap.Map(key, modifier)); Assert.Null(CanvasKeyMap.Map(key, modifier | KeyModifiers.Shift)); }
    }
    [Fact] public void WindowsUndoRedoBindingsAreExact()
    {
        Assert.Equal(CanvasKeyCommand.Undo, CanvasKeyMap.Map(0x5A, KeyModifiers.Ctrl));
        Assert.Equal(CanvasKeyCommand.Redo, CanvasKeyMap.Map(0x5A, KeyModifiers.Ctrl | KeyModifiers.Shift));
        Assert.Equal(CanvasKeyCommand.Redo, CanvasKeyMap.Map(0x59, KeyModifiers.Ctrl));
        foreach (var modifiers in new[] { KeyModifiers.None, KeyModifiers.Alt, KeyModifiers.Win, KeyModifiers.Ctrl | KeyModifiers.Alt, KeyModifiers.Ctrl | KeyModifiers.Win })
            Assert.Null(CanvasKeyMap.Map(0x5A, modifiers));
        Assert.Null(CanvasKeyMap.Map(0x59, KeyModifiers.Ctrl | KeyModifiers.Shift));
        Assert.Null(CanvasKeyMap.Map(0x56, KeyModifiers.None));
    }
}
