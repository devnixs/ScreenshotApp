using System.Diagnostics;

namespace Snapper.Core.Support;

public static class AtomicFileWriter
{
    /// <summary>Write a sibling temporary file, then atomically replace the chosen name without truncating it first.</summary>
    public static void Replace(string target, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? throw new ArgumentException("A destination folder is required.", nameof(target));
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Trace.TraceWarning("Couldn't remove annotation temporary file: {0}", error.Message); }
        }
    }
}
