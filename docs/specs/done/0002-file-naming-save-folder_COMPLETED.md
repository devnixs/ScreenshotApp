# 0002 — Add pure file naming and save-folder resolution

> Source: docs/PRD.md §9 (Files and naming), §6 step 3, §7 steps 1 and 5, §12 ("The default save folder is created when it is missing")
> Depends on: 0001-xcodegen-app-shell

## Context

No app code exists yet. 0001 creates the XcodeGen project, the `Snapper` and `SnapperTests` targets, and the app shell. Nothing in the app names or saves files yet.

`docs/PRD.md §9` defines the behavior this spec delivers as pure, testable building blocks:

- Default save folder: `~/Documents/Screenshots`, "created on first use if it doesn't exist". Screenshots and recordings share the same folder.
- File names: `Screenshot 2026-11-02 at 19.18.04.png` and `Recording 2026-11-02 at 19.18.04.mp4`. "Screenshots use the capture time, and recordings use the time the recording started."
- "On a name collision, ` (2)`, ` (3)`, and so on are added to the name."
- "If the save folder can't be created or isn't writable, an alert says 'Couldn't save to <folder>' and offers a **Choose Folder…** button." This spec supplies only the **typed error** that carries the folder. The alert UI belongs to 0008 (`Support/ErrorAlerts.swift`).

Callers that arrive later: 0008 (screenshot to file), 0011 (recording output URL), and 0020 (default name in the annotation save panel). 0003 (`AppSettings.saveFolderURL`) uses `SaveFolder.defaultURL` as its default value.

## User story

As a Snapper user, every screenshot and recording I save gets a predictable, sortable, macOS-style name in my save folder. The name never overwrites an existing file, and the default folder appears by itself the first time I save something.

## Scope & changes

All code in this spec is **pure Swift / Foundation**. It uses no AppKit, no ScreenCaptureKit and no `@MainActor`. Every type is a value type that is `Sendable` and can be called from any isolation domain.

### 1. `Snapper/Support/CaptureKind.swift` (pure)

```swift
enum CaptureKind: Sendable, Equatable, CaseIterable {
    case screenshot   // prefix "Screenshot", extension "png"
    case recording    // prefix "Recording",  extension "mp4"

    var namePrefix: String { get }      // "Screenshot" / "Recording"
    var fileExtension: String { get }   // "png" / "mp4" (lowercase, no dot)
}
```

### 2. `Snapper/Support/FileNamer.swift` (pure)

```swift
struct FileNamer: Sendable {
    init(timeZone: TimeZone = .current)

    /// "Screenshot 2026-11-02 at 19.18.04", with no extension and no suffix.
    func baseName(for kind: CaptureKind, date: Date) -> String

    /// "Screenshot 2026-11-02 at 19.18.04.png"
    func fileName(for kind: CaptureKind, date: Date) -> String

    /// The first name in the sequence
    ///   "<base>.<ext>", "<base> (2).<ext>", "<base> (3).<ext>", …
    /// for which `exists(name)` returns false.
    func uniqueFileName(for kind: CaptureKind, date: Date,
                        exists: (String) -> Bool) -> String
}
```

- **Format:** `<prefix> yyyy-MM-dd at HH.mm.ss`. All fields are zero-padded: 4-digit year, then 2-digit month, day, hour (24-hour clock, `00`–`23`), minute and second. Separators are exactly: a space after the prefix, `-` within the date, ` at `, and `.` within the time. There are no milliseconds.
- **Locale and calendar are fixed, and the time zone is injectable:** use a `Calendar(identifier: .gregorian)` whose `timeZone` is the injected `TimeZone` (default `.current`, the user's current time zone). Extract `year, month, day, hour, minute, second` with `calendar.dateComponents(in:from:)` or `dateComponents(_:from:)`, then format them with ASCII digits. One way is `String(format: "%04d-%02d-%02d at %02d.%02d.%02d", …)` using the `en_US_POSIX` locale or the `nil` locale. Prefer this over a `DateFormatter`, because `DateFormatter` isn't `Sendable`. If you use a `DateFormatter` anyway, it must set `locale = Locale(identifier: "en_US_POSIX")`, `calendar = .gregorian`, the injected `timeZone`, and `dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"`, and it must be created per call or kept private behind a `Sendable` wrapper. The user's 12/24-hour preference, numbering system and non-Gregorian calendar must **never** affect the output.
- **Collision suffix:** ` (N)` (a space, then the parenthesized integer starting at 2) goes **before** the extension: `Screenshot 2026-11-02 at 19.18.04 (2).png`. The extension is always preserved. `exists` receives the full file name, extension included, so a `.mp4` with the same base name never collides with a `.png`.
- The loop has no upper bound. In production the predicate is backed by a finite directory listing, so the loop always ends. Tests use finite sets.
- Which `Date` to pass is the caller's job: the capture time for screenshots, the start time for recordings (PRD §9). Document this in the doc comment. Do **not** call `Date()` inside `FileNamer`.

### 3. `Snapper/Support/SaveFolder.swift` (Foundation file-system glue, testable against a temp directory)

```swift
enum SaveFolderError: Error, Equatable, Sendable {
    case cannotCreate(URL)    // the folder is missing and createDirectory failed
    case notWritable(URL)     // the folder exists and is a directory, but isn't writable
    case notADirectory(URL)   // something that isn't a directory exists at that path

    /// The folder the error refers to, for "Couldn't save to <folder>" (alert built in 0008).
    var folderURL: URL { get }
}

struct SaveFolder: Sendable {
    /// ~/Documents/Screenshots
    static var defaultURL: URL { get }

    let url: URL
    init(url: URL)

    /// Makes sure the folder exists (creating it with intermediate directories if it is missing),
    /// is a directory, and is writable. Returns the standardized folder URL.
    @discardableResult
    func prepare() throws(SaveFolderError) -> URL

    /// prepare() + FileNamer.uniqueFileName, checked against the folder's real contents.
    func uniqueFileURL(for kind: CaptureKind, date: Date,
                       namer: FileNamer = FileNamer()) throws(SaveFolderError) -> URL
}
```

- `defaultURL` is `URL.documentsDirectory.appending(path: "Screenshots", directoryHint: .isDirectory)`. The app isn't sandboxed (AGENTS.md), so this resolves to the real `~/Documents/Screenshots`.
- **The folder URL comes from the caller.** `SaveFolder` never reads `UserDefaults` or `AppSettings`. In later specs, callers pass `AppSettings.saveFolderURL` (0003).
- `prepare()` steps, in order:
  1. `FileManager.default.fileExists(atPath:isDirectory:)`. This follows symlinks, so a symlink to a directory counts as a directory.
  2. If something exists but isn't a directory, throw `.notADirectory(url)`.
  3. If nothing exists, call `createDirectory(at:withIntermediateDirectories: true)`. If that throws, throw `.cannotCreate(url)`. This applies to any configured folder, not only the default, which matches the PRD's "can't be created" wording. Creation happens **on use** (when `prepare()` is called at save time), never at launch.
  4. If `FileManager.default.isWritableFile(atPath:)` is false, throw `.notWritable(url)`.
- `uniqueFileURL` calls `prepare()`, then builds the name with `namer.uniqueFileName(for:date:exists:)`. The `exists` predicate checks `folder.appending(path: name)` on disk. Use a check that also sees dangling symlinks, such as `(try? FileManager.default.attributesOfItem(atPath:)) != nil`, which doesn't traverse the final symlink. The function returns `folder.appending(path: name, directoryHint: .notDirectory)`.
- `uniqueFileURL` doesn't reserve the name. Writers in later specs handle the tiny check-then-write race. 0008 writes PNG data with `.withoutOverwriting`, and 0011 gives `SCRecordingOutput` a URL that must not exist yet. Mention this in a doc comment on `uniqueFileURL`.
- The error enum stays small and Equatable: underlying `NSError`s are dropped on purpose, because the PRD alert shows only the folder. Log the underlying error with `os.Logger` (subsystem = bundle id, category `"SaveFolder"`) before throwing, so failures can still be diagnosed.

### 4. Project

- New source files go in `Snapper/Support/` and test files in `SnapperTests/`. Run `xcodegen generate` so the generated project picks them up. Don't edit `project.pbxproj`. No `project.yml` change should be needed if 0001 declares the source folders by directory. If it lists files one by one, add these files there.
- No new dependency, no Info.plist key, no entitlement.
- **No callers are wired in this spec.** The app's behavior doesn't change, and it still builds and runs as 0001 left it.
- No PRD change. This spec only implements §9 as written.

## Acceptance criteria

- [ ] `FileNamer().fileName(for: .screenshot, date: d)` returns `Screenshot 2026-11-02 at 19.18.04.png` for d = 2026-11-02 19:18:04 in the injected time zone. `.recording` returns `Recording 2026-11-02 at 19.18.04.mp4`.
- [ ] Output always uses ASCII digits, the 24-hour clock and the Gregorian calendar, whatever the user's locale or calendar settings.
- [ ] The time zone is injectable. The default is the user's current time zone.
- [ ] Collisions produce ` (2)`, ` (3)`, … before the extension, and the first free name in that sequence is returned.
- [ ] `SaveFolder.defaultURL` is `~/Documents/Screenshots`.
- [ ] `prepare()` creates a missing folder, including missing intermediate folders. It succeeds silently for an existing writable folder. It throws `.notADirectory`, `.notWritable` or `.cannotCreate` with the folder URL in the cases listed above.
- [ ] `uniqueFileURL` returns a URL inside the folder whose file doesn't exist yet. It never returns the URL of an existing file.
- [ ] None of these types import AppKit or ScreenCaptureKit, and none are `@MainActor`.
- [ ] `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` succeed.

## Edge cases

- **Two captures in the same second:** the second one gets ` (2)`. Sub-second precision is never shown.
- **Midnight and single-digit components:** `00.00.00`, `2027-01-05 at 03.04.05`. Every component stays zero-padded.
- **Time zone and DST:** the name shows wall-clock time in the injected zone. The same `Date` yields different names in different zones. Skipped or repeated DST hours are of no concern: the name is whatever the calendar returns for that instant.
- **User locale with 12-hour time, Arabic-Indic digits, or a Buddhist/Japanese calendar:** the output is unchanged, because the locale and calendar are fixed.
- **A collision only with a different extension** (`Screenshot … .mp4` exists while saving a `.png`): no suffix.
- **Gaps in the sequence** (`X.png` and `X (3).png` exist, `X (2).png` doesn't): returns `X (2).png`. This is the first free name, not "max + 1".
- **The file system is case-insensitive (APFS default):** handled by checking existence on disk rather than comparing strings.
- **Save path is an existing regular file:** `.notADirectory`.
- **Folder exists but is read-only:** `.notWritable`.
- **Parent is read-only, so the folder can't be created:** `.cannotCreate`.
- **Configured folder was deleted** (e.g. a custom folder, or the user removed `~/Documents/Screenshots`): it is recreated on the next save.
- **Folder URL with a trailing slash or `..` components:** standardize with `url.standardizedFileURL` before checking.
- **Dangling symlink with the candidate name:** treated as existing, so it gets the next suffix and is never overwritten.

## Test plan

- **Unit tests**, using the test framework 0001 set up (Swift Testing `@Test` or XCTest):
  - `SnapperTests/FileNamerTests.swift`. Every test injects `TimeZone(identifier: "Europe/Paris")` or `"UTC"` and builds dates from explicit `DateComponents` in that zone, so they never depend on the machine's time zone.
    1. `screenshotNameFormat`: 2026-11-02 19:18:04 → `Screenshot 2026-11-02 at 19.18.04.png` (exact string equality).
    2. `recordingNameFormat`: same date → `Recording 2026-11-02 at 19.18.04.mp4`.
    3. `baseNameHasNoExtension`: `baseName` → `Screenshot 2026-11-02 at 19.18.04`.
    4. `zeroPadsSingleDigitComponents`: 2027-01-05 03:04:05 → `… 2027-01-05 at 03.04.05.png`.
    5. `midnight`: 2026-11-03 00:00:00 → `… 2026-11-03 at 00.00.00.png`, which is not `24.00.00` or `12.00.00`.
    6. `usesTwentyFourHourClock`: 23:59:59 → `23.59.59`.
    7. `respectsInjectedTimeZone`: the same `Date` (2026-11-02T18:18:04Z) gives `19.18.04` with Europe/Paris and `18.18.04` with UTC. A date near midnight UTC also rolls the **date** part (e.g. 2026-11-02T23:30:00Z → `2026-11-03 at 00.30.00` in Paris).
    8. `dropsSubSecondPrecision`: 19:18:04.999 → `19.18.04`.
    9. `noCollisionReturnsPlainName`: `exists` always false → the plain name.
    10. `collisionSequence`: the set holds `X.png` → `X (2).png`. The set holds `X.png` and `X (2).png` → `X (3).png`. The set holds `X.png`, `X (2).png` and `X (3).png` → `X (4).png`.
    11. `collisionFillsFirstGap`: the set holds `X.png` and `X (3).png` → `X (2).png`.
    12. `collisionPreservesExtension`: for `.recording`, the set holds `R.mp4` → `R (2).mp4`. The result ends with `.mp4` and contains no `.png`.
    13. `differentExtensionIsNotACollision`: the set holds only `Screenshot … .mp4` → the `.png` name has no suffix.
    14. `predicateReceivesFullFileNames`: record the arguments passed to `exists` and assert they are `[X.png, X (2).png]` when the first one exists.
  - `SnapperTests/SaveFolderTests.swift`. Each test makes a unique temp root (`FileManager.default.temporaryDirectory/UUID`) and deletes it in teardown. Restore permissions before deleting.
    1. `defaultURLIsDocumentsScreenshots`: `SaveFolder.defaultURL.lastPathComponent == "Screenshots"` and `deletingLastPathComponent() == URL.documentsDirectory` (compare standardized paths).
    2. `createsMissingNestedFolder`: `root/a/b/Screenshots` doesn't exist. `prepare()` succeeds, and the path then exists as a directory.
    3. `existingFolderIsOK`: a pre-created directory. `prepare()` succeeds twice in a row, and its contents are untouched (a sentinel file is still there).
    4. `pathIsAFileThrowsNotADirectory`: write a file at `root/Screenshots`. `prepare()` throws `.notADirectory(url)`.
    5. `readOnlyFolderThrowsNotWritable`: create the folder, set POSIX permissions `0o555`. `prepare()` throws `.notWritable(url)`.
    6. `readOnlyParentThrowsCannotCreate`: `root` set to `0o555`. `prepare()` on `root/Screenshots` throws `.cannotCreate(url)`.
    7. `uniqueFileURLInEmptyFolder`: returns `folder/Screenshot … .png`. The file doesn't exist yet, and the folder was created.
    8. `uniqueFileURLSkipsExistingFiles`: pre-create `X.png` and `X (2).png` on disk. The result is `X (3).png`.
    9. `uniqueFileURLPropagatesFolderErrors`: if the folder path is a file, `uniqueFileURL` throws `.notADirectory`.
    10. `errorExposesFolderURL`: `folderURL` returns the associated URL for all three cases.

    Tests 5 and 6 depend on POSIX permissions being enforced. They are under a normal user account. If the test process runs as root (`geteuid() == 0`), skip those two tests and say why in the skip message.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test`. Both must pass.
- **Manual verification:** none is required for this spec, and that's intentional. It adds only pure naming logic and Foundation file-system calls. The unit tests cover all of it against a real temp directory. Nothing touches permissions, hotkeys, overlays, capture or UI, and nothing in the app calls this code yet. As a sanity check, launch the app after building and confirm it still behaves as 0001 left it (menu bar icon present, no Dock icon). The end-to-end checks (first screenshot creates `~/Documents/Screenshots`, two quick screenshots produce a ` (2)` file, an unwritable folder shows "Couldn't save to <folder>") belong to the manual verification of 0008 and 0011.

## Out of scope

- The "Couldn't save to <folder>" alert, the **Choose Folder…** button, and copying to the clipboard anyway with the *Both* destination: 0008 (`Support/ErrorAlerts.swift`, `ScreenshotOutputPlan`).
- Storing the user's save folder or the destination setting: 0003 (`AppSettings`). The folder picker UI: 0010.
- Writing PNG data and recording files, and revealing them in Finder: 0008 and 0011.
- The annotation save panel's default folder and name: 0020, which reuses `FileNamer.fileName(for: .screenshot, date:)` and `AppSettings.saveFolderURL`.
- Security-scoped bookmarks: not needed, because the app isn't sandboxed (PRD §2, §3).

## Verification status

- Verified 2026-11-02: `xcodegen generate` succeeded.
- `xcodebuild -scheme Snapper -configuration Debug build`: **BUILD SUCCEEDED**.
- `xcodebuild -scheme Snapper test`: **TEST SUCCEEDED**; 46 tests in 7 suites passed, including 28 new file-naming/save-folder tests. Both POSIX permission tests ran under the normal user account; none were skipped.
- No PRD or AGENTS.md change was needed: this implements the specified building blocks without wiring callers or changing app behavior.
- Required manual verification: none for this spec. Capture/save integration checks remain in 0008 and 0011.
- [ ] not yet run by user — Optional app-shell sanity check: launch the built app and confirm the menu bar icon is present and no Dock icon appears. The agent did not launch the app or verify GUI behavior.
