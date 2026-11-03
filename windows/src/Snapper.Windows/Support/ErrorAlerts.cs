using System.IO;
using Microsoft.Win32;
using Snapper.Core.Capture;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Forms = System.Windows.Forms;
namespace Snapper.Windows.Support;
internal static class ErrorAlerts
{
    private static Forms.TaskDialogPage Page(string title, string message)
    {
        var page = new Forms.TaskDialogPage
        {
            Caption = "Snapper", Heading = title, Text = message,
            Icon = Forms.TaskDialogIcon.Warning
        };
        page.Created += (_, _) =>
        {
            if (page.BoundDialog is { } dialog) PInvoke.SetForegroundWindow(new HWND(dialog.Handle));
        };
        return page;
    }
    public static void ShowCaptureFailure(Exception error) => ShowError("Couldn't take screenshot", error.Message);
    public static void ShowError(string title, string message)
    {
        var page = Page(title, message);
        page.Buttons.Add(Forms.TaskDialogButton.OK);
        Forms.TaskDialog.ShowDialog(page, Forms.TaskDialogStartupLocation.CenterScreen);
    }
    public static string? ShowSaveFolderFailure(string folder, Exception error, AppSettings settings)
    {
        var page = Page(SaveFolderPolicy.FailureTitle(folder), error.Message);
        var choose = new Forms.TaskDialogButton("Choose Folder…");
        page.Buttons.Add(choose); page.Buttons.Add(Forms.TaskDialogButton.Cancel); page.DefaultButton = choose;
        if (Forms.TaskDialog.ShowDialog(page, Forms.TaskDialogStartupLocation.CenterScreen) != choose) return null;
        var picker = new OpenFolderDialog
        {
            Title = "Choose Save Folder", Multiselect = false,
            InitialDirectory = ScreenshotOutputPlan.NearestExistingFolder(folder, Directory.Exists)
        };
        if (picker.ShowDialog() != true) return null;
        // AppSettings owns the store and persists/announces this change itself.
        settings.SaveFolder = picker.FolderName;
        return picker.FolderName;
    }
}
