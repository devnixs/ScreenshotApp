using Snapper.Core.Annotate;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Forms = System.Windows.Forms;

namespace Snapper.Windows.Annotate;

internal static class AnnotationPrompts
{
    public static Forms.TaskDialogPage DiscardPage()
    {
        var page = Page(UnsavedAnnotationsPolicy.DiscardTitle);
        var discard = new Forms.TaskDialogButton(UnsavedAnnotationsPolicy.DiscardButton);
        page.Buttons.Add(discard); page.Buttons.Add(Forms.TaskDialogButton.Cancel); page.DefaultButton = discard;
        return page;
    }
    public static bool ConfirmExit(string message)
    {
        var page = Page(message);
        var exit = new Forms.TaskDialogButton(UnsavedAnnotationsPolicy.QuitButton);
        page.Buttons.Add(exit); page.Buttons.Add(Forms.TaskDialogButton.Cancel); page.DefaultButton = exit;
        page.Created += (_, _) =>
        {
            if (page.BoundDialog is not { } dialog) return;
            var hwnd = new HWND(dialog.Handle);
            PInvoke.SetWindowPos(hwnd, new HWND(-1), 0, 0, 0, 0, // HWND_TOPMOST, WinUser.h pseudo-handle.
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
            PInvoke.SetForegroundWindow(hwnd);
        };
        return Forms.TaskDialog.ShowDialog(page, Forms.TaskDialogStartupLocation.CenterScreen) == exit;
    }
    private static Forms.TaskDialogPage Page(string heading) => new()
    { Caption = "Snapper", Heading = heading, Icon = Forms.TaskDialogIcon.Warning, AllowCancel = true };
}
