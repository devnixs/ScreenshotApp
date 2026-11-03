using Snapper.Core.Support;
namespace Snapper.Core.Recording;

/// <summary>Portable file moves. Publish a complete sibling before deleting the source, including across volumes.</summary>
public static class RecordingOutputFiles
{
    public static string Propose(string folder, DateTimeOffset startTime, CaptureKind kind = CaptureKind.RecordingMp4) =>
        new SaveFolderPolicy(new PhysicalSaveFolderFileSystem()).UniqueFilePath(folder, kind, startTime, new FileNamer());

    public static string SaveOriginal(string source, string folder, DateTimeOffset startTime, string? preferredPath = null,
        CancellationToken token = default, CaptureKind kind = CaptureKind.RecordingMp4)
    {
        var fs = new PhysicalSaveFolderFileSystem();
        folder = new SaveFolderPolicy(fs).Prepare(folder);
        var temporary = Path.Combine(folder, $".snapper-recording-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            var destination = preferredPath ?? Propose(folder, startTime, kind);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { File.Move(temporary, destination); break; }
                catch (IOException) when (fs.EntryExists(destination)) { destination = Propose(folder, startTime, kind); }
            }
            // A source deletion failure must not invalidate an already committed destination.
            TryDelete(source);
            return destination;
        }
        finally { TryDelete(temporary); }
    }
    public static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine($"Snapper: Removing recording file: {error.Message}"); }
    }
}
