using Snapper.Core.Support;
namespace Snapper.Core.Recording;

/// <summary>Reserves the final name before the native writer opens it, without replacing any existing entry.</summary>
public static class RecordingFilePath
{
    public static string Reserve(string folder, DateTimeOffset startDate)
    {
        var fs = new PhysicalSaveFolderFileSystem();
        var policy = new SaveFolderPolicy(fs);
        var namer = new FileNamer();
        folder = policy.Prepare(folder);
        for (var attempt = 0; ; attempt++)
        {
            var path = System.IO.Path.Join(folder, namer.UniqueFileName(CaptureKind.RecordingMp4, startDate, name => fs.EntryExists(System.IO.Path.Join(folder, name))));
            try { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); return path; }
            catch (IOException) when (attempt == 0 && fs.EntryExists(path)) { }
        }
    }
}
