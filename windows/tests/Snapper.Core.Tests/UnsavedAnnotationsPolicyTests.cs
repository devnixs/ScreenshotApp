using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;

public sealed class UnsavedAnnotationsPolicyTests
{
    [Theory] [InlineData(-1)] [InlineData(0)]
    public void NoDirtyWindowsTerminateImmediately(int count) => Assert.IsType<QuitDecision.TerminateNow>(UnsavedAnnotationsPolicy.QuitDecisionFor(count));
    [Theory] [InlineData(1, "")] [InlineData(2, "s")] [InlineData(3, "s")] [InlineData(10, "s")]
    public void DirtyWindowsUseWindowsExitMessage(int count, string plural)
    {
        var message = $"Exit and discard {count} unsaved annotation{plural}?";
        Assert.Equal(new QuitDecision.Confirm(message), UnsavedAnnotationsPolicy.QuitDecisionFor(count));
        Assert.Equal(message, UnsavedAnnotationsPolicy.QuitMessage(count));
    }
    [Fact] public void ZeroMessageIsPlural() => Assert.Equal("Exit and discard 0 unsaved annotations?", UnsavedAnnotationsPolicy.QuitMessage(0));
    [Theory] [InlineData(false, CloseDecision.CloseImmediately)] [InlineData(true, CloseDecision.ConfirmDiscard)]
    public void CloseDecisionMatchesDirtyState(bool dirty, CloseDecision expected) => Assert.Equal(expected, UnsavedAnnotationsPolicy.CloseDecisionFor(dirty));
    [Fact] public void RealModelCloseTable()
    {
        var document = new AnnotationDocument(new(200, 100), 1);
        void Expect(CloseDecision expected) => Assert.Equal(expected, UnsavedAnnotationsPolicy.CloseDecisionFor(document.IsDirty));
        Expect(CloseDecision.CloseImmediately);
        var draft = new TextEditDraft(null, "", new PointD(0, 0), RgbaColor.Red, StrokeWidth.M, new(40, 30));
        document.Apply(TextCommitDecision.Decide(draft)); Expect(CloseDecision.CloseImmediately);
        document.Apply(TextCommitDecision.Decide(draft with { Text = "Hi" })); Expect(CloseDecision.ConfirmDiscard);
        document.MarkClean(); Expect(CloseDecision.CloseImmediately);
        document.Add(new(new ShapeKind.Rectangle(new(10, 10, 30, 30)), RgbaColor.Red, StrokeWidth.M)); Expect(CloseDecision.ConfirmDiscard);
        document.MarkClean(); Expect(CloseDecision.CloseImmediately);
        document.Replace(document.Shapes[0].WithColor(RgbaColor.Blue)); Expect(CloseDecision.ConfirmDiscard);
    }
    [Theory]
    [InlineData(false, false, SessionEndDecision.Allow)] [InlineData(true, false, SessionEndDecision.Block)]
    [InlineData(false, true, SessionEndDecision.Allow)] [InlineData(true, true, SessionEndDecision.Allow)]
    public void SessionEnd(bool dirty, bool critical, SessionEndDecision expected) => Assert.Equal(expected, UnsavedAnnotationsPolicy.SessionEndDecisionFor(dirty, critical));
    [Fact] public void StringsMatchWindowsProduct()
    {
        Assert.Equal("Discard changes?", UnsavedAnnotationsPolicy.DiscardTitle);
        Assert.Equal("Discard", UnsavedAnnotationsPolicy.DiscardButton);
        Assert.Equal("Exit", UnsavedAnnotationsPolicy.QuitButton);
        Assert.Equal("Cancel", UnsavedAnnotationsPolicy.CancelButton);
        Assert.Equal("Unsaved annotations", UnsavedAnnotationsPolicy.ShutdownBlockReason);
        Assert.Equal(@"Couldn't save to D:\Shots", UnsavedAnnotationsPolicy.SaveFailedTitle(@"D:\Shots"));
        Assert.Equal("Couldn't copy to clipboard", UnsavedAnnotationsPolicy.CopyFailedTitle);
    }
}
