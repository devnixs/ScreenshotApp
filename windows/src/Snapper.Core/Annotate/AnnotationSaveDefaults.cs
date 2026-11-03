using Snapper.Core.Support;

namespace Snapper.Core.Annotate;

public static class AnnotationSaveDefaults
{
    public static string SuggestedFileName(DateTime captureTime, string? directory, Func<string, bool> fileExists, FileNamer? namer = null)
    {
        namer ??= new FileNamer();
        var instant = new DateTimeOffset(captureTime);
        return directory is null ? namer.FileName(CaptureKind.Screenshot, instant)
            : namer.UniqueFileName(CaptureKind.Screenshot, instant, name => fileExists(Path.Combine(directory, name)));
    }
}
