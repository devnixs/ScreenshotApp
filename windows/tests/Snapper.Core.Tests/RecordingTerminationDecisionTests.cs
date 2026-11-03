using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class RecordingTerminationDecisionTests
{
    [Fact] public void IdleQuitsImmediately() { var d = new RecordingTerminationDecision(); Assert.False(d.Request(false)); Assert.False(d.RecordingEnded()); }
    [Fact] public void ActiveRecordingDefersExactlyOneReply()
    {
        var d = new RecordingTerminationDecision(); Assert.True(d.Request(true)); Assert.True(d.Request(true));
        Assert.True(d.RecordingEnded()); Assert.False(d.RecordingEnded()); Assert.False(d.Request(false));
    }
    [Fact] public void OrdinaryStopNeverRepliesToTermination() => Assert.False(new RecordingTerminationDecision().RecordingEnded());
    [Fact] public void ShutdownReasonUsesRealEllipsis() => Assert.Equal("Saving recording…", RecordingTerminationDecision.ShutdownBlockReason);
}
