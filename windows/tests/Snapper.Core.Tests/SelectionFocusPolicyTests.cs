using Snapper.Core.App;
using Snapper.Core.Capture;
namespace Snapper.Core.Tests;
public sealed class SelectionFocusPolicyTests
{
    [Fact] public void CancelledAlwaysRestores()
    {foreach(var p in Enum.GetValues<CapturePurpose>())Assert.True(SelectionFocusPolicy.RestoresPreviousWindow(p,SelectionOutcome.Cancelled));}
    [Theory] [InlineData(CapturePurpose.Screenshot,true)] [InlineData(CapturePurpose.Record,true)] [InlineData(CapturePurpose.Annotate,false)]
    public void SelectedFocusPolicy(CapturePurpose purpose,bool expected)=>Assert.Equal(expected,SelectionFocusPolicy.RestoresPreviousWindow(purpose,SelectionOutcome.Selected));
    [Theory] [InlineData("Chrome_WidgetWin_1")] [InlineData("HwndWrapper[Snapper;;…]")] [InlineData("shell_traywnd")]
    public void ShouldRememberNormalVisibleWindow(string className)=>Assert.True(SelectionFocusPolicy.ShouldRemember(true,true,className));
    [Theory] [InlineData("Shell_TrayWnd")] [InlineData("Shell_SecondaryTrayWnd")] [InlineData("Progman")] [InlineData("WorkerW")]
    public void ShouldNotRememberShell(string name)=>Assert.False(SelectionFocusPolicy.ShouldRemember(true,true,name));
    [Theory] [InlineData(false,true)] [InlineData(true,false)] [InlineData(false,false)]
    public void ShouldNotRememberHiddenOrDeadWindows(bool alive,bool visible)=>Assert.False(SelectionFocusPolicy.ShouldRemember(alive,visible,"Window"));
}
