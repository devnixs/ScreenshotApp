namespace Snapper.Core.Capture;
public interface IScreenshotOutputEnvironment
{
    bool CopyToClipboard();
    string Save(string folder);
    string? ShowSaveFolderFailure(string folder, Exception error);
    void ShowError(string title, string message);
}
public readonly record struct ScreenshotOutputResult(bool Copied, string? SavedPath);
public static class ScreenshotOutputRunner
{
    public static ScreenshotOutputResult Run(ScreenshotOutputPlan plan, string saveFolder, IScreenshotOutputEnvironment env)
    {
        var copied = plan.CopiesToClipboard && env.CopyToClipboard();
        if (plan.CopiesToClipboard && !copied)
            env.ShowError("Couldn't copy to clipboard", "The clipboard couldn't accept the screenshot.");
        if (!plan.SavesToFile) return new(copied, null);
        var folder = saveFolder;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { return new(copied, env.Save(folder)); }
            catch (Exception error)
            {
                if (ScreenshotOutputPlan.ClassifySaveFailure(error) == SaveFailureAlert.Generic)
                { env.ShowError("Couldn't save screenshot", error.Message); break; }
                var next = env.ShowSaveFolderFailure(folder, error);
                if (next is null || attempt == 1) break;
                folder = next;
            }
        }
        return new(copied, null);
    }
}
