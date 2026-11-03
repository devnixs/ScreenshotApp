using System.Diagnostics;

namespace Snapper.Core.Support;

public enum SaveFolderFailure { CannotCreate, NotWritable, NotADirectory }

public sealed class SaveFolderException : IOException
{
    public SaveFolderException(SaveFolderFailure failure, string folderPath, Exception? inner = null)
        : base(SaveFolderPolicy.Details(failure, inner), inner)
    {
        Failure = failure;
        FolderPath = folderPath;
    }

    public SaveFolderFailure Failure { get; }
    public string FolderPath { get; }
}

public enum FolderEntryKind { Missing, Directory, File }

public interface ISaveFolderFileSystem
{
    /// <summary>Follows directory links; dangling links count as files.</summary>
    FolderEntryKind GetEntryKind(string path);
    void CreateDirectory(string path);
    void ProbeWrite(string folderPath);
    /// <summary>Checks the final entry itself, including dangling links. Inaccessible names are taken.</summary>
    bool EntryExists(string path);
}

public sealed class PhysicalSaveFolderFileSystem : ISaveFolderFileSystem
{
    public FolderEntryKind GetEntryKind(string path) => Directory.Exists(path)
        ? FolderEntryKind.Directory : EntryExists(path) ? FolderEntryKind.File : FolderEntryKind.Missing;
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void ProbeWrite(string folderPath)
    {
        using var probe = new FileStream(Path.Join(folderPath, $".snapper-write-test-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    public bool EntryExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch { return true; }
    }
}

public sealed class SaveFolderPolicy(ISaveFolderFileSystem fileSystem)
{
    public static string DefaultFolder(string? picturesFolder, string? userProfile, string tempPath) =>
        Path.Combine(!string.IsNullOrEmpty(picturesFolder) ? picturesFolder
            : !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "Pictures") : tempPath, "Screenshots");

    public string Prepare(string folderPath)
    {
        if (!Path.IsPathFullyQualified(folderPath))
            throw new ArgumentException("The save folder must be an absolute path.", nameof(folderPath));
        var normalized = Path.GetFullPath(folderPath);
        var rootLength = Path.GetPathRoot(normalized)?.Length ?? 0;
        normalized = normalized[..Math.Max(rootLength,
            normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)];
        switch (fileSystem.GetEntryKind(normalized))
        {
            case FolderEntryKind.File:
                throw new SaveFolderException(SaveFolderFailure.NotADirectory, normalized);
            case FolderEntryKind.Missing:
                try { fileSystem.CreateDirectory(normalized); }
                catch (Exception error)
                {
                    Trace.TraceWarning("Couldn't create save folder {0}: {1}", normalized, error);
                    throw new SaveFolderException(SaveFolderFailure.CannotCreate, normalized, error);
                }
                break;
        }
        try { fileSystem.ProbeWrite(normalized); }
        catch (Exception error)
        {
            Trace.TraceWarning("Couldn't write to save folder {0}: {1}", normalized, error);
            throw new SaveFolderException(SaveFolderFailure.NotWritable, normalized, error);
        }
        return normalized;
    }

    /// <summary>Does not reserve the name. Writers must use FileMode.CreateNew and handle races.</summary>
    public string UniqueFilePath(string folderPath, CaptureKind kind, DateTimeOffset instant, FileNamer namer)
    {
        var folder = Prepare(folderPath);
        return Path.Join(folder, namer.UniqueFileName(kind, instant, name => fileSystem.EntryExists(Path.Join(folder, name))));
    }

    public static string FailureTitle(string folderPath) => "Couldn't save to " + FolderDisplayPath(folderPath);

    /// <summary>Display Windows paths consistently on every host without abbreviating them.</summary>
    public static string FolderDisplayPath(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('\\', '/');
        if (trimmed.Length == folderPath.Length) return folderPath;
        if (trimmed.Length == 0) return folderPath[..1];
        if (trimmed.Length == 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':')
            return folderPath[..3];
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = trimmed[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) return trimmed + folderPath[trimmed.Length];
        }
        return trimmed;
    }

    public static string FailureDetails(SaveFolderException error) => Details(error.Failure, error.InnerException);

    internal static string Details(SaveFolderFailure failure, Exception? inner)
    {
        const string permission = "You don't have permission to write to this folder.";
        return failure switch
        {
            SaveFolderFailure.CannotCreate => inner?.Message ?? "The save folder couldn't be created.",
            SaveFolderFailure.NotWritable => permission +
                (!string.IsNullOrEmpty(inner?.Message) && inner.Message != permission ? " " + inner.Message : ""),
            SaveFolderFailure.NotADirectory => "The save folder path is a file, not a folder.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
    }
}
