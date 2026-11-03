namespace Snapper.Core.Recording;
public sealed class RecordingTerminationDecision
{
    public const string ShutdownBlockReason = "Saving recording…";
    private bool pending;
    public bool Request(bool recordingActive) { if (recordingActive) pending = true; return pending; }
    public bool RecordingEnded() { var reply = pending; pending = false; return reply; }
}
