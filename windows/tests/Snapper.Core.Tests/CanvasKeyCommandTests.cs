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
    [Theory]
    [InlineData(0x53, false, false, CanvasKeyCommand.Save)] [InlineData(0x53, true, false, CanvasKeyCommand.Save)]
    [InlineData(0x53, false, true, CanvasKeyCommand.Refuse)] [InlineData(0x53, true, true, CanvasKeyCommand.Refuse)]
    [InlineData(0x43, false, false, CanvasKeyCommand.CopyImage)] [InlineData(0x43, false, true, CanvasKeyCommand.Refuse)]
    [InlineData(0x57, false, false, CanvasKeyCommand.Close)] [InlineData(0x57, true, false, CanvasKeyCommand.Close)]
    [InlineData(0x57, false, true, CanvasKeyCommand.Close)]
    public void ExactExportCommands(int key, bool editing, bool dragging, CanvasKeyCommand expected) =>
        Assert.Equal(expected, CanvasKeyMap.ExportCommand(KeyChord.Create(KeyModifiers.Ctrl, key), editing, dragging));
    [Theory] [InlineData(false)] [InlineData(true)]
    public void TextCopyNeverFallsBackToImage(bool dragging) =>
        Assert.Null(CanvasKeyMap.ExportCommand(KeyChord.Create(KeyModifiers.Ctrl, 0x43), true, dragging));
    [Theory]
    [InlineData(0x53, KeyModifiers.Ctrl | KeyModifiers.Shift)] [InlineData(0x43, KeyModifiers.Ctrl | KeyModifiers.Shift)]
    [InlineData(0x57, KeyModifiers.Ctrl | KeyModifiers.Shift)] [InlineData(0x43, KeyModifiers.Ctrl | KeyModifiers.Alt)]
    [InlineData(0x53, KeyModifiers.Alt)] [InlineData(0x53, KeyModifiers.Win)]
    [InlineData(0x53, KeyModifiers.None)] [InlineData(0x43, KeyModifiers.None)] [InlineData(0x57, KeyModifiers.None)]
    [InlineData(0x0D, KeyModifiers.Ctrl)]
    public void OtherChordsDoNotExport(int key, KeyModifiers modifiers) =>
        Assert.Null(CanvasKeyMap.ExportCommand(KeyChord.Create(modifiers, key), false, false));
}
