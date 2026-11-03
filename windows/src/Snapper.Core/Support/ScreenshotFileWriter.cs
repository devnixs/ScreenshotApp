namespace Snapper.Core.Support;
public static class ScreenshotFileWriter
{
    public static string Save(byte[] png, string folder, DateTime captureDate) => Save(png, folder, new DateTimeOffset(captureDate));
    public static string Save(byte[] png, string folder, DateTimeOffset captureDate)
    {
        var fs = new PhysicalSaveFolderFileSystem();
        folder = new SaveFolderPolicy(fs).Prepare(folder);
        var namer = new FileNamer();
        for (var attempt = 0; ; attempt++)
        {
            var path = Path.Join(folder, namer.UniqueFileName(CaptureKind.Screenshot, captureDate, name => fs.EntryExists(Path.Join(folder, name))));
            FileStream output;
            try { output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (attempt == 0 && fs.EntryExists(path)) { continue; }
            using (output) output.Write(png);
            return path;
        }
    }
}
