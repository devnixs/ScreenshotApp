namespace Snapper.Core.Settings;
public enum ScreenshotDestination { File, Clipboard, Both }
public static class ScreenshotDestinationExtensions
{
    public static string Title(this ScreenshotDestination value) => value switch
    {
        ScreenshotDestination.File => "Save to file",
        ScreenshotDestination.Clipboard => "Copy to clipboard",
        ScreenshotDestination.Both => "Both",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static bool SavesToFile(this ScreenshotDestination value) => value is ScreenshotDestination.File or ScreenshotDestination.Both;
    public static bool CopiesToClipboard(this ScreenshotDestination value) => value is ScreenshotDestination.Clipboard or ScreenshotDestination.Both;
}
