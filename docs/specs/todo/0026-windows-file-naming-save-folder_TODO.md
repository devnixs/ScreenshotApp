# 0026 — Add Windows file naming, save-folder preparation, and Explorer reveal

> Source: docs/PRD.md §9 (Files and naming), §13.8 (Windows files and naming), §13.5 (alerts use the full folder path), §13.6 (File Explorer opens the folder with the file selected), §13.2 (Finder → File Explorer), §12 ("The default save folder is created when it is missing")
> Depends on: 0025-windows-solution-tray-shell

## Context

0025 creates `windows/` with `Snapper.Core` (net10.0, pure), `Snapper.Windows` (WPF app), `tests/Snapper.Core.Tests` (xUnit), and `NativeMethods.txt`. Nothing in the Windows app names, saves, or reveals files yet.

The macOS reference implementation is complete and tested:

- `Snapper/Support/CaptureKind.swift:1-18`: prefix (`Screenshot`/`Recording`) and extension. macOS spec 0023 (todo, built before this batch) changes it to `screenshot | recording(RecordingFormat)`, so recordings can end in `.mp4` or `.gif` (PRD §9).
- `Snapper/Support/FileNamer.swift:3-41`: `baseName`, `fileName`, and `uniqueFileName(exists:)`. It uses a fixed Gregorian calendar, ASCII digits, the 24-hour clock, and an injected time zone. Collisions add ` (2)`, ` (3)`, … before the extension, and the first free slot wins.
- `Snapper/Support/SaveFolder.swift:4-88`: `defaultURL` (`~/Documents/Screenshots`), `prepare()`/`prepareForWriting()` (create on use, then check that the path is a directory and writable), `uniqueFileURL`, and `SaveFolderError` (`cannotCreate`/`creationFailed`/`notWritable`/`notADirectory`, plus `folderURL` and user-facing descriptions at `:79-88`).
- `Snapper/Capture/ScreenshotOutputPlan.swift:31-33`: `folderDisplayName` (a tilde-abbreviated path). `Snapper/Support/ErrorAlerts.swift:19-32` builds "Couldn't save to <folder>" from it.
- `Snapper/Recording/RecordingSession.swift:106`: Finder reveal (`activateFileViewerSelecting`).
- Tests: `SnapperTests/FileNamerTests.swift:21-122` (15 cases) and `SnapperTests/SaveFolderTests.swift:17-138` (13 cases). Spec: `docs/specs/done/0002-file-naming-save-folder_COMPLETED.md`.

PRD behavior to deliver on Windows:

- §13.8: the default folder is **`%USERPROFILE%\Pictures\Screenshots`** (the Pictures known folder + `Screenshots`), created on first use. File names are identical to macOS (`Screenshot 2026-11-02 at 19.18.04.png`, `Recording 2026-11-02 at 19.18.04.mp4`/`.gif`) and contain no characters that are invalid on Windows. The §9 collision rule applies.
- §9: screenshots use the capture time and recordings use the start time. If the folder can't be created or isn't writable, the alert says "Couldn't save to <folder>" and includes the system error details.
- §13.5: Windows alerts use the **full folder path**, not a tilde-abbreviated one.
- §13.6: after a recording is saved, File Explorer opens the save folder with the new file selected.

macOS facts that **don't apply** on Windows: `~` abbreviation (use the full path); `URL.documentsDirectory` (use the Pictures known folder); POSIX `isWritableFile` (Windows ACLs, Controlled Folder Access, and OneDrive placeholders make an access check unreliable, so writability is tested by actually creating a file); `os.Logger` (use `System.Diagnostics.Debug`/`Trace`); sandboxing and security-scoped bookmarks (n/a).

## User story

As a Snapper user on Windows, every screenshot and recording I save gets the same predictable, sortable name as on macOS, in `Pictures\Screenshots` by default. A name never overwrites an existing file, whatever its letter case. The folder appears by itself the first time I save. If it can't be used, I see the folder's full path in the error, and after a recording File Explorer opens with the new file selected.

## Scope & changes

Namespaces follow folders: `Snapper.Core.Support`, `Snapper.Windows.Support`. Nullable is enabled and warnings are errors (0025's `Directory.Build.props`).

### 1. `windows/src/Snapper.Core/Support/CaptureKind.cs` (pure)

```csharp
public enum CaptureKind { Screenshot, RecordingMp4, RecordingGif }

public static class CaptureKindExtensions
{
    public static string NamePrefix(this CaptureKind kind);    // "Screenshot" / "Recording" / "Recording"
    public static string FileExtension(this CaptureKind kind); // "png" / "mp4" / "gif" (lowercase, no dot)
}
```

- This is the C# equivalent of the post-0023 Swift `CaptureKind.recording(RecordingFormat)`. `RecordingFormat` is owned by 0027, which depends on this spec, so this spec must not reference it. 0027 (or the first recording spec that needs it, 0034) adds the one-line mapping `RecordingFormat.Mp4 → RecordingMp4`, `Gif → RecordingGif`.
- Unknown enum values (a cast from an int) throw `ArgumentOutOfRangeException` in both extension methods.

### 2. `windows/src/Snapper.Core/Support/FileNamer.cs` (pure)

```csharp
public sealed class FileNamer
{
    /// Case-insensitive, like NTFS. Use it for any in-memory set of existing names.
    public static StringComparer NameComparer { get; } = StringComparer.OrdinalIgnoreCase;

    public FileNamer(TimeZoneInfo? timeZone = null); // null → TimeZoneInfo.Local

    /// "Screenshot 2026-11-02 at 19.18.04", with no extension and no suffix.
    public string BaseName(CaptureKind kind, DateTimeOffset instant);
    /// "Screenshot 2026-11-02 at 19.18.04.png"
    public string FileName(CaptureKind kind, DateTimeOffset instant);
    /// The first of "<base>.<ext>", "<base> (2).<ext>", "<base> (3).<ext>", … for which exists(name) is false.
    public string UniqueFileName(CaptureKind kind, DateTimeOffset instant, Func<string, bool> exists);
}
```

Port of `Snapper/Support/FileNamer.swift:3-41`. Don't change the behavior:

- **Format:** `<prefix> yyyy-MM-dd at HH.mm.ss`, with a zero-padded 4-digit year and 2-digit month, day, hour (24-hour clock, `00`–`23`), minute, and second. Separators are exactly: a space after the prefix, `-` within the date, ` at `, and `.` within the time. No sub-second part.
- **Culture and calendar are fixed.** Convert with `TimeZoneInfo.ConvertTime(instant, timeZone)`, then format the components explicitly with `CultureInfo.InvariantCulture`, for example `string.Create(CultureInfo.InvariantCulture, $"{local.Year:D4}-{local.Month:D2}-{local.Day:D2} at {local.Hour:D2}.{local.Minute:D2}.{local.Second:D2}")`. Never use `ToString()` with the current culture, and never use a format string with `:` or `/`, which are culture-dependent separators in .NET. The user's culture (12-hour time, non-Latin digits, Thai/Japanese calendar) must not affect the output.
- **The clock is not read here.** `FileNamer` never calls `DateTime.Now`/`DateTimeOffset.Now`. Callers pass the capture time (screenshots) or the recording start time (recordings), which they get from an injected `System.TimeProvider` (`TimeProvider.System` in production). Put this in the XML doc comments, like the Swift doc comments at `FileNamer.swift:10,25,30`.
- **Collision suffix:** ` (N)` (N starts at 2) goes before the extension, which is always kept. `exists` receives the full file name, extension included, so a `.mp4` with the same base name doesn't collide with a `.png`. The loop has no upper bound (the production probe is backed by a finite directory).
- **Case-insensitivity (Windows-specific):** NTFS and ReFS treat `Screenshot … .PNG` and `screenshot … .png` as the same file. `FileNamer` passes names to `exists` exactly as it generates them. The production probe in §3 checks the disk, which applies the file system's own case rule. Any caller that checks against an in-memory set must build it with `FileNamer.NameComparer`. The tests cover both.
- **Windows-valid names:** the generated names contain only letters, digits, spaces, `-`, `.`, `(`, `)`. They contain none of `< > : " / \ | ? *` or control characters, don't end with a space or a dot, and are never a reserved device name (`CON`, `NUL`, `COM1`, …). A test asserts this.

### 3. `windows/src/Snapper.Core/Support/SaveFolderPolicy.cs` (pure decisions + a portable `System.IO` adapter)

```csharp
public enum SaveFolderFailure { CannotCreate, NotWritable, NotADirectory }

public sealed class SaveFolderException : IOException
{
    public SaveFolderException(SaveFolderFailure failure, string folderPath, Exception? inner = null);
    public SaveFolderFailure Failure { get; }
    public string FolderPath { get; }   // the normalized folder path the error refers to
    // Message == SaveFolderPolicy.FailureDetails(this)
}

public enum FolderEntryKind { Missing, Directory, File }

/// Everything SaveFolderPolicy needs from the file system, so the decisions are testable with a fake.
public interface ISaveFolderFileSystem
{
    /// Follows a symlink/junction to a directory (it counts as Directory). A dangling link is File.
    FolderEntryKind GetEntryKind(string path);
    void CreateDirectory(string path);        // creates intermediate folders; throws on failure
    void ProbeWrite(string folderPath);       // creates and deletes a temp file; throws if it can't
    /// True when anything exists at the path (file, directory, or dangling link), without following the final link.
    bool EntryExists(string path);
}

/// System.IO implementation. It uses no Windows-only API, so it also runs in the tests on macOS.
public sealed class PhysicalSaveFolderFileSystem : ISaveFolderFileSystem { … }

public sealed class SaveFolderPolicy
{
    public SaveFolderPolicy(ISaveFolderFileSystem fileSystem);

    /// "<pictures>\Screenshots". Falls back to "<userProfile>\Pictures\Screenshots" when the known
    /// folder is null/empty, then to "<tempPath>\Screenshots" when both are null/empty.
    public static string DefaultFolder(string? picturesFolder, string? userProfile, string tempPath);

    /// Normalizes, creates a missing folder (with intermediate folders), and checks that it is a
    /// writable directory. Returns the normalized path. Throws SaveFolderException.
    public string Prepare(string folderPath);

    /// Prepare() + FileNamer.UniqueFileName probed against the folder's real contents.
    /// Doesn't reserve the name: writers must open the file with FileMode.CreateNew.
    public string UniqueFilePath(string folderPath, CaptureKind kind, DateTimeOffset instant, FileNamer namer);

    /// "Couldn't save to C:\Users\ana\Pictures\Screenshots": the alert title (PRD §9, §13.5).
    public static string FailureTitle(string folderPath);
    /// The full path as shown: trailing separators trimmed, except for a root such as "D:\" or "/".
    public static string FolderDisplayPath(string folderPath);
    /// Alert body: the system error details (PRD §9).
    public static string FailureDetails(SaveFolderException error);
}
```

Port of `Snapper/Support/SaveFolder.swift:17-77`. Rules:

- **Normalization:** `Prepare` requires a fully qualified path (`Path.IsPathFullyQualified`) and throws `ArgumentException` otherwise. 0027 guarantees absolute paths by falling back to the default for invalid values (PRD §13.9). It normalizes with `Path.GetFullPath` (which resolves `.`/`..`) and trims trailing separators except at a root. All later checks and the returned path use the normalized form.
- **`Prepare` steps, in order:**
  1. `GetEntryKind(path)`. `File` → throw `NotADirectory`. Don't create or probe anything.
  2. `Missing` → `CreateDirectory(path)`. Any exception → throw `CannotCreate` with that exception as `inner`. This applies to any configured folder, not only the default: for example a disconnected drive `X:\Shots`, an offline UNC share, or a read-only parent. Creation happens **on use** (at save time), never at launch.
  3. `ProbeWrite(path)`. Any exception → throw `NotWritable` with that exception as `inner`. This catches ACL denial, Windows Defender **Controlled Folder Access** blocking writes to Pictures, read-only media, and a full disk.
- **`PhysicalSaveFolderFileSystem`:**
  - `GetEntryKind`: `Directory.Exists(path)` (follows links and junctions) → `Directory`. Otherwise, if `EntryExists(path)` → `File`. Otherwise `Missing`.
  - `CreateDirectory`: `Directory.CreateDirectory(path)`.
  - `ProbeWrite`: `new FileStream(Path.Join(folder, $".snapper-write-test-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)`, disposed immediately. With DeleteOnClose, the probe file is removed even if the process dies. Don't use the folder's read-only attribute: Windows ignores it for folders.
  - `EntryExists`: `try { File.GetAttributes(path); return true; } catch (FileNotFoundException or DirectoryNotFoundException) { return false; }`. On Windows, `GetFileAttributes` doesn't follow the final reparse point, so dangling symlinks count. Any other exception (for example `UnauthorizedAccessException`) returns **true**: the name is treated as taken, so it is never overwritten.
- **`UniqueFilePath`:** `Prepare`, then `namer.UniqueFileName(kind, instant, name => fs.EntryExists(Path.Join(folder, name)))`, and return `Path.Join(folder, name)`. A **directory** with the candidate name also counts as taken. Case-insensitivity comes from the file system itself.
- **`FailureTitle`** is `"Couldn't save to " + FolderDisplayPath(path)`. `FolderDisplayPath` is pure string work so it gives the same result on macOS: trim trailing `\` and `/` unless the result would be a root (`X:\`, `\\server\share\`, `/`). It never abbreviates (no `~`, no `%USERPROFILE%`).
- **`FailureDetails`**, mirroring `SaveFolder.swift:79-88`:
  - `CannotCreate`: the inner exception's `Message` if present, otherwise "The save folder couldn't be created."
  - `NotWritable`: "You don't have permission to write to this folder." followed by a space and the inner exception's `Message` when it adds something (an inner message that is non-empty and different).
  - `NotADirectory`: "The save folder path is a file, not a folder."
- Log the inner exception with `System.Diagnostics.Trace.TraceWarning` before throwing, so failures can be diagnosed.
- **No UI here.** The alert with **Choose Folder…**, Cancel, and the retry-once rule is built by 0031 (`Support/ErrorAlerts`), using `FailureTitle`/`FailureDetails`. Classifying a *write* exception (for example `UnauthorizedAccessException` while writing the PNG) as a save-folder failure is `ScreenshotOutputPlan`'s job (0031, port of `ScreenshotOutputPlan.swift:14-29`).

### 4. `windows/src/Snapper.Windows/Support/SaveFolder.cs` (platform glue)

```csharp
internal static class SaveFolder
{
    /// %USERPROFILE%\Pictures\Screenshots, resolved from the Pictures known folder (honors redirection, e.g. OneDrive).
    public static string DefaultPath { get; }
    public static SaveFolderPolicy CreatePolicy();   // new SaveFolderPolicy(new PhysicalSaveFolderFileSystem())
}
```

- `DefaultPath` = `SaveFolderPolicy.DefaultFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.DoNotVerify), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath())`. On Windows, `SpecialFolder.MyPictures` is `SHGetKnownFolderPath(FOLDERID_Pictures)`, so no CsWin32 entry is needed. `DoNotVerify` keeps it from failing when Pictures doesn't exist yet. Compute it once (lazy). It **doesn't create** the folder.
- 0027's `AppSettings` default for the save folder can't call this (Core can't reference `Snapper.Windows`), so 0027 receives it as an injected default value. This spec doesn't change settings.

### 5. `windows/src/Snapper.Windows/Support/ExplorerReveal.cs` (platform glue)

```csharp
internal static class ExplorerReveal
{
    /// Opens the file's folder in File Explorer with the file selected (PRD §13.6). Returns false on failure.
    public static bool RevealFile(string filePath);
    /// Opens the folder itself in File Explorer (for Settings' "Open folder", 0033). Returns false on failure.
    public static bool OpenFolder(string folderPath);
}
```

- `RevealFile`: `SHParseDisplayName(filePath, null, out pidl, 0, out _)`, then `SHOpenFolderAndSelectItems(pidl, 0, null, 0)` (with `cidl == 0`, the PIDL names the single item to select in its parent folder), then `ILFree(pidl)` in a `finally`. It needs COM initialized: call it on the WPF dispatcher thread (STA). If either call fails (HRESULT < 0), fall back to `Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true })`. If that throws, return `false` and trace the error. It never throws and never shows UI of its own. A missing file is a caller bug: trace it and return `false`.
- `OpenFolder`: `Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true })` inside try/catch. Return `false` on failure.
- Add to `windows/src/Snapper.Windows/NativeMethods.txt`: `SHParseDisplayName`, `SHOpenFolderAndSelectItems`, `ILFree`.

### 6. Wiring, tests, and the PRD

- **No callers are wired in this spec.** The tray app behaves exactly as 0025 left it. Later callers: 0031 (screenshot file path and the "Couldn't save to" alert), 0033 (save-folder picker and "Open folder"), 0034 (recording output path and the reveal after Stop), 0043 (annotation Save dialog default name, the port of `AnnotationSaveDefaults.swift`), 0046 (`.gif` names).
- Test files go in `windows/tests/Snapper.Core.Tests/` (see the test plan). No new NuGet package.
- No PRD change: this implements §9 and §13.8 as written.

## Acceptance criteria

- [ ] `new FileNamer(utc).FileName(CaptureKind.Screenshot, 2026-11-02T19:18:04Z)` is `Screenshot 2026-11-02 at 19.18.04.png`. `RecordingMp4` gives `Recording 2026-11-02 at 19.18.04.mp4`, and `RecordingGif` gives `… .gif`.
- [ ] Output always uses ASCII digits, the 24-hour clock, and the Gregorian calendar, whatever `CultureInfo.CurrentCulture` is. The time zone is injectable and defaults to `TimeZoneInfo.Local`.
- [ ] Collisions produce ` (2)`, ` (3)`, … before the extension and fill the first gap. A name that differs only in letter case counts as a collision.
- [ ] `SaveFolderPolicy.DefaultFolder` gives `<Pictures>\Screenshots`, and `SaveFolder.DefaultPath` on Windows resolves to `%USERPROFILE%\Pictures\Screenshots` (or the redirected Pictures folder).
- [ ] `Prepare` creates a missing folder and its missing parents, succeeds silently for an existing writable folder without leaving files behind, and throws `SaveFolderException` with `NotADirectory`, `CannotCreate`, or `NotWritable` and the normalized folder path in the cases above.
- [ ] `UniqueFilePath` returns a path inside the folder where nothing exists yet (no file, directory, or dangling link).
- [ ] `FailureTitle` shows the full path (never `~`), and `FailureDetails` includes the system error.
- [ ] `ExplorerReveal` and `SaveFolder` compile in `Snapper.Windows`. `Snapper.Core` references no WPF, WinForms, WinRT, or interop types.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed.

## Edge cases

- **Two captures in the same second:** the second one gets ` (2)`.
- **Midnight and single digits:** `00.00.00`, `2027-01-05 at 03.04.05`.
- **Time zone and DST:** wall-clock time in the injected zone. Paris in July is UTC+2. Ambiguous or skipped DST hours give whatever `ConvertTime` returns.
- **Exotic current culture** (`th-TH` Buddhist calendar, `ar-SA`, `en-US` 12-hour): unchanged output.
- **Different letter case on disk** (`screenshot 2026-11-02 at 19.18.04.PNG` exists): treated as taken, so the result gets ` (2)`.
- **A directory or a dangling symlink** with the candidate name: taken.
- **Gaps** (`X.png` and `X (3).png` exist): returns `X (2).png`.
- **A different extension** with the same base name: not a collision.
- **Save path is an existing file:** `NotADirectory`, and nothing is created.
- **Folder is a junction or a symlink to a directory** (e.g. a redirected Pictures folder): accepted.
- **Disconnected drive or offline network share:** `CannotCreate`, with the system message in the details.
- **Controlled Folder Access, or an ACL that denies write:** `NotWritable`. The probe file is never left behind.
- **The folder's read-only attribute is set** (Windows ignores it for folders): writes succeed, so no error.
- **Pictures known folder missing or redirected to OneDrive:** `DoNotVerify` returns the configured path. `Prepare` creates the folder on first save. An empty result falls back to `%USERPROFILE%\Pictures\Screenshots`.
- **Long paths (> 260 characters):** .NET handles them. Nothing here truncates paths.
- **Trailing separator or `..` in the configured path:** normalized before checking and display. A root such as `D:\` keeps its backslash.
- **Relative path passed to `Prepare`:** `ArgumentException` (a programming error; 0027 never stores one).
- **Check-then-write race:** `UniqueFilePath` doesn't reserve the name. Writers use `FileMode.CreateNew` and retry once with a fresh name (0031, 0034), as in `ScreenshotFileWriter.swift:5-23`.
- **Windows' own Win+Print Screen files** (`Screenshot 2026-11-02 191804.png`) in the same folder: different names, so no collision.
- **`RevealFile` on a file that was just deleted, or with a shell failure:** returns `false`, no crash and no dialog.

## Test plan

- **Unit tests** (xUnit, in `windows/tests/Snapper.Core.Tests/`). Time zones: use `TimeZoneInfo.Utc` and `TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")`. .NET accepts IANA IDs on Windows (ICU) and macOS. Never depend on the machine's zone or culture. Tests that change `CultureInfo.CurrentCulture` restore it in `finally`.
  - `FileNamerTests.cs`. The first 15 cases port `SnapperTests/FileNamerTests.swift:21-122`, with the Swift `.recording` changed to `CaptureKind.RecordingMp4`:
    1. `ScreenshotNameFormat`: Paris 2026-11-02 19:18:04 → `Screenshot 2026-11-02 at 19.18.04.png` (exact).
    2. `RecordingNameFormat`: → `Recording 2026-11-02 at 19.18.04.mp4`.
    3. `GifRecordingNameFormat`: `RecordingGif` → `Recording 2026-11-02 at 19.18.04.gif`.
    4. `BaseNameHasNoExtension`: → `Screenshot 2026-11-02 at 19.18.04`.
    5. `ZeroPadsSingleDigitComponents`: 2027-01-05 03:04:05 → `… 2027-01-05 at 03.04.05.png`.
    6. `Midnight`: 2026-11-03 00:00:00 → `… at 00.00.00.png`.
    7. `UsesTwentyFourHourClock`: 23:59:59 → `23.59.59`, even with `CurrentCulture = en-US`.
    8. `RespectsInjectedTimeZone`: 2026-11-02T18:18:04Z → `19.18.04` (Paris) and `18.18.04` (UTC). 2026-11-02T23:30:00Z in Paris → `2026-11-03 at 00.30.00`.
    9. `ObservesDaylightSavingTime`: 2026-07-02T18:18:04Z in Paris → `Screenshot 2026-07-02 at 20.18.04.png`.
    10. `DropsSubSecondPrecision`: 19:18:04.999 → `19.18.04`.
    11. `IgnoresCurrentCulture`: with `CurrentCulture` set to `th-TH`, `ar-SA`, and `fa-IR` in turn, the output equals case 1 exactly (Gregorian year 2026, ASCII digits).
    12. `NoCollisionReturnsPlainName`: `exists` always false → the plain name.
    13. `CollisionSequence`: adding each result to the set gives `(2)`, `(3)`, `(4)` in turn.
    14. `CollisionFillsFirstGap`: the set holds `X.png` and `X (3).png` → `X (2).png`.
    15. `CollisionPreservesExtension`: `RecordingMp4` with `R.mp4` taken → `R (2).mp4`. `RecordingGif` with `R.gif` taken → `R (2).gif`.
    16. `DifferentExtensionIsNotACollision`: only the `.mp4` exists → the `.png` name has no suffix.
    17. `PredicateReceivesFullFileNames`: the recorded arguments are exactly `[X.png, X (2).png]` when the first exists.
    18. `CaseInsensitiveSetCollides` (Windows-specific): a `HashSet` with `FileNamer.NameComparer` holding `SCREENSHOT 2026-11-02 AT 19.18.04.PNG` → the result is `Screenshot 2026-11-02 at 19.18.04 (2).png`.
    19. `NamesAreValidWindowsFileNames`: for every `CaptureKind`, plain and ` (12)` names contain none of `<>:"/\|?*` and no char < 0x20, don't end with `.` or space, and their stem isn't a reserved device name.
    20. `CaptureKindPrefixesAndExtensions`: `Screenshot/png`, `Recording/mp4`, `Recording/gif`. `(CaptureKind)99` throws `ArgumentOutOfRangeException`.
  - `SaveFolderPolicyTests.cs`, decision tests with a `FakeSaveFolderFileSystem` (a test helper in the same project: case-insensitive dictionary of entries, configurable exceptions for `CreateDirectory`/`ProbeWrite`, and a call log). Paths are built with `Path.Combine(Path.GetTempPath(), …)`, so they are fully qualified on every OS:
    1. `DefaultFolderUsesPictures`: `DefaultFolder(Combine(root,"Pictures"), Combine(root,"Profile"), tmp)` → `Combine(root,"Pictures","Screenshots")`. A trailing separator on the input gives no double separator.
    2. `DefaultFolderFallsBackToProfile`: `pictures = ""` (and `null`) → `Combine(profile,"Pictures","Screenshots")`.
    3. `DefaultFolderFallsBackToTemp`: both empty → `Combine(tmp,"Screenshots")`.
    4. `PrepareCreatesMissingFolder`: entry `Missing` → `CreateDirectory` then `ProbeWrite` called once each, and it returns the normalized path.
    5. `PrepareExistingWritableFolder`: `Directory` → no `CreateDirectory`, one `ProbeWrite`, no throw.
    6. `PrepareFileThrowsNotADirectory`: `File` → `Failure == NotADirectory`, `FolderPath` is the path, and no create or probe calls.
    7. `PrepareCreateFailureThrowsCannotCreate`: `CreateDirectory` throws `UnauthorizedAccessException("denied")` → `CannotCreate`, `InnerException` is that instance, and no probe.
    8. `PrepareProbeFailureThrowsNotWritable`: `ProbeWrite` throws `IOException("blocked")` → `NotWritable` with that inner exception.
    9. `PrepareNormalizesPath`: `Combine(root,"a","..","Screenshots") + separator` → it returns and reports `Combine(root,"Screenshots")`, and the fake saw only the normalized path.
    10. `PrepareRejectsRelativePath`: `"Screenshots"` → `ArgumentException`.
    11. `UniqueFilePathSkipsExisting`: the fake holds `X.png` and `X (2).png` → `Combine(folder, "X (3).png")`.
    12. `UniqueFilePathIsCaseInsensitive`: the fake holds `screenshot … .PNG` → `(2)`.
    13. `UniqueFilePathTreatsDirectoryAsTaken`: a directory entry named `X.png` → `X (2).png`.
    14. `UniqueFilePathPropagatesFolderErrors`: a `File` at the folder path → `NotADirectory`, and `EntryExists` is never asked about candidate names.
    15. `FailureTitleUsesFullPath`: `@"C:\Users\ana\Pictures\Screenshots"` → `Couldn't save to C:\Users\ana\Pictures\Screenshots`. `@"C:\Users\ana\Pictures\Screenshots\"` gives the same result. `@"D:\"` → `Couldn't save to D:\`. `@"\\nas\share\Shots\"` → `Couldn't save to \\nas\share\Shots`. `"/"` stays `/`. None contain `~`.
    16. `FailureDetailsPerFailure`: `CannotCreate` with inner "Access is denied." → `Access is denied.`, and without inner → `The save folder couldn't be created.` `NotWritable` without inner → `You don't have permission to write to this folder.`, and with inner "Blocked." → that sentence + ` Blocked.` `NotADirectory` → `The save folder path is a file, not a folder.` `Message` equals `FailureDetails`.
  - `PhysicalSaveFolderFileSystemTests.cs`, against a real unique temp root (`Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))`, deleted in `Dispose`, with permissions restored first). These are the ports of `SnapperTests/SaveFolderTests.swift:23-138`:
    1. `CreatesMissingNestedFolder`: `root/a/b/Screenshots` → it exists as a directory afterwards.
    2. `ExistingFolderIsUntouched`: prepare twice. A sentinel file is still there, and the folder holds exactly that one entry (the probe file is gone).
    3. `FileAtPathThrowsNotADirectory`.
    4. `ReadOnlyFolderThrowsNotWritable`: Unix only. `File.SetUnixFileMode(folder, UserRead|UserExecute)` → `NotWritable`.
    5. `ReadOnlyParentThrowsCannotCreate`: Unix only. Root set to read+execute, `root/Screenshots` → `CannotCreate`.
    6. `UniqueFilePathInEmptyFolder`: returns `folder/Screenshot … .png`, which doesn't exist, and the folder was created.
    7. `UniqueFilePathSkipsExistingFiles`: real `X.png`, `X (2).png` → `X (3).png`.
    8. `DirectorySymlinkIsAccepted`: `Directory.CreateSymbolicLink(link, root)` → `Prepare(link)` succeeds and returns `link`.
    9. `DanglingSymlinkIsACollision`: `File.CreateSymbolicLink(plainName, root/missing)` → `(2)`.

    Tests 4 and 5 are skipped on Windows (Windows ACLs aren't set up in unit tests; 0031's manual check covers them) and when running as root (`Environment.UserName == "root"`). Tests 8 and 9 are skipped when creating the link throws `UnauthorizedAccessException`/`IOException` (Windows without Developer Mode). Use the dynamic skip of the xUnit version pinned in 0025 (`Assert.Skip`/`SkipWhen` in xUnit v3) with a reason message. If that version has no dynamic skip, return early with a comment.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` both pass (both run on macOS).
- **Manual verification** on a Windows 11 machine. Nothing calls this code yet, so the end-to-end checks (first screenshot creates `Pictures\Screenshots`, two quick screenshots give ` (2)`, an unwritable folder shows "Couldn't save to C:\…" with **Choose Folder…**, File Explorer selects a new recording) belong to 0031, 0033, and 0034. For this spec:
  1. Build and run `Snapper.Windows`. The tray icon and menu behave exactly as after 0025, and no `Pictures\Screenshots` folder is created at launch (if it didn't exist before).
  2. Optional, for the implementer: in a scratch Debug-only call (not committed), call `ExplorerReveal.RevealFile` on an existing file in `Pictures`. File Explorer opens that folder with the file selected. Calling it on a nonexistent path returns `false`, with no dialog and no crash. Record the result in the verification status. If it isn't run, mark it `[ ] not yet run`.

## Out of scope

- The "Couldn't save to" alert UI, **Choose Folder…**, Cancel, retry-once, copying to the clipboard with *Both*, writing PNG files with `FileMode.CreateNew`, and classifying write exceptions: 0031 (`Support/ErrorAlerts`, `Capture/ScreenshotOutputPlan`).
- Storing the save folder and its injected default, and `RecordingFormat` with its mapping to `CaptureKind`: 0027.
- The Settings folder picker and "Open folder" button: 0033.
- The recording output path, Explorer reveal after Stop, and write-failure handling: 0034. `.gif` output: 0046.
- The annotation Save dialog's default folder and name (`AnnotationSaveDefaults`): 0043.
- An onboarding window, TCC, `~` abbreviation, and Finder: macOS-only and not applicable (PRD §13.5, §13.10).
