using Snapper.Core.Support;

namespace Snapper.Core.Annotate;

public enum CloseDecision { CloseImmediately, ConfirmDiscard }
public enum SessionEndDecision { Allow, Block }
public abstract record QuitDecision
{
    public sealed record TerminateNow : QuitDecision;
    public sealed record Confirm(string Message) : QuitDecision;
}

public static class UnsavedAnnotationsPolicy
{
    public const string DiscardTitle = "Discard changes?", DiscardButton = "Discard", QuitButton = "Exit", CancelButton = "Cancel";
    public const string ShutdownBlockReason = "Unsaved annotations", CopyFailedTitle = "Couldn't copy to clipboard";
    public static CloseDecision CloseDecisionFor(bool isDirtyAfterCommittingText) =>
        isDirtyAfterCommittingText ? CloseDecision.ConfirmDiscard : CloseDecision.CloseImmediately;
    public static QuitDecision QuitDecisionFor(int dirtyWindowCount) => dirtyWindowCount > 0
        ? new QuitDecision.Confirm(QuitMessage(dirtyWindowCount)) : new QuitDecision.TerminateNow();
    public static string QuitMessage(int dirtyWindowCount) =>
        $"Exit and discard {dirtyWindowCount} unsaved annotation{(dirtyWindowCount == 1 ? "" : "s")}?";
    public static SessionEndDecision SessionEndDecisionFor(bool isDirtyAfterCommittingText, bool isCritical) =>
        isDirtyAfterCommittingText && !isCritical ? SessionEndDecision.Block : SessionEndDecision.Allow;
    public static string SaveFailedTitle(string folderPath) => SaveFolderPolicy.FailureTitle(folderPath);
}
