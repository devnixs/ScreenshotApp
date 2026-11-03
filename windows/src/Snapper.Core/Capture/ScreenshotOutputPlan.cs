using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Capture;
public enum SaveFailureAlert { SaveFolder, Generic }
public sealed class ScreenshotOutputPlan(ScreenshotDestination destination)
{
    public bool CopiesToClipboard { get; } = destination.CopiesToClipboard();
    public bool SavesToFile { get; } = destination.SavesToFile();
    public static SaveFailureAlert ClassifySaveFailure(Exception error)
    {
        Exception? current = error;
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        for (var level = 0; level < 16 && current is not null && seen.Add(current); level++, current = current.InnerException)
            if (current is SaveFolderException or UnauthorizedAccessException or DirectoryNotFoundException or DriveNotFoundException ||
                current is IOException && unchecked((uint)current.HResult) is 0x80070005 or 0x80070013 or 0x80070003 or 0x80070015 or 0x80070035)
                return SaveFailureAlert.SaveFolder;
        return SaveFailureAlert.Generic;
    }
    public static double PngDpi(double scaleFactor) => 96 * scaleFactor;
    public static string NearestExistingFolder(string folder, Func<string, bool> directoryExists)
    {
        for (string? current = folder; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (directoryExists(current)) return current;
        return Path.GetPathRoot(folder) is { Length: > 0 } root ? root : folder;
    }
}
