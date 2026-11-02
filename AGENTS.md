# AGENTS.md

Guidance for AI coding agents working in this repository.

## Project

**Snapper** is a macOS menu bar app with three global shortcuts: screenshot (⌘⇧4), screen recording (⌘⇧5), and capture-then-annotate (⌘⇧6).

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
  - the annotation model (shapes, hit-testing, move/resize, undo/redo)
  - the geometry helpers (point↔pixel conversion, display selection, overlay pill placement)
  - file naming (timestamps, collision suffixes)
  
  Views only render the model and forward input to it.
- Annotation shapes are stored in **image pixel coordinates**. Export renders at full image resolution, never at the window's display scale.
- All UI work runs on `@MainActor`. ScreenCaptureKit callbacks must hop back to the main actor before touching UI.
- Our own windows (overlays, recording pill and border) must be **excluded from capture** through `SCContentFilter`, so they never appear in screenshots or recordings.
- A selection is always confined to one display. Convert carefully between AppKit's bottom-left global coordinates and ScreenCaptureKit and CoreGraphics' top-left coordinates. This is the most common source of bugs, so cover it with tests.
  Use `DisplayGeometry` for coordinate conversion, display lookup, drag clamping, and pixel alignment; feature code must not flip Y inline. `DisplayInfo.allCurrent()` is the main-actor AppKit adapter, and capture regions use the selected display's own backing scale.
- Store preferences in `UserDefaults`, behind one typed settings type. Shortcuts are stored by `KeyboardShortcuts`.

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

Rebuilding with a different signature can reset the Screen Recording permission. If capture suddenly returns blank images or errors, check permission before debugging the code.

## Conventions

- Swift API Design Guidelines. Prefer `struct`/`enum` and use `final class` only when reference semantics are needed.
- No force unwraps outside tests, except for truly invariant cases, each with a comment explaining why.
- User-facing strings are in English and kept short and native-sounding.
- Commits are small and focused, with imperative-mood messages. Don't commit unless asked.
