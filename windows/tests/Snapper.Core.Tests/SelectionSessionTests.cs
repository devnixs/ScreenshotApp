using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using static Snapper.Core.Tests.SelectionFixtures;
namespace Snapper.Core.Tests;
public sealed class SelectionSessionTests
{
    [Fact] public void ClicksAndBelowThresholdDragsStayOpen()
    {
        var s = new SelectionSession();Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(100,100)));
        foreach(var end in new PointD[]{new(100,100),new(103,200),new(200,103)})
        {s.MouseDown(new(100,100),[A]);Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(end));Assert.False(s.IsMouseDown);}
    }
    [Fact] public void FullDragReturnsRectangle()
    {
        var s = new SelectionSession();s.MouseDown(new(100,100),[A]);Assert.True(s.IsMouseDown);s.MouseDragged(new(300,200));
        Assert.Equal(new SelectionSessionOutcome.Rectangle(new RectSelection(A,new(100,100,200,100))),s.MouseUp(new(300,200)));
        Assert.False(s.IsMouseDown);Assert.Null(s.Tracker.CurrentRect);
    }
    [Fact] public void NegativeOriginMonitorClamps()
    {
        var s = new SelectionSession();s.MouseDown(new(-100,100),[A,C]);
        Assert.Equal(new SelectionSessionOutcome.Rectangle(new RectSelection(C,new(-100,-500,100,600))),s.MouseUp(new(100,-800)));
    }
    [Fact] public void ResetClearsPress()
    {
        var s = new SelectionSession();s.MouseDown(new(100,100),[A]);s.Reset();Assert.False(s.IsMouseDown);Assert.Null(s.Tracker.CurrentRect);
        Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(400,400)));
    }

    [Fact] public void StartsInRectangleModeAndTogglesBothWays()
    {
        var s=new SelectionSession();Assert.Equal(SelectionMode.Rectangle,s.Mode);
        Assert.False(s.IsMouseDown || s.IsPressStale || s.IsWindowClickArmed);
        s.ToggleMode();Assert.Equal(SelectionMode.Window,s.Mode);s.ToggleMode();Assert.Equal(SelectionMode.Rectangle,s.Mode);
    }
    [Fact] public void ToggleDuringDragDiscardsDragAndStaleMotion()
    {
        var s=new SelectionSession();s.MouseDown(new(100,100),[A]);s.MouseDragged(new(300,200));Assert.NotNull(s.Tracker.CurrentRect);
        s.ToggleMode();Assert.Null(s.Tracker.CurrentRect);Assert.True(s.IsPressStale);
        s.ToggleMode();s.MouseDragged(new(300,200));Assert.Null(s.Tracker.CurrentRect);
        Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(300,200)));Assert.False(s.IsPressStale || s.IsMouseDown);
        s.MouseDown(new(100,100),[A]);s.MouseDragged(new(300,200));
        Assert.Equal(new SelectionSessionOutcome.Rectangle(new RectSelection(A,new(100,100,200,100))),s.MouseUp(new(300,200)));
    }
    [Fact] public void StalePressDragIsIgnoredAfterToggleBack()
    {
        var s=new SelectionSession();s.MouseDown(new(100,100),[A]);s.ToggleMode();s.ToggleMode();s.MouseDragged(new(300,200));
        Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(300,200)));
    }
    [Fact] public void WindowClickReturnsReleasePointAndDisarms()
    {
        var s=new SelectionSession();s.ToggleMode();Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(100,100)));
        s.MouseDown(new(100,100),[A]);s.MouseDragged(new(300,200));Assert.Null(s.Tracker.CurrentRect);
        Assert.Equal(new SelectionSessionOutcome.WindowClick(new(300,200)),s.MouseUp(new(300,200)));
        Assert.False(s.IsWindowClickArmed);Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(300,200)));
    }
    [Fact] public void RectanglePressReleasedInWindowModeDoesNotClick()
    {
        var s=new SelectionSession();s.MouseDown(new(100,100),[A]);s.ToggleMode();
        Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(300,200)));s.MouseDown(new(300,200),[A]);
        Assert.Equal(new SelectionSessionOutcome.WindowClick(new(300,200)),s.MouseUp(new(300,200)));
    }
    [Fact] public void WindowPressReleasedInRectangleModeDoesNothing()
    {
        var s=new SelectionSession();s.ToggleMode();s.MouseDown(new(100,100),[A]);s.ToggleMode();
        Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(300,200)));
    }
    [Fact] public void RectangleStillClampsToStartingNegativeOriginDisplayAfterToggles()
    {
        var left=WindowSelectionTests.C;
        var s=new SelectionSession();s.ToggleMode();s.ToggleMode();s.MouseDown(new(-100,100),[A,left]);
        Assert.Equal(new SelectionSessionOutcome.Rectangle(new RectSelection(left,new(-100,100,100,200))),s.MouseUp(new(100,300)));
    }
    [Fact] public void CaptureLossPreservesModeButDisarmsWindowPress()
    {
        var s=new SelectionSession();s.ToggleMode();s.MouseDown(new(100,100),[A]);s.Reset();
        Assert.Equal(SelectionMode.Window,s.Mode);Assert.False(s.IsMouseDown || s.IsPressStale || s.IsWindowClickArmed);
        Assert.Equal(new SelectionSessionOutcome.None(),s.MouseUp(new(300,200)));
    }
}
