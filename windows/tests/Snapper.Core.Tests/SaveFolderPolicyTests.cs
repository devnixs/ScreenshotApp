using Snapper.Core.Support;

namespace Snapper.Core.Tests;

public sealed class SaveFolderPolicyTests
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Snapper-policy-tests", "Screenshots");
    private readonly FakeSaveFolderFileSystem fs = new();
    private SaveFolderPolicy Policy => new(fs);
    private static readonly FileNamer Namer = new(TimeZoneInfo.Utc);
    private static readonly DateTimeOffset Instant = new(2026, 11, 2, 19, 18, 4, TimeSpan.Zero);
    private const string Plain = "Screenshot 2026-11-02 at 19.18.04.png";

    [Fact] public void DefaultFolderUsesPictures()
    {
        Assert.Equal(Path.Combine(folder, "Screenshots"), SaveFolderPolicy.DefaultFolder(folder, "/other", Path.GetTempPath()));
        Assert.Equal(Path.Combine(folder, "Screenshots"), SaveFolderPolicy.DefaultFolder(folder + Path.DirectorySeparatorChar, null, Path.GetTempPath()));
    }
    [Theory] [InlineData(null)] [InlineData("")]
    public void DefaultFolderFallsBackToProfile(string? pictures) => Assert.Equal(Path.Combine(folder, "Pictures", "Screenshots"), SaveFolderPolicy.DefaultFolder(pictures, folder, Path.GetTempPath()));
    [Theory] [InlineData(null, null)] [InlineData("", "")]
    public void DefaultFolderFallsBackToTemp(string? pictures, string? profile) => Assert.Equal(Path.Combine(folder, "Screenshots"), SaveFolderPolicy.DefaultFolder(pictures, profile, folder));
    [Fact] public void PrepareCreatesMissingFolder()
    {
        Assert.Equal(folder, Policy.Prepare(folder));
        Assert.Equal(["Kind:" + folder, "Create:" + folder, "Probe:" + folder], fs.Calls);
    }
    [Fact] public void PrepareExistingWritableFolder()
    {
        fs.Entries[folder] = FolderEntryKind.Directory;
        Assert.Equal(folder, Policy.Prepare(folder));
        Assert.Equal(["Kind:" + folder, "Probe:" + folder], fs.Calls);
    }
    [Fact] public void PrepareFileThrowsNotADirectory()
    {
        fs.Entries[folder] = FolderEntryKind.File;
        var error = Assert.Throws<SaveFolderException>(() => Policy.Prepare(folder));
        Assert.Equal(SaveFolderFailure.NotADirectory, error.Failure);
        Assert.Equal(folder, error.FolderPath);
        Assert.Equal(["Kind:" + folder], fs.Calls);
    }
    [Fact] public void PrepareCreateFailureThrowsCannotCreate()
    {
        var inner = new UnauthorizedAccessException("denied");
        fs.CreateError = inner;
        var error = Assert.Throws<SaveFolderException>(() => Policy.Prepare(folder));
        Assert.Equal(SaveFolderFailure.CannotCreate, error.Failure);
        Assert.Same(inner, error.InnerException);
        Assert.DoesNotContain("Probe:" + folder, fs.Calls);
    }
    [Fact] public void PrepareProbeFailureThrowsNotWritable()
    {
        var inner = new IOException("blocked");
        fs.ProbeError = inner;
        var error = Assert.Throws<SaveFolderException>(() => Policy.Prepare(folder));
        Assert.Equal(SaveFolderFailure.NotWritable, error.Failure);
        Assert.Same(inner, error.InnerException);
    }
    [Fact] public void PrepareNormalizesPath()
    {
        var input = Path.Combine(folder, "a", "..") + new string(Path.DirectorySeparatorChar, 3);
        Assert.Equal(folder, Policy.Prepare(input));
        Assert.All(fs.Calls, call => Assert.EndsWith(folder, call));
        fs.ProbeError = new IOException("blocked");
        Assert.Equal(folder, Assert.Throws<SaveFolderException>(() => Policy.Prepare(input)).FolderPath);
    }
    [Fact] public void PrepareRejectsRelativePath() => Assert.Throws<ArgumentException>(() => Policy.Prepare("Screenshots"));
    [Fact] public void UniqueFilePathSkipsExisting()
    {
        fs.Entries[Path.Join(folder, Plain)] = FolderEntryKind.File;
        fs.Entries[Path.Join(folder, "Screenshot 2026-11-02 at 19.18.04 (2).png")] = FolderEntryKind.File;
        Assert.Equal(Path.Join(folder, "Screenshot 2026-11-02 at 19.18.04 (3).png"), Policy.UniqueFilePath(folder, CaptureKind.Screenshot, Instant, Namer));
    }
    [Fact] public void UniqueFilePathIsCaseInsensitive()
    {
        fs.Entries[Path.Join(folder, Plain.ToUpperInvariant())] = FolderEntryKind.File;
        Assert.Equal(Path.Join(folder, "Screenshot 2026-11-02 at 19.18.04 (2).png"), Policy.UniqueFilePath(folder, CaptureKind.Screenshot, Instant, Namer));
    }
    [Fact] public void UniqueFilePathTreatsDirectoryAsTaken()
    {
        fs.Entries[Path.Join(folder, Plain)] = FolderEntryKind.Directory;
        Assert.EndsWith(" (2).png", Policy.UniqueFilePath(folder, CaptureKind.Screenshot, Instant, Namer));
    }
    [Fact] public void UniqueFilePathPropagatesFolderErrors()
    {
        fs.Entries[folder] = FolderEntryKind.File;
        Assert.Equal(SaveFolderFailure.NotADirectory, Assert.Throws<SaveFolderException>(() => Policy.UniqueFilePath(folder, CaptureKind.Screenshot, Instant, Namer)).Failure);
        Assert.Equal(["Kind:" + folder], fs.Calls);
    }
    [Theory]
    [InlineData(@"C:\Users\ana\Pictures\Screenshots", @"C:\Users\ana\Pictures\Screenshots")]
    [InlineData(@"C:\Users\ana\Pictures\Screenshots\", @"C:\Users\ana\Pictures\Screenshots")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData(@"\\nas\share\Shots\", @"\\nas\share\Shots")]
    [InlineData(@"\\nas\share\", @"\\nas\share\")]
    [InlineData("/", "/")]
    public void FailureTitleUsesFullPath(string input, string display)
    {
        Assert.Equal(display, SaveFolderPolicy.FolderDisplayPath(input));
        Assert.Equal("Couldn't save to " + display, SaveFolderPolicy.FailureTitle(input));
        Assert.DoesNotContain("~", SaveFolderPolicy.FailureTitle(input));
    }
    [Theory]
    [InlineData(SaveFolderFailure.CannotCreate, "Access is denied.", "Access is denied.")]
    [InlineData(SaveFolderFailure.CannotCreate, null, "The save folder couldn't be created.")]
    [InlineData(SaveFolderFailure.NotWritable, null, "You don't have permission to write to this folder.")]
    [InlineData(SaveFolderFailure.NotWritable, "Blocked.", "You don't have permission to write to this folder. Blocked.")]
    [InlineData(SaveFolderFailure.NotWritable, "", "You don't have permission to write to this folder.")]
    [InlineData(SaveFolderFailure.NotWritable, "You don't have permission to write to this folder.", "You don't have permission to write to this folder.")]
    [InlineData(SaveFolderFailure.NotADirectory, null, "The save folder path is a file, not a folder.")]
    public void FailureDetailsPerFailure(SaveFolderFailure failure, string? inner, string expected)
    {
        var error = new SaveFolderException(failure, folder, inner is null ? null : new IOException(inner));
        Assert.Equal(expected, SaveFolderPolicy.FailureDetails(error));
        Assert.Equal(expected, error.Message);
    }
}

internal sealed class FakeSaveFolderFileSystem : ISaveFolderFileSystem
{
    public Dictionary<string, FolderEntryKind> Entries { get; } = new(FileNamer.NameComparer);
    public List<string> Calls { get; } = [];
    public Exception? CreateError { get; set; }
    public Exception? ProbeError { get; set; }
    public FolderEntryKind GetEntryKind(string path) { Calls.Add("Kind:" + path); return Entries.GetValueOrDefault(path, FolderEntryKind.Missing); }
    public void CreateDirectory(string path)
    {
        Calls.Add("Create:" + path);
        if (CreateError is not null) throw CreateError;
        Entries[path] = FolderEntryKind.Directory;
    }
    public void ProbeWrite(string path) { Calls.Add("Probe:" + path); if (ProbeError is not null) throw ProbeError; }
    public bool EntryExists(string path) { Calls.Add("Exists:" + path); return Entries.ContainsKey(path); }
}
