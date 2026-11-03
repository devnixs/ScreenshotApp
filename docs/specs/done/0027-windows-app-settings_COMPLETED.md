# 0027 — Add the Windows typed AppSettings persisted as JSON, with KeyChord, RgbaColor, and StrokeWidth

> Source: docs/PRD.md §10 (General / Shortcuts / Recording settings and defaults), §13.9 (Windows storage: one typed settings type, JSON at `%APPDATA%\Snapper\settings.json`, shortcuts included, per-value fallback), §13.8 (default save folder), §13.2 (shortcut display `Ctrl+Shift+4`, Ctrl+Shift+4/5/6 defaults), §8.2 (last-used color red / stroke width M, remembered between sessions), §7 item 6 (Format MP4/GIF, MP4 quality High, GIF quality Medium); AGENTS.md "Windows app (`windows/`)" → "Architecture rules (Windows)"
> Depends on: 0025-windows-solution-tray-shell (solution, Core/test/app projects, `AppCoordinator`), 0026-windows-file-naming-save-folder (Windows `Support/SaveFolder` default path)

## Context

Not yet implemented. After 0025 and 0026 the Windows app has a tray icon with stub actions (`windows/src/Snapper.Windows/App/AppCoordinator.cs`) and the save-folder helpers, but no preferences. Every later Windows spec needs persisted user choices, so this spec defines the **complete settings schema** once. Later specs only read and write properties; they don't add keys.

The macOS reference implementation:

- `Snapper/Settings/AppSettings.swift:4-95`: one `@Observable @MainActor` class. Keys are defined once (`:6-9`) and defaults once (`:11-20`). Each property writes itself on `didSet` (`:24-60`). `init` loads every value through per-value decoders that fall back to the default (`:62-94`) and **writes nothing**.
- `Snapper/Settings/ScreenshotDestination.swift:1-14`: `file`/`clipboard`/`both`, `savesToFile`, `copiesToClipboard`, `title`.
- `Snapper/Support/KeyChord.swift:1-20`: a key code plus masked modifiers, with a display string.
- `Snapper/Annotate/Model/RGBAColor.swift:3-82`: sRGB doubles in `0...1`, a clamping init (`:9-18`), a strict decoder that rejects missing, non-finite, or out-of-range values (`:24-40`), and presets in toolbar order (`:65-81`).
- `Snapper/Annotate/Model/StrokeWidth.swift:3-23`: `s`/`m`/`l` → 2/4/8 points and 16/24/36 pt text.
- macOS todo spec `docs/specs/todo/0023-recording-export-mp4-quality_TODO.md:27-29` adds `recordingFormat` (`mp4`/`gif`, default `mp4`), `mp4Quality` (default `high`), and `gifQuality` (default `medium`). `docs/specs/todo/0024-recording-gif-format_TODO.md:10` relies on them.
- The macOS spec this ports: `docs/specs/done/0003-app-settings_COMPLETED.md`.
- macOS tests to port: `SnapperTests/AppSettingsTests.swift:15-150`, `SnapperTests/KeyChordTests.swift:5-19`, `SnapperTests/RGBAColorTests.swift:7-67`, `SnapperTests/StrokeWidthTests.swift:5-10`.

**Windows differences (PRD §13):**
- Storage is a JSON file, not `UserDefaults`. Shortcuts are stored **in the same file**, because there is no KeyboardShortcuts library.
- The default save folder is `%USERPROFILE%\Pictures\Screenshots` (§13.8), and 0026 resolves it.
- There is no onboarding window (§13.10), so the macOS `onboardingDismissed` key is **not ported**.
- `RgbaColor` has no CoreGraphics bridge (`RGBAColor.swift:42-52` doesn't apply). It gets byte conversions instead, for WPF `Color` and the Win32 `ChooseColor` `COLORREF` (used by 0040).
- KeyChord uses Windows virtual-key codes and `MOD_*` modifier flags, not Carbon key codes, and displays as `Ctrl+Shift+4` (§13.2).

## User story

As a Snapper user on Windows, my choices persist across exits and relaunches: save folder, screenshot destination, shutter sound, launch at login, the three shortcuts, recording format and quality, microphone, and the last annotation color and stroke width. A fresh install, a missing file, or a damaged file gives me the PRD defaults. A single bad value in a hand-edited file resets only that value.

## Scope & changes

All new types live in `Snapper.Core` (`net10.0`, pure, no Windows/WPF/WinRT/interop references) with xUnit tests, except the one wiring bullet at the end. Namespaces follow the folders. JSON uses **System.Text.Json** (in-box, so no new package). Use the reader/writer APIs (`JsonDocument` + `Utf8JsonWriter`) with hand-written property and value mappings. Don't use reflection-based `JsonSerializer` with attribute-driven enum naming. This pins the on-disk names explicitly, makes the per-value fallback natural, and stays trim- and single-file-safe for 0047.

### Pure value types (Core, unit-tested)

- **`windows/src/Snapper.Core/Annotate/Model/RgbaColor.cs`**: a port of `RGBAColor.swift`. `public readonly record struct RgbaColor` with `double R, G, B, Alpha`, each in `0…1`, sRGB.
  - The public constructor `RgbaColor(double red, double green, double blue, double alpha = 1)` **clamps** each component to `0…1`. A non-finite component becomes `0`, or `1` for alpha. This is a port of `RGBAColor.swift:9-18`. A `default(RgbaColor)` is transparent black, which is valid.
  - `public static bool TryCreateStrict(double r, double g, double b, double a, out RgbaColor color)` returns `false` when any component is non-finite or outside `0…1`. Storage uses it, so stored garbage falls back to the default instead of being clamped into another color (port of `RGBAColor.swift:24-40`).
  - `public static RgbaColor FromBytes(byte r, byte g, byte b, byte a = 255)` returns component `/ 255`. `public (byte R, byte G, byte B, byte A) ToBytes()` returns each component `Math.Round(c * 255, MidpointRounding.AwayFromZero)`. 0040 uses these for WPF `Color` and `COLORREF`.
  - `public enum ColorPreset { Red, Orange, Yellow, Green, Blue, Purple, Black, White }` in **exactly this order** (toolbar swatch order, §8.2). `ColorPresetExtensions.Color(this ColorPreset)` and `DisplayName(this ColorPreset)` ("Red" … "White"). Statics `RgbaColor.Red` … `RgbaColor.White`, plus `public static IReadOnlyList<RgbaColor> Presets` in preset order. The exact values, written as `n / 255.0` so tests compare exactly:

    | Preset | Hex | Red | Green | Blue |
    |---|---|---|---|---|
    | Red | `#FF3B30` | 255/255 | 59/255 | 48/255 |
    | Orange | `#FF9500` | 255/255 | 149/255 | 0 |
    | Yellow | `#FFCC00` | 255/255 | 204/255 | 0 |
    | Green | `#34C759` | 52/255 | 199/255 | 89/255 |
    | Blue | `#007AFF` | 0 | 122/255 | 255/255 |
    | Purple | `#AF52DE` | 175/255 | 82/255 | 222/255 |
    | Black | `#000000` | 0 | 0 | 0 |
    | White | `#FFFFFF` | 255/255 | 255/255 | 255/255 |

  - 0038 extends this file or folder (shape model). This spec adds only what settings and the toolbar defaults need.

- **`windows/src/Snapper.Core/Annotate/Model/StrokeWidth.cs`**: a port of `StrokeWidth.swift`. `public enum StrokeWidth { S, M, L }` (declaration order is the toolbar order). `StrokeWidthExtensions`:
  - `Points(this StrokeWidth)` → `2`, `4`, `8` (image points, §8.2; converting them to image pixels belongs to the document in 0039/0040).
  - `TextPointSize(this StrokeWidth)` → `16`, `24`, `36` (§8.3).
  - `Label(this StrokeWidth)` → `"S"`, `"M"`, `"L"`.

- **`windows/src/Snapper.Core/Settings/ScreenshotDestination.cs`**: `public enum ScreenshotDestination { File, Clipboard, Both }`. Extensions: `SavesToFile` (File, Both), `CopiesToClipboard` (Clipboard, Both), and `Title` → "Save to file", "Copy to clipboard", "Both" (PRD §10 wording, port of `ScreenshotDestination.swift:4-13`).

- **`windows/src/Snapper.Core/Settings/RecordingFormat.cs`**: `public enum RecordingFormat { Mp4, Gif }`. Extensions: `Title` → "MP4", "GIF"; `FileExtension` → "mp4", "gif" (port of macOS 0023 `RecordingFormat`, `docs/specs/todo/0023-recording-export-mp4-quality_TODO.md:27`).

- **`windows/src/Snapper.Core/Settings/Mp4Quality.cs`**: `public enum Mp4Quality { High, Medium, Low }`. **`windows/src/Snapper.Core/Settings/GifQuality.cs`**: `public enum GifQuality { High, Medium, Low }`. Each has a `Title` extension → "High", "Medium", "Low". They are separate types so the compiler keeps the two remembered levels (§10: "MP4 and GIF each remember their own level") from being mixed up. macOS uses one shared `RecordingQuality`, so 0045 and 0046 port its logic per type. This spec adds **no** bitrate or fps math. That belongs to 0045 (`RecordingExportPlan`) and 0046 (`GifConversionPlan`).

- **`windows/src/Snapper.Core/Support/KeyChord.cs`**: the Windows analogue of `KeyChord.swift`.
  - `[Flags] public enum KeyModifiers { None = 0, Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8 }`. The values equal Win32 `MOD_ALT`/`MOD_CONTROL`/`MOD_SHIFT`/`MOD_WIN`, so 0029 can cast them straight to `HOT_KEY_MODIFIERS` for `RegisterHotKey`. (Snapper's `MOD_NOREPEAT` is added by 0029, not stored.)
  - `public readonly struct KeyChord : IEquatable<KeyChord>` with `KeyModifiers Modifiers` and `int VirtualKey`, plus a **private** constructor.
    - `public static bool TryCreate(KeyModifiers modifiers, int virtualKey, out KeyChord chord)` masks `modifiers` to the four known flags. This drops noise such as `0x4000` and is a port of the masking in `KeyChord.swift:6-9`. It returns `false` when `virtualKey` is outside `0x01…0xFE` or is itself a modifier key: `VK_SHIFT 0x10`, `VK_CONTROL 0x11`, `VK_MENU 0x12`, `VK_LWIN 0x5B`, `VK_RWIN 0x5C`, `VK_LSHIFT…VK_RMENU 0xA0…0xA5`. `public static KeyChord Create(...)` throws `ArgumentException` on the same inputs and is used for constants and tests. `==`/`!=` and `GetHashCode` are value-based. `default(KeyChord)` is never stored. Callers only obtain chords through `TryCreate`/`Create`.
    - `public bool IsValidBinding`: `true` when Ctrl **or** Alt is present and Win is **absent** (PRD §13.9: "a binding needs Ctrl or Alt (Shift is allowed in addition) plus one non-modifier key. Combinations with the Windows key are not accepted"). This is the single definition of that rule. 0033's `ShortcutValidation` and this spec's loader both call it. Win stays representable so the recorder in 0033 can recognize a Win combination and reject it.
    - `public string DisplayString` lists the modifiers in the order **Ctrl, Alt, Shift, Win**, then the key name, joined by `+`. Example: `Ctrl+Shift+4` (§13.2: "Shortcuts are displayed as `Ctrl+Shift+4` everywhere").
    - `public static string KeyName(int virtualKey)` uses a fixed table (Core can't call `GetKeyNameText`):
      - `0x30–0x39` → `"0"`–`"9"`, and `0x41–0x5A` → `"A"`–`"Z"`.
      - `0x70–0x87` → `"F1"`–`"F24"`, and `0x60–0x69` → `"Num 0"`–`"Num 9"`.
      - `0x6A` `"Num *"`, `0x6B` `"Num +"`, `0x6D` `"Num -"`, `0x6E` `"Num ."`, `0x6F` `"Num /"`.
      - `0x08` `"Backspace"`, `0x09` `"Tab"`, `0x0D` `"Enter"`, `0x13` `"Pause"`, `0x14` `"Caps Lock"`, `0x1B` `"Esc"`, `0x20` `"Space"`, `0x21` `"Page Up"`, `0x22` `"Page Down"`, `0x23` `"End"`, `0x24` `"Home"`, `0x25` `"Left"`, `0x26` `"Up"`, `0x27` `"Right"`, `0x28` `"Down"`, `0x2C` `"PrtScn"`, `0x2D` `"Insert"`, `0x2E` `"Delete"`.
      - US-layout OEM labels: `0xBA` `";"`, `0xBB` `"="`, `0xBC` `","`, `0xBD` `"-"`, `0xBE` `"."`, `0xBF` `"/"`, `0xC0` `` "`" ``, `0xDB` `"["`, `0xDC` `"\"`, `0xDD` `"]"`, `0xDE` `"'"`.
      - Anything else → `"Key 0xNN"` (two uppercase hex digits), the analogue of macOS `"Key 99"`.

      If a layout-aware name is ever wanted, the Windows layer can add it later. The table is the stored-and-displayed fallback.

### Settings store (Core, unit-tested)

- **`windows/src/Snapper.Core/Settings/SettingsValues.cs`**: `public sealed record SettingsValues`, an immutable snapshot of every persisted value. It is the unit that `ISettingsStore` loads and saves. Its properties, in this order, are the **complete schema**. Each is owned by and later used by the spec in the last column:

  | Property | Type | Default | JSON key | JSON form | Used by |
  |---|---|---|---|---|---|
  | `LaunchAtLogin` | `bool` | `false` | `launchAtLogin` | `true`/`false` | 0033 (mirror only, see below) |
  | `SaveFolder` | `string?` | `null` = "use the default folder" | `saveFolder` | string, **omitted** when `null` | 0031, 0033, 0034, 0043 |
  | `ScreenshotDestination` | `ScreenshotDestination` | `Both` | `screenshotDestination` | `"file"`/`"clipboard"`/`"both"` | 0031, 0033 |
  | `PlayShutterSound` | `bool` | `true` | `playShutterSound` | `true`/`false` | 0031, 0040 |
  | `RecordMicrophone` | `bool` | `false` | `recordMicrophone` | `true`/`false` | 0037, 0046 |
  | `RecordingFormat` | `RecordingFormat` | `Mp4` | `recordingFormat` | `"mp4"`/`"gif"` | 0045, 0046 |
  | `Mp4Quality` | `Mp4Quality` | `High` | `mp4Quality` | `"high"`/`"medium"`/`"low"` | 0045 |
  | `GifQuality` | `GifQuality` | `Medium` | `gifQuality` | `"high"`/`"medium"`/`"low"` | 0046 |
  | `AnnotationColor` | `RgbaColor` | `RgbaColor.Red` (`#FF3B30`) | `annotationColor` | `{"red":…,"green":…,"blue":…,"alpha":…}` | 0040, 0041 |
  | `AnnotationStrokeWidth` | `StrokeWidth` | `M` | `annotationStrokeWidth` | `"s"`/`"m"`/`"l"` | 0040, 0041 |
  | `ScreenshotShortcut` | `KeyChord?` | Ctrl+Shift+4 (`0x34`) | `shortcuts.screenshot` | chord object, or `null` = cleared | 0029, 0033 |
  | `RecordShortcut` | `KeyChord?` | Ctrl+Shift+5 (`0x35`) | `shortcuts.record` | chord object, or `null` = cleared | 0029, 0033 |
  | `AnnotateShortcut` | `KeyChord?` | Ctrl+Shift+6 (`0x36`) | `shortcuts.annotate` | chord object, or `null` = cleared | 0029, 0033 |

  - `public static SettingsValues Defaults { get; }` builds exactly the table above. It is the **one place** the defaults are defined, except the default save-folder *path*, which 0026 owns (see `AppSettings`).
  - A chord object is `{"modifiers":["Ctrl","Shift"],"key":52}`. `modifiers` lists the names `"Ctrl"`, `"Alt"`, `"Shift"`, `"Win"` in that canonical order when written. On read, order doesn't matter, but every element must be one of those four strings with no repeats. `key` is the virtual-key code as an integer. Shortcuts may be cleared, which mirrors macOS, where a KeyboardShortcuts recorder can be cleared (`docs/specs/done/0010-settings-window_COMPLETED.md:158`). That is why `null` (cleared) and an absent key (default) are different.
  - A complete file as written by this spec, pretty-printed with 2-space indentation, LF line endings, and UTF-8 without a BOM:

    ```json
    {
      "version": 1,
      "launchAtLogin": false,
      "saveFolder": "D:\\Shots",
      "screenshotDestination": "both",
      "playShutterSound": true,
      "recordMicrophone": false,
      "recordingFormat": "mp4",
      "mp4Quality": "high",
      "gifQuality": "medium",
      "annotationColor": { "red": 1, "green": 0.23137254901960785, "blue": 0.18823529411764706, "alpha": 1 },
      "annotationStrokeWidth": "m",
      "shortcuts": {
        "screenshot": { "modifiers": ["Ctrl", "Shift"], "key": 52 },
        "record": { "modifiers": ["Ctrl", "Shift"], "key": 53 },
        "annotate": null
      }
    }
    ```

    `version` is always written as `1`. On read it's ignored (any value, or none, is accepted, and known keys are still read), so a future schema bump can migrate.

- **`windows/src/Snapper.Core/Settings/ISettingsStore.cs`**: `public interface ISettingsStore { SettingsValues Load(); void Save(SettingsValues values); }`. `Load` never throws and returns defaults on any failure. `Save` throws `SettingsSaveException`, a small public exception class in the same file that wraps the underlying `IOException`/`UnauthorizedAccessException`, when the file can't be written.

- **`windows/src/Snapper.Core/Settings/JsonSettingsStore.cs`**: `public sealed class JsonSettingsStore : ISettingsStore`.
  - `public JsonSettingsStore(string filePath)` takes an injectable path, so tests use a temp directory. `public string FilePath`.
  - `public static string DefaultFilePath(string appDataFolder)` → `Path.Combine(appDataFolder, "Snapper", "settings.json")` (§13.9). The Windows layer passes `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)`.
  - Parsing is split out as **`public static SettingsValues Parse(string json)`**, and writing as **`public static string Serialize(SettingsValues values)`**, so most tests need no file system. `Load()` = read the file as UTF-8 text → `Parse`. If the file is missing, or reading throws `IOException`/`UnauthorizedAccessException`, `Load()` returns `SettingsValues.Defaults`. `Load` **never writes or repairs** the file.
  - **`Parse`, per-value fallback** (PRD §13.9: "A missing or unreadable file means defaults. Unknown or invalid values fall back to their defaults individually"). Use `JsonDocument.Parse` with `CommentHandling = Skip` and `AllowTrailingCommas = true`, so a hand-edited file stays readable.
    - Invalid JSON, or a root that isn't an object → `Defaults`.
    - Property names match **case-sensitively**. Unknown properties are ignored.
    - Booleans: only JSON `true`/`false`. Anything else (`0`, `1`, `"yes"`, `null`) → that key's default. Unlike macOS (`AppSettingsTests.swift:86-99`), numbers are **not** accepted; that NSNumber bridging was a `UserDefaults` artifact.
    - Enum strings: only the exact lowercase storage strings listed in the table. `"M"`, `"Both"`, `"ultra"`, non-strings → default. Map them with explicit `switch` tables in this file (`"file"` ↔ `ScreenshotDestination.File`, and so on), never `Enum.Parse`/`ToString()`, so renaming a C# member can't change the format.
    - `saveFolder`: absent, `null`, a non-string, or a string that fails `IsValidSaveFolderPath` → `null` (the default folder). `internal static bool IsValidSaveFolderPath(string)` is a pure string rule, so it gives the same answer on macOS test hosts, where `Path.IsPathFullyQualified` doesn't understand drive letters. It accepts a drive-absolute path (`^[A-Za-z]:\\`) or a UNC path (`\\server\share…`, with at least server and share segments). It rejects empty or whitespace-only strings, relative paths (`Pictures\Screenshots`, `\Shots`, `C:Shots`), `~`-paths, `%USERPROFILE%\…` (environment variables aren't expanded), strings containing `< > " | ? *` or characters below U+0020, and any `:` other than the drive colon. A valid path to a folder that doesn't exist is **valid**: it gets created or reported at save time by 0026 and 0031.
    - `annotationColor`: it must be an object whose `red`, `green`, `blue`, `alpha` members are all JSON numbers and pass `RgbaColor.TryCreateStrict`. Otherwise → `RgbaColor.Red`. Extra members are ignored.
    - `shortcuts`: if the member is absent or not an object, all three are their defaults. Inside it, each of `screenshot`/`record`/`annotate` is resolved separately: absent → default; JSON `null` → `null` (cleared); a valid chord object whose `KeyChord.TryCreate` succeeds **and** whose `IsValidBinding` is true → that chord; anything else (wrong types, unknown or duplicate modifier name, `key` not an integer, a modifier VK, a Win combination, Shift-only) → default.
    - **Duplicate bindings** can come from a hand-edited file and would make `RegisterHotKey` fail misleadingly in 0029. They are resolved after the individual parsing, in the order screenshot, record, annotate. A non-null binding equal to an earlier non-null binding is replaced by its own default, and if that default is *also* equal to an earlier binding, it becomes `null` (cleared). This mirrors PRD §10's rule that "a binding already used by another Snapper shortcut is rejected".
  - **`Serialize`**: `Utf8JsonWriter` with `Indented = true` writes the keys in the table's order, after `"version": 1`. `saveFolder` is omitted when `null`. Doubles are written with `WriteNumber`, which round-trips in .NET, so a color decoded from a file equals the color that was saved exactly. Shortcut `null` is written as JSON `null`.
  - **Atomic write** (`Save`):
    1. `Directory.CreateDirectory` on the file's folder.
    2. Write the serialized UTF-8 bytes to the sibling `settings.json.tmp` (`FileMode.Create`, `FileShare.None`), then `Flush(flushToDisk: true)`.
    3. `File.Move(tmp, path, overwrite: true)`, which is `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` on Windows and atomic on the same volume.

    Readers therefore see either the old file or the new one, never a partial file. A stale `.tmp` left by a crash is simply overwritten by the next save. On any `IOException`/`UnauthorizedAccessException`, delete the `.tmp` best-effort, leave the existing `settings.json` untouched, and throw `SettingsSaveException`.

- **`windows/src/Snapper.Core/Settings/AppSettings.cs`**: `public sealed class AppSettings : INotifyPropertyChanged`, the one typed settings object. It ports `AppSettings.swift`, with injection of the store instead of `UserDefaults`.
  - `public AppSettings(ISettingsStore store, string defaultSaveFolder)` calls `store.Load()` once and **writes nothing** (port of `AppSettings.swift:62-72`, test `initWritesNothing`). `defaultSaveFolder` comes from 0026, so the default path has exactly one definition in the codebase.
  - Read/write properties, one per row of the schema table, with the same names and types. The exception is `SaveFolder`:
    - `public string SaveFolder { get; set; }` returns the stored custom folder, or `defaultSaveFolder` when none is stored.
    - `public bool HasCustomSaveFolder` reports whether a folder is stored.
    - Assigning any value, including one equal to the default path, stores it as custom, which mirrors macOS (`AppSettings.swift:27-35`, which always persists the path once it's set).
    - Assigning a value that fails `IsValidSaveFolderPath` throws `ArgumentException`, because that's a programmer error: the Settings folder picker only yields absolute paths (port of the macOS `assertionFailure`).
    - **Why `null` = default and the key is omitted:** if the user never picks a folder, the default keeps following the Pictures known folder (for example after OneDrive redirection), instead of being frozen into the file by the first unrelated save.
  - **Setter semantics:**
    - If the new value equals the current value, do nothing: no save, no event.
    - Otherwise, update the in-memory value, call `store.Save(snapshot)` with the full current `SettingsValues`, then raise `PropertyChanged` with `nameof(Property)`.
    - If `Save` throws `SettingsSaveException`, keep the new in-memory value (the app keeps working this session), raise `public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed` (args carry the exception), and still raise `PropertyChanged`. The next successful save writes everything, because every save writes the full snapshot.
  - `public SettingsValues Snapshot()` returns the current values, with `SaveFolder` `null` unless custom. Tests and the store use it.
  - **Threading:** `AppSettings` isn't thread-safe and is only touched on the WPF dispatcher thread, the Windows analogue of `@MainActor` (AGENTS.md "Threading"). Background work (encoding in 0045/0046) receives values read at recording start (§7 item 6: "read once when a recording starts"), not the object. Document this in the XML doc comment.
  - **`LaunchAtLogin` is a mirror.** The HKCU `Run` value is the source of truth (§13.9: "The toggle is on only when the value exists and points to the running exe"). 0033 writes the mirror after reading the registry. Nothing at startup registers or unregisters based on the stored bool, the same rule as macOS (`docs/specs/done/0010-settings-window_COMPLETED.md:93`).
  - Nothing else in the codebase reads or writes `settings.json` directly.

### Wiring (Windows, platform glue, minimal)

- **`windows/src/Snapper.Windows/App/AppCoordinator.cs`** (from 0025): at startup, create the single instance:

  ```csharp
  new AppSettings(
      new JsonSettingsStore(JsonSettingsStore.DefaultFilePath(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))),
      <0026 SaveFolder default-path member>)
  ```

  Keep it for the app's lifetime and expose it to later specs by injection. Subscribe to `SaveFailed` and write the message with `Debug.WriteLine`. There's no UI for it in v1 (see Open questions). No other code constructs `AppSettings` or `JsonSettingsStore` against the real path.
- No `NativeMethods.txt` additions. No new NuGet packages.
- No PRD change in this spec.

## Acceptance criteria

- [ ] `new AppSettings(store, defaultFolder)` over an empty or missing file reports exactly the PRD defaults:
  - launch at login off, save folder = 0026's default (`%USERPROFILE%\Pictures\Screenshots`), destination Both, shutter sound on, microphone off;
  - format MP4, MP4 quality High, GIF quality Medium;
  - annotation color red `#FF3B30`, stroke width M;
  - shortcuts Ctrl+Shift+4 / Ctrl+Shift+5 / Ctrl+Shift+6.
- [ ] Every property persists: a value set through one `AppSettings` is read back by a new instance over the same file.
- [ ] Each invalid value in the file (wrong JSON type, unknown string, out-of-range color, relative path, invalid chord, Win or Shift-only chord) falls back to its own default while the valid values beside it are kept. Invalid JSON, a non-object root, or an unreadable file gives all defaults.
- [ ] Constructing `AppSettings` writes nothing (no file or folder is created).
- [ ] Writes are atomic: after a save, `settings.json` contains the full snapshot and no `.tmp` remains. A failed save leaves the previous file intact, keeps the new value in memory, and raises `SaveFailed`.
- [ ] `PropertyChanged` fires once per actual change, with the property name, and not for same-value assignments.
- [ ] The JSON key names and value strings match the schema table exactly. Shortcuts are stored in `settings.json`; `null` means cleared and an absent key means default.
- [ ] `KeyChord.DisplayString` for the defaults is `Ctrl+Shift+4`, `Ctrl+Shift+5`, `Ctrl+Shift+6`.
- [ ] `ColorPreset` order and values, and `StrokeWidth` points and text sizes, match §8.2 and §8.3.
- [ ] Nothing in `Snapper.Core` references WPF, WinForms, WinRT, or interop.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed with no warnings (warnings are errors), and the app still starts with its tray icon.

## Edge cases

- **`playShutterSound` defaults to `true`:** a missing key must read as `true`, not `false`.
- **The `%APPDATA%\Snapper` folder doesn't exist:** `Load` returns defaults without creating it, and the first `Save` creates it.
- **The file is locked by another process or antivirus during a read:** defaults for this session (the `IOException` path). The next save replaces the file. Because 0025 enforces a single instance, there's never a second Snapper writer.
- **`settings.json` is read-only or the folder is unwritable:** `File.Move` fails → `SettingsSaveException` → `SaveFailed`. The in-memory value stays, and the previous file stays untouched.
- **A stale `settings.json.tmp` from a crash:** ignored by `Load` and overwritten by the next `Save`.
- **A crash between the temp write and the move:** the old `settings.json` is intact, so no settings are corrupted.
- **Hand edits with comments or trailing commas:** still parsed. Unknown keys are ignored and dropped by the next save. Invalid values are replaced with defaults by the next save, because every save writes the full in-memory snapshot, which already holds the defaults.
- **A non-ASCII or very long save-folder path (for example `D:\Écrans\…`):** UTF-8 round-trips it unchanged. `Utf8JsonWriter` may escape non-ASCII characters as `\uXXXX`, which is still valid JSON and reads back identically. Configure `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so the file stays human-readable. That's safe here because the file is never embedded in HTML.
- **A UNC save folder (`\\nas\shots`):** valid. Whether it's reachable is checked at save time (0026/0031).
- **A save folder stored with a trailing backslash (`D:\Shots\`) or as a drive root (`D:\`):** valid and stored as given. Path joining in 0026 handles both.
- **`version` greater than 1 (a future file opened by an older build):** known keys are still read and unknown keys ignored. The next save writes `version: 1`. That's acceptable for v1.
- **Duplicate JSON property names:** use the value `JsonElement.TryGetProperty` returns (no special handling). Not tested.
- **A chord with a modifier VK (`key: 16`) or `key: 0`/`255`:** invalid → that shortcut's default.
- **Two shortcuts equal in the file, for example screenshot and record both Ctrl+Shift+4:** record falls back to Ctrl+Shift+5. If annotate is then also Ctrl+Shift+5 and its default Ctrl+Shift+6 is free, annotate becomes Ctrl+Shift+6. If it isn't free, annotate becomes `null`.
- **Translucent custom colors** (0040's Color dialog yields opaque colors, but a hand-edited alpha < 1 is valid) round-trip unchanged.
- **External edits while Snapper runs** aren't observed, as on macOS. The next save overwrites them.

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`). File-system tests each create a unique temp directory (`Path.Combine(Path.GetTempPath(), "SnapperTests", Guid.NewGuid().ToString("N"))`) and delete it in `Dispose`. A test-only `InMemorySettingsStore : ISettingsStore`, in the test project, records each saved snapshot and can be told to throw `SettingsSaveException`. Windows-style path strings work on macOS hosts, because path validation is a pure string rule.
  - `RgbaColorTests.cs` (port of `SnapperTests/RGBAColorTests.swift:7-67`)
    - `PresetOrder`: the `Enum.GetValues<ColorPreset>()` order is Red, Orange, Yellow, Green, Blue, Purple, Black, White, and `RgbaColor.Presets` equals their colors in that order.
    - `PresetValues`: each preset's components equal the `n / 255.0` table values, with alpha 1. `DisplayName` is "Red" … "White".
    - `ConstructorClamps`: `new RgbaColor(2, -1, 0.5, 3)` == `(1, 0, 0.5, 1)`, and `new RgbaColor(NaN, +∞, −∞, NaN)` == `(0, 0, 0, 1)`.
    - `TryCreateStrictRejectsInvalid`: for each component, `-0.1`, `1.5`, `NaN`, and `+∞` return `false`; `(0.5, 0.5, 0.5, 1)` returns `true`, with equal values.
    - `ByteConversions`: `FromBytes(255, 59, 48)` == `RgbaColor.Red`; `ToBytes()` of every preset returns its hex bytes; `new RgbaColor(0.5, 0.5, 0.5).ToBytes()` == `(128, 128, 128, 255)` (away-from-zero rounding).
  - `StrokeWidthTests.cs` (port of `SnapperTests/StrokeWidthTests.swift:5-10`)
    - `CasesOrder`: `[S, M, L]`.
    - `Points`: `[2, 4, 8]`.
    - `TextPointSize`: `[16, 24, 36]`.
    - `Labels`: `["S", "M", "L"]`.
  - `KeyChordTests.cs` (adapted from `SnapperTests/KeyChordTests.swift:5-19`)
    - `MasksModifierNoise`: `Create((KeyModifiers)0x4006, 0x34)` equals `Create(Ctrl | Shift, 0x34)`, and `Create((KeyModifiers)int.MaxValue, 0x34).Modifiers == Ctrl | Alt | Shift | Win`.
    - `ModifierFlagValuesMatchWin32`: `Alt == 1`, `Ctrl == 2`, `Shift == 4`, `Win == 8`.
    - `RejectsModifierAndOutOfRangeKeys`: `TryCreate` is `false` for VKs `0x00`, `0xFF`, `0x100`, `-1`, `0x10`, `0x11`, `0x12`, `0x5B`, `0x5C`, `0xA0`–`0xA5`; `Create` throws `ArgumentException` for `0x10`.
    - `DisplaysDefaultsAndModifierOrder`: Ctrl+Shift+`0x34` → `"Ctrl+Shift+4"`; all four modifiers + `0x41` → `"Ctrl+Alt+Shift+Win+A"`; Alt + `0x70` → `"Alt+F1"`; no modifiers + `0x35` → `"5"`.
    - `KeyNames`: digits `0x30`–`0x39` → `"0"`–`"9"`; `0x5A` → `"Z"`; `0x87` → `"F24"`; `0x60` → `"Num 0"`; `0x6B` → `"Num +"`; `0x20` → `"Space"`; `0x2C` → `"PrtScn"`; `0x2E` → `"Delete"`; `0x22` → `"Page Down"`; `0xBD` → `"-"`; `0xDC` → `"\"`; `0x07` → `"Key 0x07"`; `0xE5` → `"Key 0xE5"`.
    - `IsValidBinding`: Ctrl+4 ✓, Alt+4 ✓, Ctrl+Shift+4 ✓, Ctrl+Alt+Shift+4 ✓; Shift+4 ✗, no modifiers ✗, Win+4 ✗, Ctrl+Win+4 ✗.
    - `Equality`: equal chords are `==` and share a hash code; different VKs or modifiers are not equal.
  - `SettingsEnumsTests.cs`
    - `ScreenshotDestinationFlagsAndTitles`: `SavesToFile` `[true, false, true]` and `CopiesToClipboard` `[false, true, true]` for File, Clipboard, Both; titles "Save to file", "Copy to clipboard", "Both" (port of `AppSettingsTests.swift:144-149`).
    - `RecordingFormatTitlesAndExtensions`: "MP4"/"GIF" and "mp4"/"gif".
    - `QualityTitles`: `Mp4Quality` and `GifQuality` each give "High", "Medium", "Low", in declaration order High, Medium, Low.
  - `JsonSettingsStoreTests.cs` (format and fallback; mostly `Parse`/`Serialize`)
    - `DefaultsMatchPrd`: `SettingsValues.Defaults` has every value in the schema table (one assertion per property; `SaveFolder` is `null`; shortcuts Ctrl+Shift+`0x34`/`0x35`/`0x36`).
    - `DefaultFilePath`: `DefaultFilePath("C:\\Users\\a\\AppData\\Roaming")` == `Path.Combine("C:\\Users\\a\\AppData\\Roaming", "Snapper", "settings.json")`.
    - `ParseEmptyAndInvalidGiveDefaults`: `""`, `"not json"`, `"[]"`, `"42"`, and `"null"` each parse to `SettingsValues.Defaults`; `"{}"` also does.
    - `SerializeWritesPinnedFormat`: serialize a fully non-default snapshot, then `JsonDocument.Parse` it. Assert the top-level property names are exactly, in order, `version, launchAtLogin, saveFolder, screenshotDestination, playShutterSound, recordMicrophone, recordingFormat, mp4Quality, gifQuality, annotationColor, annotationStrokeWidth, shortcuts`. Assert `version == 1`, `screenshotDestination == "clipboard"`, `recordingFormat == "gif"`, `mp4Quality == "low"`, `gifQuality == "high"`, `annotationStrokeWidth == "l"`, the color object has exactly the keys `red, green, blue, alpha`, and the shortcut objects have `modifiers` in canonical order and an integer `key`.
    - `SerializeOmitsDefaultSaveFolderAndWritesNullShortcut`: with `SaveFolder = null` the `saveFolder` key is absent; with `AnnotateShortcut = null`, `shortcuts.annotate` is JSON `null`.
    - `RoundTripEveryValue`: for each enum case of each enum property, each bool flipped, a custom color `(0.1, 0.2, 0.3, 0.5)`, the color `1/3`, the folder `D:\Écrans\Snapper`, a UNC folder `\\nas\shots`, cleared shortcuts, and Alt+F1 → `Parse(Serialize(v)) == v` exactly.
    - `InvalidBoolsFallBack`: `playShutterSound` as `"nope"`, `0`, `1`, or `null` → `true`; `launchAtLogin` as `"yes"` → `false`; each time, a sibling valid value in the same file (`recordMicrophone: true`) is kept.
    - `InvalidEnumsFallBack`: `screenshotDestination` `"printer"`, `42`, `"Both"` → `Both`; `annotationStrokeWidth` `"xl"`, `"M"` → `M`; `recordingFormat` `"webm"` → `Mp4`; `mp4Quality` `"ultra"` → `High`; `gifQuality` `"ultra"`, `2` → `Medium`.
    - `InvalidSaveFoldersFallBack`: `""`, `"   "`, `"Pictures\\Screenshots"`, `"\\Shots"`, `"C:Shots"`, `"~\\Pictures"`, `"%USERPROFILE%\\Pictures"`, `"C:\\a<b"`, `"C:\\a:b"`, `"\\\\server"` (no share), `42`, `null` → `SaveFolder == null`.
    - `ValidSaveFoldersAccepted`: `"C:\\Users\\a\\Pictures\\Screenshots"`, `"d:\\"`, `"D:\\Shots\\"`, `"\\\\nas\\shots"`, `"\\\\nas\\shots\\2026"`, `"D:\\Écrans\\Snapper"` each parse to that exact string.
    - `InvalidColorsFallBack`: a string `"red"`, `{"red":2,…}`, `{"red":-0.1,…}`, a missing `alpha`, `{"red":"1",…}`, and `[1,0,0,1]` → `RgbaColor.Red`; `{"red":0,"green":0,"blue":0,"alpha":1,"extra":5}` → black (extra members ignored).
    - `ShortcutFallbacks`: `shortcuts` absent → all defaults; `shortcuts: 5` → all defaults; `screenshot` absent → default and `record: null` → `null` in the same file; `annotate` with `modifiers: ["Ctrl","Hyper"]`, `["Ctrl","Ctrl"]`, `"Ctrl"` (not an array), `key: "A"`, `key: 16`, `key: 300`, Win+Ctrl+A, or Shift+A → Ctrl+Shift+6; `modifiers: ["Shift","Ctrl"]`, key 65 → Ctrl+Shift+A (order-insensitive read).
    - `DuplicateShortcutsResolved`:
      - screenshot = record = Ctrl+Shift+4 → record becomes Ctrl+Shift+5.
      - screenshot = Ctrl+Shift+5, record = Ctrl+Shift+5, annotate = Ctrl+Shift+4 → record falls back to its default Ctrl+Shift+5, which equals screenshot, so record becomes `null`; annotate stays Ctrl+Shift+4.
      - All three cleared (`null`) → all stay `null` (nulls are never duplicates).
    - `CommentsAndTrailingCommasAccepted`: `{ // c\n "playShutterSound": false, }` → `PlayShutterSound == false`.
    - `PropertyNamesAreCaseSensitive`: `{"PlayShutterSound": false}` → `true` (the default).
    - `UnknownKeysAndVersionIgnored`: `{"version": 99, "foo": 1, "recordMicrophone": true}` → `RecordMicrophone == true`, everything else default.
  - `JsonSettingsStoreFileTests.cs` (temp directory)
    - `LoadMissingFileGivesDefaultsAndCreatesNothing`: the path is in a non-existent subfolder → `Defaults`, and neither the folder nor the file exists afterward.
    - `SaveCreatesFolderAndFileAtomically`: `Save(v)` creates the folder; the file text equals `Serialize(v)`; no `settings.json.tmp` exists; `Load()` == `v`.
    - `SaveReplacesExistingAndStaleTemp`: pre-create `settings.json` with other content and a stale `settings.json.tmp` with garbage; `Save(v)` → `Load() == v`, and the `.tmp` is gone.
    - `SaveFailureThrowsAndKeepsOldFile`: create a **file** where the settings folder should be (so `CreateDirectory` fails) → `Save` throws `SettingsSaveException`. Second case: save once, then make the `.tmp` path an existing **directory**, so the temp write fails → `SettingsSaveException`, and `settings.json` still holds the first snapshot.
    - `LoadCorruptFileDoesNotRewrite`: write `"garbage"` → `Load()` == `Defaults`, and the file content is still `"garbage"`.
    - `FileIsUtf8WithoutBom`: after saving a folder `D:\Écrans`, the first bytes are `{` (no `EF BB BF`), and the raw text contains `Écrans` unescaped.
  - `AppSettingsTests.cs` (port of `SnapperTests/AppSettingsTests.swift:15-135`, with `InMemorySettingsStore` unless noted)
    - `DefaultsOnEmptyStore`: one assertion per property. `SaveFolder` equals the injected `defaultSaveFolder` (`"C:\\Users\\a\\Pictures\\Screenshots"`) and `HasCustomSaveFolder` is `false`.
    - `ConstructionWritesNothing`: the store has 0 saves after construction. With a `JsonSettingsStore` in a temp directory, no file exists after construction.
    - `RoundTripEachProperty`: for each property, set a non-default value on instance A over a temp-directory `JsonSettingsStore`, then build instance B over the same path and assert equality. Values: `LaunchAtLogin = true`, `SaveFolder = "D:\\Shots"`, `ScreenshotDestination` File and Clipboard, `PlayShutterSound = false`, `RecordMicrophone = true`, `RecordingFormat = Gif`, `Mp4Quality` Medium and Low, `GifQuality` High and Low, `AnnotationColor` Blue and `(0.1, 0.2, 0.3, 0.5)`, `AnnotationStrokeWidth` S and L, `ScreenshotShortcut = Alt+F1`, `RecordShortcut = null`.
    - `SetterSavesFullSnapshotAndNotifies`: setting `RecordMicrophone = true` → exactly one save, whose snapshot equals `Defaults with { RecordMicrophone = true }`, and exactly one `PropertyChanged` with `"RecordMicrophone"`, raised after the save (assert the order with a shared log).
    - `SameValueAssignmentIsNoOp`: `PlayShutterSound = true` on defaults → 0 saves and 0 events.
    - `DefaultFolderNotPersistedUntilChosen`: changing `PlayShutterSound` → the saved snapshot has `SaveFolder == null`. A new `AppSettings` over that store with a *different* `defaultSaveFolder` reports the new default.
    - `ChoosingDefaultPathMakesItCustom`: setting `SaveFolder` to the default path → `HasCustomSaveFolder == true`, and the snapshot carries the path.
    - `InvalidSaveFolderThrows`: setting `"Shots"` throws `ArgumentException`, and the value and the save count are unchanged.
    - `SaveFailureKeepsValueAndRaisesSaveFailed`: the store throws → after `RecordMicrophone = true` the property reads `true`, `SaveFailed` fires once with the exception, and `PropertyChanged` fires once.
    - `ShortcutCanBeCleared`: `AnnotateShortcut = null` → saved snapshot has `null`, and a reload over a `JsonSettingsStore` reads `null` (not the default).
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed with no warnings. Both run on macOS too.
- **Manual verification** (Windows 11). This spec adds no UI, hotkeys, or capture. Everything it adds is covered by the unit tests, so the manual checks only confirm that the wiring breaks nothing and that the real path is right:
  1. Delete `%APPDATA%\Snapper` if it exists. Start Snapper. Expected: the tray icon and menu behave as after 0025. `%APPDATA%\Snapper\settings.json` does **not** exist (construction writes nothing).
  2. Exit Snapper. Create `%APPDATA%\Snapper\settings.json` with the content `not json`, then start Snapper. Expected: it starts normally with its tray icon (no crash), and the file still contains `not json`.
  3. (Optional, until 0033 adds UI.) In a Debug build, temporarily set `settings.RecordMicrophone = true` from `AppCoordinator` startup code, run once, then revert. Expected: `settings.json` appears, matching the schema example, with `"recordMicrophone": true`, no `saveFolder` key, and no `settings.json.tmp` beside it.

## Resolved decisions

- Settings write failure: silent in v1 (value kept for the session, `SaveFailed` raised, logged only). Recorded in PRD §13.9.

## Out of scope

- The Settings window, the folder picker, the shortcut recorder, `ShortcutValidation` (duplicate and modifier rules in the UI), the "Another app is using this shortcut." warning, and the launch-at-login registry read/write: **0033-windows-settings-window**. 0033 reuses `KeyChord.IsValidBinding`.
- Registering hotkeys, re-registering when the shortcut properties change (by subscribing to `PropertyChanged`), the `CapturePurpose` ↔ shortcut property mapping, and the tray menu's shortcut labels: **0029-windows-global-hotkeys-app-mode**.
- The default save-folder path (Pictures known folder + `Screenshots`), folder creation, writability, and file naming: **0026-windows-file-naming-save-folder**.
- Using the destination and shutter-sound values when capturing: **0031**. Using the microphone value and the privacy check: **0037**. The Recording tab's Format and Quality pickers, bitrate math, and GIF plans: **0045**, **0046**.
- Swatch UI, the `ChooseColor` dialog, and writing back the last-used color and width: **0040**/**0041**. Extending the annotation model types: **0038**.
- The macOS `onboardingDismissed` key: not applicable on Windows, which has no onboarding window (PRD §13.10).


## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**; `dotnet test windows/Snapper.slnx` passed **221 tests**, including **123 new tests**, with zero skips. The Core assembly platform-dependency smoke test remains green. PRD and AGENTS.md needed no changes; behavior and architecture follow their existing requirements.

Implementation naming adjustment: C# cannot declare both an instance component and a static preset named `Red`, `Green`, or `Blue` on the same type. `RgbaColor` therefore exposes `R`, `G`, `B`, and `Alpha` components while preserving the specified `RgbaColor.Red` … `White` presets. The persisted component names remain `red`, `green`, `blue`, and `alpha`, unchanged. This is an internal API detail, with no product behavior change.

GUI behavior has not been verified. On Windows 11:

- [ ] not yet run by user: Back up any existing `%APPDATA%\Snapper` settings, start Snapper with that folder absent, and confirm the tray/menu work and construction creates no `settings.json`.
- [ ] not yet run by user: With Snapper exited, create `%APPDATA%\Snapper\settings.json` containing `not json`, launch Snapper, and confirm normal tray startup without a crash or rewriting the corrupt file. Restore any backed-up settings afterwards.
- [ ] not yet run by user: Optional until 0033 adds UI: temporarily set `Settings.RecordMicrophone = true` in a Debug startup, run once, and revert the temporary code. Confirm UTF-8 `settings.json` matches the pinned schema, includes `"recordMicrophone": true`, omits `saveFolder`, and leaves no `.tmp` sibling.
