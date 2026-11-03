using Snapper.Core.Annotate;

namespace Snapper.Core.Tests;

public sealed class CopyAndCloseDecisionTests
{
    [Theory]
    [InlineData(false, false, CopyAndCloseStart.Copy)]
    [InlineData(true, false, CopyAndCloseStart.Refuse)]
    [InlineData(false, true, CopyAndCloseStart.Refuse)]
    [InlineData(true, true, CopyAndCloseStart.Refuse)]
    public void StartRefusesDuringDragOrDialog(bool dragging, bool dialog, CopyAndCloseStart expected) =>
        Assert.Equal(expected, CopyAndCloseDecision.Start(dragging, dialog));

    [Theory]
    [InlineData(true, CopyAndCloseFinish.Close)]
    [InlineData(false, CopyAndCloseFinish.StayOpen)]
    public void OnlySuccessfulCopyCloses(bool copied, CopyAndCloseFinish expected) =>
        Assert.Equal(expected, CopyAndCloseDecision.Finish(copied));

    [Fact]
    public void MainAndNumpadEnterUseReturnVirtualKey()
    {
        Assert.Equal(0x0D, CopyAndCloseDecision.VirtualKeyReturn);
        Assert.True(CopyAndCloseDecision.IsShortcut(0x0D, true, false, false, false, false));
    }

    [Theory]
    [InlineData(0x0D, false, false, false, false, false)]
    [InlineData(0x0D, true, true, false, false, false)]
    [InlineData(0x0D, true, false, true, false, false)]
    [InlineData(0x0D, true, false, false, true, false)]
    [InlineData(0x0D, true, false, false, false, true)]
    [InlineData(0x43, true, false, false, false, false)]
    [InlineData(0x53, true, false, false, false, false)]
    [InlineData(0x20, true, false, false, false, false)]
    [InlineData(0xE5, true, false, false, false, false)]
    public void ShortcutRequiresExactModifiersReturnAndInitialPress(int key, bool ctrl, bool shift, bool alt, bool win, bool repeat) =>
        Assert.False(CopyAndCloseDecision.IsShortcut(key, ctrl, shift, alt, win, repeat));
}
