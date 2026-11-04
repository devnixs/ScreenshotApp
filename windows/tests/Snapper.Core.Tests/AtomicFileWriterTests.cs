using Snapper.Core.Support;
namespace Snapper.Core.Tests;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SnapperAtomicTests-" + Guid.NewGuid());
    public AtomicFileWriterTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, recursive: true);
    [Fact] public void WritesAndReplacesExactBytesWithoutTemporaryFiles()
    {
        var target = Path.Combine(folder, "image.png");
        AtomicFileWriter.Replace(target, [1, 2, 3]); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        AtomicFileWriter.Replace(target, [4, 5]); Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFileSystemEntries(folder));
    }
    [Fact] public void FailedRenameCleansTemporaryAndPreservesExistingEntry()
    {
        var target = Path.Combine(folder, "image.png"); Directory.CreateDirectory(target);
        var retained = Path.Combine(target, "keep"); File.WriteAllBytes(retained, [9]);
        // Replacing a directory is an access error on Windows and an I/O error on Unix.
        var error = Record.Exception(() => AtomicFileWriter.Replace(target, [1, 2]));
        Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected failure: {error}");
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(retained));
        Assert.Equal(new[] { target }, Directory.GetFileSystemEntries(folder));
    }
    [Fact] public void DoesNotOverwriteAnUnrelatedTemporaryFile()
    {
        var target = Path.Combine(folder, "image.png"); var unrelated = target + ".tmp";
        File.WriteAllBytes(unrelated, [8]); AtomicFileWriter.Replace(target, [1]);
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(unrelated));
    }
    [Fact] public void MissingFolderFailsWithoutCreatingIt()
    {
        var missing = Path.Combine(folder, "absent");
        Assert.Throws<DirectoryNotFoundException>(() => AtomicFileWriter.Replace(Path.Combine(missing, "image.png"), [1]));
        Assert.False(Directory.Exists(missing));
    }
}
