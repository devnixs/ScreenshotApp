using System.Diagnostics;
using System.IO;
using Windows.Win32;
using Windows.Win32.UI.Shell.Common;

namespace Snapper.Windows.Support;

internal static class ExplorerReveal
{
    /// <summary>Call on the WPF dispatcher (STA). Opens the parent folder and selects the saved file.</summary>
    public static unsafe bool RevealFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Trace.TraceWarning("Couldn't reveal missing file: {0}", filePath);
            return false;
        }
        try
        {
            ITEMIDLIST* pidl = null;
            try
            {
                if (PInvoke.SHParseDisplayName(filePath, null, out pidl, 0, out _).Succeeded &&
                    PInvoke.SHOpenFolderAndSelectItems(pidl, 0, null, 0).Succeeded) return true;
            }
            finally { if (pidl != null) PInvoke.ILFree(pidl); }
        }
        catch (Exception error) { Trace.TraceWarning("Shell reveal failed: {0}", error); }
        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            // The shell can reuse an existing Explorer window without returning a process.
            return true;
        }
        catch (Exception error)
        {
            Trace.TraceWarning("Couldn't reveal file {0}: {1}", filePath, error);
            return false;
        }
    }

    public static bool OpenFolder(string folderPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
            return true;
        }
        catch (Exception error)
        {
            Trace.TraceWarning("Couldn't open folder {0}: {1}", folderPath, error);
            return false;
        }
    }
}
