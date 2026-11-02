# 0003 — Add the typed AppSettings store, RGBAColor, and StrokeWidth

> Source: docs/PRD.md §10 (General/Recording defaults), §11 ("Don't show again"), §8.2 (remembered color and stroke width, swatches, S/M/L), §8.3 (text sizes), §9 (default save folder); AGENTS.md "Architecture rules" (one typed settings type over `UserDefaults`)
> Depends on: 0001-xcodegen-app-shell (0002-file-naming-save-folder lands first and is reused if it defines the default folder, see below)

## Context

Not yet implemented. After 0001 the app has `AppCoordinator` (`Snapper/App/AppCoordinator.swift`) and a placeholder Settings window, but no preferences at all. Several later specs need persisted user choices:

- docs/PRD.md §10: launch at login (default **off**), save folder (default `~/Documents/Screenshots`), screenshot destination *Save to file / Copy to clipboard / Both* (default **Both**), play shutter sound (default **on**), record microphone (default **off**).
- docs/PRD.md §11: the onboarding window appears at each launch until both steps are done "or the user dismisses it with 'Don't show again'". That dismissal must persist.
- docs/PRD.md §8.2: "The last-used color is remembered between sessions. The initial default is **red**." and "The last-used stroke width is remembered between sessions. The initial default is **M**." Swatches are red, orange, yellow, green, blue, purple, black, white, plus the system color picker. Stroke width is "S / M / L = 2 / 4 / 8 image points".
- docs/PRD.md §8.3: text size follows the stroke width, "S = 16pt, M = 24pt, L = 36pt, in image points".

AGENTS.md requires preferences in `UserDefaults` behind **one typed settings type**, with shortcuts stored by `KeyboardShortcuts` (not by us). This spec adds that type and the two small pure value types (`RGBAColor`, `StrokeWidth`) that the settings and the annotation specs share. It adds no UI.

## User story

As a Snapper user, my choices (save folder, screenshot destination, shutter sound, microphone, "Don't show again", last annotation color and stroke width) survive quitting and relaunching, and a fresh install starts with the PRD defaults.

## Scope & changes

### Pure value types (unit-tested, no AppKit)

- **`Snapper/Annotate/Model/RGBAColor.swift`** — `struct RGBAColor: Codable, Equatable, Hashable, Sendable` with `red`, `green`, `blue`, `alpha` as `Double`, each in `0...1`, in the **sRGB** color space.
  - `init(red:green:blue:alpha: = 1)` clamps each component to `0...1`; a non-finite component (NaN, ±∞) becomes `0` (alpha: `1`). This keeps every in-memory value valid.
  - Custom `init(from:)` decodes the four keys `red`, `green`, `blue`, `alpha` and **throws** `DecodingError.dataCorrupted` if any is missing, non-finite, or outside `0...1`. (Stored garbage must fall back to the default, not be silently clamped into some other color.) `encode(to:)` writes the same four keys. Keys are part of the persisted format: don't rename them.
  - Presets, with exact values (system-palette sRGB hex, alpha 1). Write components as `n / 255` so tests can compare exactly:

    | Preset | Hex | red | green | blue |
    |---|---|---|---|---|
    | red | `#FF3B30` | 255/255 | 59/255 | 48/255 |
    | orange | `#FF9500` | 255/255 | 149/255 | 0 |
    | yellow | `#FFCC00` | 255/255 | 204/255 | 0 |
    | green | `#34C759` | 52/255 | 199/255 | 89/255 |
    | blue | `#007AFF` | 0 | 122/255 | 255/255 |
    | purple | `#AF52DE` | 175/255 | 82/255 | 222/255 |
    | black | `#000000` | 0 | 0 | 0 |
    | white | `#FFFFFF` | 255/255 | 255/255 | 255/255 |

  - `enum ColorPreset: String, CaseIterable, Sendable` with cases `red, orange, yellow, green, blue, purple, black, white` **in exactly this order** (it is the toolbar swatch order, §8.2), `var color: RGBAColor`, and `var displayName: String` ("Red", "Orange", …, "White") for tooltips and accessibility labels in 0018. Convenience statics `RGBAColor.red` … `RGBAColor.white` return the preset colors, and `static let presets: [RGBAColor] = ColorPreset.allCases.map(\.color)`.
  - Thin CoreGraphics bridge (import `CoreGraphics` only, no AppKit): `var cgColor: CGColor` (via `CGColor(srgbRed:green:blue:alpha:)`), and `init?(cgColor: CGColor)` that converts to sRGB (`CGColorSpace(name: CGColorSpace.sRGB)` + `CGColor.converted(to:intent:options:)`), returns `nil` if conversion fails or the result doesn't have 4 components, and otherwise goes through the clamping init. 0018 uses this to store colors picked in `NSColorPanel` (which may be in Display P3 or grayscale), so out-of-gamut values get clamped. No `NSColor` helpers here; views add them where needed.

- **`Snapper/Annotate/Model/StrokeWidth.swift`** — `enum StrokeWidth: String, CaseIterable, Codable, Sendable { case s, m, l }` (raw values `"s"`, `"m"`, `"l"` are the persisted format).
  - `var points: CGFloat` → `2`, `4`, `8` (image points, §8.2). Converting image points to image pixels is the document's job (`pixelsPerPoint` in 0016), not this type's.
  - `var textPointSize: CGFloat` → `16`, `24`, `36` (image points, §8.3).
  - `var label: String` → `"S"`, `"M"`, `"L"` for the toolbar.
  - `static let default: StrokeWidth = .m` is **not** added here; the default lives in `AppSettings.Defaults` so there is one place for all defaults.

### Settings store (pure Foundation + Observation, unit-tested)

- **`Snapper/Settings/AppSettings.swift`** — `@Observable @MainActor final class AppSettings`.

  **Why this shape:** `@Observable` lets the SwiftUI Settings and onboarding windows (0006, 0010) bind with `@Bindable` and re-render on change, and AppKit code (0008 capture output, 0017/0018 toolbar) just reads properties. All readers and writers are UI or UI-triggered code on the main actor, so `@MainActor` costs nothing and makes it `Sendable`-safe under Swift 6 strict concurrency. A struct over `UserDefaults` with `NotificationCenter` change notifications was rejected: it needs extra plumbing to drive SwiftUI and gives the same testability. Testability comes from injecting the `UserDefaults` instance.

  - `init(defaults: UserDefaults = .standard)`. In `init`, every property is loaded from `defaults` through a decoding helper that falls back to the default on a missing or invalid value. `init` **writes nothing** (assignments in `init` don't trigger `didSet`), so launching the app doesn't populate the defaults domain.
  - Stored properties, each with a `didSet` that writes the new value to `defaults` immediately (no `synchronize()`):

    | Property | Type | Default | Key (`AppSettings.Key` raw value) | Stored as |
    |---|---|---|---|---|
    | `launchAtLogin` | `Bool` | `false` | `"launchAtLogin"` | Bool |
    | `saveFolderURL` | `URL` | `~/Documents/Screenshots` | `"saveFolderPath"` | String: absolute, standardized file path |
    | `screenshotDestination` | `ScreenshotDestination` | `.both` | `"screenshotDestination"` | String raw value |
    | `playShutterSound` | `Bool` | `true` | `"playShutterSound"` | Bool |
    | `recordMicrophone` | `Bool` | `false` | `"recordMicrophone"` | Bool |
    | `onboardingDismissed` | `Bool` | `false` | `"onboardingDismissed"` | Bool |
    | `annotationColor` | `RGBAColor` | `RGBAColor.red` (`#FF3B30`) | `"annotationColor"` | `Data` (JSON via `JSONEncoder`) |
    | `annotationStrokeWidth` | `StrokeWidth` | `.m` | `"annotationStrokeWidth"` | String raw value |

  - **Keys in one place:** `enum Key: String, CaseIterable` nested in `AppSettings`, with exactly the raw values above. These strings are the on-disk format; changing one loses users' settings, so the tests pin them. They don't collide with `KeyboardShortcuts`, which uses its own `KeyboardShortcuts_`-prefixed keys.
  - **Defaults in one place:** `enum Defaults` nested in `AppSettings` with a static per property (`launchAtLogin = false`, `screenshotDestination = .both`, `playShutterSound = true`, `recordMicrophone = false`, `onboardingDismissed = false`, `annotationColor = .red`, `annotationStrokeWidth = .m`, `saveFolderURL`). `UserDefaults.register(defaults:)` is **not** used: it can't express "invalid value → default" and would split the defaults across two mechanisms.
  - **Default save folder:** `FileManager.default.homeDirectoryForCurrentUser` + `Documents/Screenshots` (directory URL). If 0002's `SaveFolder` already defines this default (e.g. `SaveFolder.defaultURL`), `AppSettings.Defaults.saveFolderURL` must **reuse it** instead of defining a second copy. There is exactly one definition in the codebase. Creating the folder and checking writability stay in 0002's `SaveFolder`; `AppSettings` only stores the user's choice.
  - **Why a path string, not a bookmark:** Snapper is not sandboxed (AGENTS.md, PRD §3), so no security-scoped bookmark is needed to regain access. A bookmark also can't point to a folder that doesn't exist yet, and the PRD default folder is "created on first use if it doesn't exist" (§9). A plain path keeps that simple. Trade-off: if the user renames or moves the folder in Finder, the stored path goes stale; that is handled at save time by 0002/0008 ("Couldn't save to <folder>" + **Choose Folder…**, §9), not here.
  - **Writing the save folder:** store `url.standardizedFileURL.path` (no `~`, no trailing-slash dependence). A non-file URL assigned to `saveFolderURL` is a programmer error: `assertionFailure` and don't persist it.
  - **Invalid stored values fall back to the default** (and the stored garbage is left as is until the next write overwrites it):
    - Bools: read with `defaults.object(forKey:) as? Bool`; a missing key or a non-Bool value (e.g. a string, or a number other than 0/1) → default. Do **not** use `bool(forKey:)`, which turns "missing" into `false` and would break `playShutterSound`'s `true` default.
    - `saveFolderPath`: not a `String`, empty, or not an absolute path (doesn't start with `/`) → default. A well-formed path to a folder that doesn't exist is **valid** (it is created or reported at save time).
    - `screenshotDestination` / `annotationStrokeWidth`: not a `String`, or not a known raw value → default.
    - `annotationColor`: not `Data`, not valid JSON, or rejected by `RGBAColor`'s decoder (missing/out-of-range/non-finite component) → default.
  - Keep the decode logic in small `static` helpers (`func decodeBool(_:in:default:)` etc. or one generic helper) so each fallback rule is directly testable through a fresh `AppSettings(defaults:)`.

- **`Snapper/Settings/ScreenshotDestination.swift`** — `enum ScreenshotDestination: String, CaseIterable, Sendable { case file, clipboard, both }` (raw values `"file"`, `"clipboard"`, `"both"` are the persisted format), with `var savesToFile: Bool` (`.file`, `.both`) and `var copiesToClipboard: Bool` (`.clipboard`, `.both`) for 0008, and `var title: String` → "Save to file", "Copy to clipboard", "Both" (PRD §10 wording) for the 0010 picker.

### Wiring (platform glue, minimal)

- `AppCoordinator` (0001) creates the single instance at launch, `let settings = AppSettings()`, and keeps it for the app's lifetime. Later specs receive it from the coordinator by injection; nobody else constructs an `AppSettings` against `.standard` and no code reads Snapper keys from `UserDefaults` directly.

### Project

- New files are picked up by the `Snapper` and `SnapperTests` source globs from 0001. Run `xcodegen generate` after adding them. No `project.yml` changes are expected; if 0001's spec lists sources explicitly, add the new files there.
- No PRD change in this spec.

## Acceptance criteria

- [ ] A fresh `AppSettings` over an empty `UserDefaults` suite reports exactly the PRD defaults: launch at login off, save folder `~/Documents/Screenshots`, destination Both, shutter sound on, record microphone off, onboarding not dismissed, annotation color red, stroke width M.
- [ ] Every property persists: a value set on one instance is read back by a new instance over the same suite.
- [ ] Missing or invalid stored values (wrong type, unknown raw value, corrupt color data, out-of-range color component, relative or empty path) yield the default instead of crashing or producing a different value.
- [ ] Key strings are defined once, in `AppSettings.Key`, and match the table above.
- [ ] `ColorPreset.allCases` is red, orange, yellow, green, blue, purple, black, white, with the exact component values in the table.
- [ ] `StrokeWidth` maps S/M/L to 2/4/8 points and 16/24/36 pt text.
- [ ] `RGBAColor` and `StrokeWidth` import no AppKit; `AppSettings` imports only Foundation and Observation.
- [ ] Constructing `AppSettings` writes nothing to `UserDefaults`.
- [ ] Keyboard shortcuts are not stored in `AppSettings`.
- [ ] The app builds, all tests pass, and the app still launches as before.

## Edge cases

- **`playShutterSound` default is `true`:** a missing key must read as `true`, which is why `object(forKey:) as? Bool` is required (see above).
- **Bool stored as a number:** `NSNumber` 0/1 bridges to `Bool` and is accepted; other numbers (e.g. `5`) and strings (`"yes"`) fall back to the default.
- **Save folder that no longer exists or is unwritable:** valid as a setting; not this spec's problem (0002/0008).
- **Save folder given with `~` or `..` components:** the setter stores the standardized absolute path; a stored relative path like `"Documents/Screenshots"` or `"~/Foo"` is invalid (no leading `/`) and falls back to the default.
- **Colors from `NSColorPanel` in another color space or with components outside sRGB gamut:** `RGBAColor(cgColor:)` converts to sRGB and clamps; a pattern color or one that can't be converted returns `nil` (0018 then keeps the previous color).
- **Alpha:** presets are opaque, but a color with alpha < 1 from the picker round-trips unchanged.
- **Floating-point round-trip:** JSON encoding of `Double` round-trips exactly, so `Equatable` comparison after decode is exact.
- **Concurrent writers:** none; everything is on the main actor and there is one instance.
- **External changes to the defaults domain while running** (e.g. `defaults write` in Terminal) are not observed. This is acceptable: `AppSettings` is the only writer.

## Test plan

- **Unit tests.** Use XCTest (or whatever framework the `SnapperTests` target from 0001 uses, to stay consistent). Settings test classes are `@MainActor`. Each test builds `UserDefaults(suiteName: "SnapperTests.\(UUID().uuidString)")` (force unwrap is fine in tests), and `tearDown` calls `removePersistentDomain(forName:)` for that suite.
  - `SnapperTests/AppSettingsTests.swift`
    - `testDefaultsOnEmptySuite`: one assertion per property equal to the PRD default; `saveFolderURL.path` equals `FileManager.default.homeDirectoryForCurrentUser.appending(path: "Documents/Screenshots").path` (or `SaveFolder`'s default, if 0002 defines one).
    - `testInitWritesNothing`: after `AppSettings(defaults:)`, `defaults.persistentDomain(forName: suite)` is nil or empty.
    - Round-trip, one test per property: set a non-default value on instance A, create instance B over the same suite, and assert equality. Values: `launchAtLogin = true`, `saveFolderURL = /tmp/Snapper Test Folder` (a non-existent path is fine), `screenshotDestination` each of `.file` and `.clipboard`, `playShutterSound = false`, `recordMicrophone = true`, `onboardingDismissed = true`, `annotationColor = .blue` and a custom `RGBAColor(red: 0.1, green: 0.2, blue: 0.3, alpha: 0.5)`, `annotationStrokeWidth` each of `.s` and `.l`.
    - `testSettingValueWritesStoredFormat`: after setting, the raw stored values are `"clipboard"`, `"l"`, a `String` path equal to the standardized path, `Data` for the color, and a `Bool` for the toggles. This pins the on-disk format.
    - `testSaveFolderStandardizesPath`: setting `URL(filePath: "/tmp/a/../b")` stores `"/tmp/b"`.
    - Garbage fallback (write raw values with `defaults.set(_:forKey:)`, then construct and assert the default):
      - `playShutterSound` = `"nope"` → `true`; = `5` → `true`; `launchAtLogin` = `"yes"` → `false`.
      - `playShutterSound` = `0` (NSNumber) → `false` (accepted bridging).
      - `screenshotDestination` = `"printer"` → `.both`; = `42` → `.both`.
      - `annotationStrokeWidth` = `"xl"` → `.m`; = `"M"` (wrong case) → `.m`.
      - `saveFolderPath` = `""`, `"Documents/Screenshots"`, `"~/Pictures"`, `42` → default folder.
      - `annotationColor` = `Data("garbage".utf8)`, `"red"` (a String), JSON with `red: 2`, JSON missing `alpha` → `.red`.
  - `SnapperTests/AppSettingsKeyTests.swift` (or a section of the file above)
    - `testKeyRawValues`: `AppSettings.Key.allCases.map(\.rawValue)` equals exactly `["launchAtLogin", "saveFolderPath", "screenshotDestination", "playShutterSound", "recordMicrophone", "onboardingDismissed", "annotationColor", "annotationStrokeWidth"]` and contains no duplicates.
    - `testScreenshotDestinationRawValuesAndFlags`: raw values `"file"`, `"clipboard"`, `"both"`; `savesToFile`/`copiesToClipboard` truth table (file: T/F, clipboard: F/T, both: T/T).
  - `SnapperTests/RGBAColorTests.swift`
    - `testPresetOrder`: `ColorPreset.allCases` equals `[.red, .orange, .yellow, .green, .blue, .purple, .black, .white]`, and `RGBAColor.presets` equals their colors in the same order.
    - `testPresetValues`: each preset's components equal the `n/255` values in the table, alpha `1`.
    - `testCodableRoundTrip`: encode and decode each preset and a custom translucent color; the result is equal.
    - `testEncodedKeys`: encoded JSON object has exactly the keys `red`, `green`, `blue`, `alpha`.
    - `testDecodeRejectsInvalid`: out-of-range (`1.5`, `-0.1`), and a missing key each throw `DecodingError`.
    - `testInitClamps`: `RGBAColor(red: 2, green: -1, blue: 0.5, alpha: 3)` → `(1, 0, 0.5, 1)`; a `.nan` component → `0`.
    - `testCGColorRoundTrip`: `RGBAColor(cgColor: preset.cgColor) == preset` for every preset (allow `1e-6` accuracy).
    - `testCGColorFromGrayAndP3`: `CGColor(gray: 0.5, alpha: 1)` → components all ≈ the sRGB equivalent (r == g == b, alpha 1); a Display P3 pure red converts to sRGB red clamped to `1, 0, 0` (± `1e-3`).
  - `SnapperTests/StrokeWidthTests.swift`
    - `testCasesOrder`: `allCases == [.s, .m, .l]`.
    - `testPoints`: 2 / 4 / 8.
    - `testTextPointSize`: 16 / 24 / 36.
    - `testRawValuesAndLabels`: `"s"/"m"/"l"` and `"S"/"M"/"L"`.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` both succeed with no new warnings under Swift 6 strict concurrency.
- **Manual verification.** This spec adds no UI, hotkeys, capture, or permissions. Everything it adds is covered by the unit tests above, so the only manual check is that wiring it into `AppCoordinator` broke nothing:
  1. Build and run the app. The `camera.viewfinder` menu bar icon appears, with no Dock icon, and the menu behaves as after 0001 (left-click menu, right-click Settings, Quit works).
  2. Run `defaults read com.thera-engineering.snapper` in Terminal. None of the `AppSettings.Key` names appear (construction writes nothing). The domain may not exist at all, or may contain only system/KeyboardShortcuts keys.

## Out of scope

- Any Settings UI, the folder picker, the launch-at-login `SMAppService` registration, and whether the toggle reflects `SMAppService.mainApp.status` rather than the stored `launchAtLogin` value: **0010-settings-window**.
- Creating the save folder, writability checks, file naming, and `SaveFolderError`: **0002-file-naming-save-folder**.
- Keyboard shortcut storage and names: **0005-global-shortcuts-app-mode** (`KeyboardShortcuts` owns them).
- Reading `onboardingDismissed` to decide whether to show onboarding, and the "Don't show again" button: **0006-permissions-onboarding**.
- Using `screenshotDestination` and `playShutterSound` when capturing: **0008-screenshot-rectangle-capture**.
- Using `recordMicrophone` and requesting microphone permission: **0010-settings-window** / **0014-microphone-audio**.
- Swatch UI, `NSColorPanel`, `NSColor` conversion, and writing back the last-used color and width: **0018-annotation-editing**. Text-outline luminance rule: **0019-annotation-text-tool**. Image-point → pixel scaling of stroke widths: **0016-annotation-document-undo** / **0017-annotation-window-canvas**.

## Verification status

- Automated verification on **2026-11-02**: `xcodegen generate` succeeded; `xcodebuild -scheme Snapper -configuration Debug build` reported **BUILD SUCCEEDED**; `xcodebuild -scheme Snapper test` reported **TEST SUCCEEDED**, with **75 tests in 10 suites passing** (29 tests added by this spec).
- No Swift compiler warnings. Xcode emitted tooling diagnostics about skipped AppIntents metadata extraction and parsing its bundled XCUIAutomation executable; the hosted test process also logged unavailable macOS service connections. These did not fail the build or tests.
- Reused `SaveFolder.defaultURL`; no product behavior changed, so `docs/PRD.md` and `AGENTS.md` required no edits.
- GUI behavior was not exercised. The hosted unit-test process suppresses app UI; no interactive launch or permission checks were performed.

Manual verification:

- [ ] **Not yet run by user:** Build and run Snapper. Confirm the `camera.viewfinder` menu bar icon appears, no Dock icon appears, left-click opens the menu, right-click opens Settings, and Quit works.
- [ ] **Not yet run by user:** On a fresh preferences domain, run `defaults read com.thera-engineering.snapper` after launching. Confirm none of the eight `AppSettings.Key` names appear; the domain may be absent or contain only system/KeyboardShortcuts keys. Existing preferences from earlier use need not be absent.
