using System.Diagnostics;
using Snapper.Core.App;
namespace Snapper.Core.Capture;

public enum SelectionOutcome { Selected, Cancelled }
public static class SelectionFocusPolicy
{
    public static bool RestoresPreviousWindow(CapturePurpose purpose, SelectionOutcome outcome) => outcome == SelectionOutcome.Cancelled || purpose switch
    {
        CapturePurpose.Screenshot or CapturePurpose.Record => true,
        CapturePurpose.Annotate => false,
        _ => throw new UnreachableException()
    };
    public static bool ShouldRemember(bool isWindow, bool isVisible, string className) => isWindow && isVisible &&
        className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW");
}
