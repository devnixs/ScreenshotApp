# AGENTS.md

Guidance for AI coding agents working in this repository.

## Project

**Snapper** is a macOS menu bar app with three global shortcuts: screenshot (⌘⇧4), screen recording (⌘⇧5), and capture-then-annotate (⌘⇧6). A native Windows version with the same features lives in `windows/` (see "Windows app" below). Unless a section says it's about Windows, the rules in this file apply to the macOS app.

**The source of truth for behavior is [`docs/PRD.md`](docs/PRD.md).** Read it before implementing a feature. If the code and the PRD disagree, or the PRD is silent on a decision, ask the user instead of guessing. Update the PRD in the same change whenever a decision changes.

## Stack

- Swift 6 with strict concurrency, minimum deployment target **macOS 15**. Developed with Xcode 26.
- **AppKit** for the menu bar (`NSStatusItem`), selection and recording overlays (`NSPanel`/`NSWindow`), and the annotation canvas (`NSView`).
- **SwiftUI** only for the Settings and onboarding windows.
- **ScreenCaptureKit** for all capture: `SCScreenshotManager` for images, and `SCStream` with `SCRecordingOutput` for video. Do not use the deprecated `CGWindowListCreateImage` or `CGDisplayStream`.
- The only dependency is **KeyboardShortcuts** (sindresorhus), added via SwiftPM. Do not add other dependencies without asking.
- Not sandboxed. `LSUIElement = YES` (no Dock icon).

## Project layout

```
project.yml              # XcodeGen spec — the source of truth for the Xcode project
Snapper/
  App/                   # App entry, AppDelegate, status item, menu
  Capture/               # Selection overlay, window hit-testing, screenshot capture
  Recording/             # Recording session, overlay pill, border
  Annotate/              # Annotation window, canvas view, tools
    Model/               # Pure shape model + document + undo (no AppKit drawing)
  Settings/              # SwiftUI settings + onboarding, preference keys
  Support/               # File naming, save folder, clipboard, permissions
SnapperTests/            # Unit tests
docs/PRD.md
```

- `project.yml` is the source of truth, and `*.xcodeproj` is generated from it, so don't edit `project.pbxproj` by hand. After adding or removing files or targets, run `xcodegen generate`.
- The `.xcodeproj` is git-ignored.

## Commands

```sh
xcodegen generate                                        # regenerate Snapper.xcodeproj
xcodebuild -scheme Snapper -configuration Debug build    # build
xcodebuild -scheme Snapper test                          # run unit tests
```

Run the build and the tests before reporting work as done. Report failures as they are; don't paper over them.

## Architecture rules

- **Keep logic testable and separate from AppKit.** The following are pure Swift types with unit tests:
  - the annotation model (shapes, hit-testing, move/resize, undo/redo), save-name defaults, and unsaved close/quit policies
  - full-resolution annotation raster export (headless bitmap tests; shared AppKit renderer and exporter run on the main actor)
  - the geometry helpers (point↔pixel conversion, display selection, overlay pill placement, annotation canvas transforms and window sizing)
  - text commit decisions, contrast outlines, font/placement metrics, and text click routing (with injected text measurement for selected-shape size changes)
  - annotation tool routing, drawing and editing drag state (view-point thresholds, image-edge clamping, Shift constraints), selection hit-testing, and toolbar style decisions
  - file naming (timestamps, collision suffixes)
  - Settings decisions (shortcut conflicts/duplicates, microphone authorization, launch-at-login state)
  - window candidate ordering/hit-testing, display assignment, and selection mode/press state
  - recording configuration, export plans/bitrates, GIF sizing/frame sampling, lifecycle/effects (including conversion), startup callback ordering, deferred-termination decisions, and window polling/parser/tracking state
  - headless MP4 compression (dimensions, variable frame timing, duration, and unchanged encoded audio) and GIF encoding (dimensions, colors, frame delays, and looping)
  
  Views only render the model and forward input to it.
- Annotation shapes are stored in **image pixel coordinates**. Export renders at full image resolution, never at the window's display scale.
- All UI work runs on `@MainActor`. ScreenCaptureKit callbacks must hop back to the main actor before touching UI.
- Our own windows (overlays, recording pill and border) must be **excluded from capture** through `SCContentFilter`, so they never appear in screenshots or recordings.
- A rectangle selection is confined to its starting display. Window selections capture the whole window, even across displays; `WindowDisplayRule` assigns the display with the largest overlap. Convert carefully between AppKit's bottom-left global coordinates and ScreenCaptureKit and CoreGraphics' top-left coordinates. This is the most common source of bugs, so cover it with tests.
  Use `DisplayGeometry` for coordinate conversion, display lookup, drag clamping, and pixel alignment; feature code must not flip Y inline. `DisplayInfo.allCurrent()` is the main-actor AppKit adapter, and capture regions use the selected display's own backing scale.
- Store preferences in `UserDefaults`, behind one typed settings type. Shortcuts are stored by `KeyboardShortcuts`.

## Windows app (`windows/`)

The Windows version is a separate native C# app in `windows/` with the same features. `docs/PRD.md` §13 lists every Windows difference. The macOS Swift sources and the specs in `docs/specs/done/` are the **reference implementation**. When porting a feature, read the matching Swift types and tests and port their behavior and test cases. Don't redesign them.

### Stack

- C# on **.NET 10**, nullable enabled, warnings as errors. Minimum OS **Windows 11** (build 22000).
- **WPF** for all windows. The tray icon uses the WinForms `NotifyIcon` from .NET (`UseWindowsForms`, with the global `System.Windows.Forms`/`System.Drawing` usings removed to avoid ambiguities with WPF).
- **Windows.Graphics.Capture** for all capture, **Media Foundation** for MP4 encode/re-encode and microphone audio, **WIC** for PNG/GIF, and `RegisterHotKey` for global shortcuts. Don't use GDI `BitBlt`/`PrintWindow` or Desktop Duplication for capture.
- Dependencies: **Microsoft.Windows.CsWin32** (all Win32/COM/D3D11/Media Foundation/WIC interop, generated from `NativeMethods.txt`) and **xUnit** (tests only). Don't add others without asking. Versions are pinned centrally in `windows/Directory.Packages.props`.
- Unpackaged, self-contained single-file publish for win-x64 and win-arm64. Per-Monitor V2 DPI awareness is declared in `app.manifest`.

### Layout

```
windows/
  Snapper.slnx
  Directory.Build.props        # shared C# settings (LangVersion, Nullable, TreatWarningsAsErrors)
  Directory.Packages.props     # central package versions
  src/Snapper.Core/            # net10.0: pure logic, no Windows or WPF APIs (runs and tests on any OS)
    Geometry/ App/ Capture/ Recording/ Annotate/Model/ Settings/ Support/
  src/Snapper.Windows/         # net10.0-windows10.0.22621.0 WinExe: WPF + tray + interop
    App/ Capture/ Recording/ Annotate/ Settings/ Support/ Interop/  app.manifest  NativeMethods.txt
  tests/Snapper.Core.Tests/    # net10.0 xUnit tests for Snapper.Core
  tools/Snapper.IconGen/      # regenerates src/Snapper.Windows/Assets/Snapper.ico
  artifacts/                 # publish output (git-ignored)
  README.md
```

Namespaces follow the folders (`Snapper.Core.Annotate.Model`, `Snapper.Windows.Capture`, …).

### Commands (they run on macOS too)

```sh
dotnet build windows/Snapper.slnx -c Debug       # builds Core, tests, and the WPF app (EnableWindowsTargeting)
dotnet test windows/Snapper.slnx                 # runs Snapper.Core.Tests
dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64   # or win-arm64 → windows/artifacts/publish/<rid>/Snapper.exe
dotnet run --project windows/tools/Snapper.IconGen -- windows/src/Snapper.Windows/Assets/Snapper.ico
```

`Snapper.Windows` sets `EnableWindowsTargeting=true`, so it compiles on macOS but can only run on Windows. Debug builds default to `win-x64` (override with `-r win-arm64`) so cross-builds also emit a Windows `Snapper.exe`. The test project opts out of the xUnit package’s default Microsoft.Testing.Platform application mode to keep the documented VSTest command working. Every Windows spec must leave `dotnet build` and `dotnet test` green. GUI behavior is verified manually on a Windows 11 machine.

### Architecture rules (Windows)

- **Pure logic lives in `Snapper.Core`** and has xUnit tests. Use the same split as the macOS list above: the annotation model and undo, geometry, file naming, settings decisions, the app-mode machine, selection, window hit-testing, recording decisions, and pill placement. `Snapper.Core` must not reference WPF, WinForms, WinRT, or interop types. It uses its own primitives from `Snapper.Core.Geometry` (`PointD`, `SizeD`, `RectD`, `PixelRect`, …). Screenshot crop/row-copy helpers, output ordering/retry/error decisions, DIB packing, and deterministic shutter synthesis also live in Core with tests. Views only render the model and forward input.
- **Coordinates:** global positions are **physical pixels** in virtual-screen coordinates (top-left origin, y down). DIPs exist only inside a window and are converted with that window's monitor DPI. Use `DisplayGeometry` (Core) for every pixel↔DIP conversion, monitor lookup, clamping, and pixel alignment. Position Snapper's own windows in physical pixels with `SetWindowPos`, not with WPF `Left`/`Top`. Mixed-DPI monitors are the most common source of bugs, so cover them with tests (100%/150%/200%, negative-origin monitors).
- **Threading:** all UI runs on the WPF dispatcher thread. Capture (`FrameArrived`), Media Foundation, and audio callbacks run elsewhere and must marshal to the dispatcher before touching UI. Encoding never blocks the UI thread. Acquire the shared `Direct3D11Helper` through a disposable device lease so in-flight captures retain their device generation across a reset. D3D device/context calls in free-threaded capture callbacks use retained CsWin32 native interfaces; do not marshal UI-apartment RCWs into those callbacks.
- Recording uses a dedicated MTA writer thread and one lock for every `IMFSinkWriter` call. Capture feeds a bounded sample queue and never waits for encoding. Recording device leases request video support, share `ContextLock` for immediate-context calls, and stay alive until the writer releases the file. Cancelled/failed-start files are deleted after that release, including after a timeout. Only forced `WM_ENDSESSION` handling may synchronously wait on the UI thread.
- GPU-allocated recording buffers must set their valid-data length before `WriteSample`, including held frames. Initial decoder alignment notifications refresh the decoded layout/crop while preserving visible output dimensions; later format changes remain errors. The compatibility decision stays in Core with tests.
- Overlays, the recording pill, and the border call `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, and all Snapper windows are skipped by window hit-testing.
- Annotation shapes are stored in **image pixel coordinates**. One renderer draws both the canvas and the export, and export renders at 96 DPI to the image's pixel size.
- Preferences go through one typed settings type persisted as JSON (`%APPDATA%\Snapper\settings.json`), with the file path injectable for tests.
- Wrap native handles and COM objects (`SafeHandle`, `using`, `Marshal.ReleaseComObject` where needed). No `!` null-forgiving operator outside tests, except for truly invariant cases, each with a comment.
- Namespace pitfalls: the WPF application class is `SnapperApplication`. A class named `App` clashes with the `Snapper.Windows.App` namespace. Inside `namespace Snapper.Windows…`, an unqualified `Windows.*` resolves to `Snapper.Windows.*`, so import WinRT and CsWin32 namespaces with top-of-file `using` directives or write them as `global::Windows.…`.
- WPF owns process DPI through `app.manifest`. Suppress only WinForms analyzer `WFO0003` in the Windows app project: WinForms supplies the tray, while removing the manifest would break WPF Per-Monitor V2 awareness.
- The app is published single-file: never use `Assembly.Location`. Use `Environment.ProcessPath`/`AppContext.BaseDirectory`. Keep the Release/RID-conditional publish group in `Snapper.Windows.csproj` and the two `.pubxml` profiles in sync.
- User-facing strings follow PRD §13. Don't reuse macOS wording that names Mac concepts (Finder, ⌘, System Settings).

## Spec workflow

Work is planned and built through specs in `docs/specs/`:
- `todo/NNNN-<slug>_TODO.md`: ready-to-build specs. Numbers are global, never reused, and in dependency order.
- `done/NNNN-<slug>_COMPLETED.md`: shipped specs, with a *Verification status* section.

The **spec-writer** skill (`.agents/skills/spec-writer/`) writes specs from the PRD or a request without implementing them. The **spec-implementer** skill (`.agents/skills/spec-implementer/`) builds the todo queue in order, one sub-agent per spec, then commits each spec and moves it to `done/`. Reference the spec ID in commit messages (`0004: …`).

## Things that can't be automated

Screen Recording permission, global hotkeys, overlays, and recording all need a real GUI session with TCC permission granted. Unit tests can't cover them. When a change touches these areas:
1. Build the app.
2. List the manual verification steps for the user, taken from the PRD's acceptance criteria.
3. Never claim those paths are verified unless they were actually run.

For Windows specs, the same rule applies: run `dotnet build` and `dotnet test` (both work on macOS), and list manual steps to run on a Windows 11 machine, taken from PRD §12/§13.11.

Rebuilding with a different signature can reset the Screen Recording permission. If capture suddenly returns blank images or errors, check permission before debugging the code.

## Conventions

- Swift API Design Guidelines. Prefer `struct`/`enum` and use `final class` only when reference semantics are needed.
- No force unwraps outside tests, except for truly invariant cases, each with a comment explaining why.
- User-facing strings are in English and kept short and native-sounding.
- Commits are small and focused, with imperative-mood messages. Don't commit unless asked.
