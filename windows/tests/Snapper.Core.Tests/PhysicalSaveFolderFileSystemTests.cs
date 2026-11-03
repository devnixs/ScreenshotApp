using Snapper.Core.Support;

namespace Snapper.Core.Tests;

public sealed class PhysicalSaveFolderFileSystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Snapper-folder-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SaveFolderPolicy policy = new(new PhysicalSaveFolderFileSystem());
    private readonly FileNamer namer = new(TimeZoneInfo.Utc);
    private readonly DateTimeOffset instant = new(2026, 11, 2, 19, 18, 4, TimeSpan.Zero);

    public PhysicalSaveFolderFileSystemTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact] public void CreatesMissingNestedFolder()
    {
        var folder = Path.Combine(root, "a", "b", "Screenshots");
        Assert.Equal(folder, policy.Prepare(folder));
        Assert.True(Directory.Exists(folder));
    }
    [Fact] public void ExistingFolderIsUntouched()
    {
        var sentinel = Path.Combine(root, "sentinel");
        File.WriteAllText(sentinel, "keep me");
        Assert.Equal(root, policy.Prepare(root));
        Assert.Equal(root, policy.Prepare(root));
        Assert.Equal("keep me", File.ReadAllText(sentinel));
        Assert.Equal([sentinel], Directory.GetFileSystemEntries(root));
    }
    [Fact] public void FileAtPathThrowsNotADirectory()
    {
        var file = Path.Combine(root, "Screenshots");
        File.WriteAllText(file, "");
        Assert.Equal(SaveFolderFailure.NotADirectory, Assert.Throws<SaveFolderException>(() => policy.Prepare(file)).Failure);
    }
    [Fact] public void ReadOnlyFolderThrowsNotWritable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.UserName == "root", "Needs Unix write permissions without root bypass.");
        if (OperatingSystem.IsWindows()) return;
        var original = File.GetUnixFileMode(root);
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Equal(SaveFolderFailure.NotWritable, Assert.Throws<SaveFolderException>(() => policy.Prepare(root)).Failure);
        }
        finally { File.SetUnixFileMode(root, original); }
    }
    [Fact] public void ReadOnlyParentThrowsCannotCreate()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.UserName == "root", "Needs Unix write permissions without root bypass.");
        if (OperatingSystem.IsWindows()) return;
        var original = File.GetUnixFileMode(root);
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Equal(SaveFolderFailure.CannotCreate, Assert.Throws<SaveFolderException>(() => policy.Prepare(Path.Combine(root, "Screenshots"))).Failure);
        }
        finally { File.SetUnixFileMode(root, original); }
    }
    [Fact] public void UniqueFilePathInEmptyFolder()
    {
        var folder = Path.Combine(root, "Screenshots");
        var path = policy.UniqueFilePath(folder, CaptureKind.Screenshot, instant, namer);
        Assert.Equal(Path.Join(folder, namer.FileName(CaptureKind.Screenshot, instant)), path);
        Assert.False(File.Exists(path));
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }
    [Fact] public void UniqueFilePathSkipsExistingFiles()
    {
        var stem = namer.BaseName(CaptureKind.Screenshot, instant);
        File.WriteAllText(Path.Join(root, stem + ".png"), "");
        File.WriteAllText(Path.Join(root, stem + " (2).png"), "");
        var path = policy.UniqueFilePath(root, CaptureKind.Screenshot, instant, namer);
        Assert.Equal(Path.Join(root, stem + " (3).png"), path);
        Assert.False(File.Exists(path));
    }
    [Fact] public void DirectorySymlinkIsAccepted()
    {
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(root, "alias");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        { Assert.Skip("Directory links unavailable: " + error.Message); }
        Assert.Equal(link, policy.Prepare(link));
    }
    [Fact] public void DanglingSymlinkIsACollision()
    {
        var link = Path.Join(root, namer.FileName(CaptureKind.Screenshot, instant));
        try { File.CreateSymbolicLink(link, Path.Combine(root, "missing")); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        { Assert.Skip("File links unavailable: " + error.Message); }
        Assert.True(new PhysicalSaveFolderFileSystem().EntryExists(link));
        Assert.Equal(FolderEntryKind.File, new PhysicalSaveFolderFileSystem().GetEntryKind(link));
        Assert.EndsWith(" (2).png", policy.UniqueFilePath(root, CaptureKind.Screenshot, instant, namer));
    }
}
