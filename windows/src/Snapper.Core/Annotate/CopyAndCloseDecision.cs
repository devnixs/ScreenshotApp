namespace Snapper.Core.Annotate;

public enum CopyAndCloseStart { Refuse, Copy }
public enum CopyAndCloseFinish { Close, StayOpen }

public static class CopyAndCloseDecision
{
    public const int VirtualKeyReturn = 0x0D;

    public static CopyAndCloseStart Start(bool isDragging, bool hasOpenDialog) =>
        isDragging || hasOpenDialog ? CopyAndCloseStart.Refuse : CopyAndCloseStart.Copy;

    public static CopyAndCloseFinish Finish(bool copySucceeded) =>
        copySucceeded ? CopyAndCloseFinish.Close : CopyAndCloseFinish.StayOpen;

    public static bool IsShortcut(int virtualKey, bool ctrl, bool shift, bool alt, bool win, bool isRepeat) =>
        virtualKey == VirtualKeyReturn && ctrl && !shift && !alt && !win && !isRepeat;
}
