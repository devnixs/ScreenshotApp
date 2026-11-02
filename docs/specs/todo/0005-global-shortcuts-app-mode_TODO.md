# 0005 — Register the three global shortcuts and route them through a pure app-mode state machine

> Source: docs/PRD.md §1, §4, §5 (line 76), §7.4 (lines 100–104), §10 *Shortcuts*, §12 ("All three shortcuts can be rebound in Settings and take effect immediately")
> Depends on: 0001-xcodegen-app-shell, 0003-app-settings

## Context

Not yet implemented. After 0001 the app has a status item (`Snapper/App/StatusItemController.swift`)
with a left-click menu (Screenshot / Record / Annotate / — / Settings… / Quit Snapper), right-click →
Settings, and an `AppCoordinator` (`Snapper/App/AppCoordinator.swift`, `@MainActor final class`) with
stub methods `startScreenshot()`, `startRecording()`, `startAnnotate()`, `openSettings()`. The
KeyboardShortcuts SwiftPM package is already linked into the `Snapper` target by `project.yml`
(0001), but no shortcut is registered and the menu items show no key equivalents.

The PRD requires:

- §1: three global shortcuts, defaults ⌘⇧4 (screenshot), ⌘⇧5 (record), ⌘⇧6 (annotate), all
  rebindable in Settings (§10). §12: rebinding takes effect immediately.
- §4: the menu shows "Screenshot (⌘⇧4)" etc. — "the shortcuts shown always reflect the current
  bindings". Left-click opens the menu, right-click opens Settings. **While recording, clicking the
  icon (either button) stops the recording and saves it.**
- §5 (line 76): while the selection overlay is open, **all Snapper shortcuts are ignored**.
- §7.4: ⌘⇧5 while recording stops and saves; **⌘⇧4 and ⌘⇧6 are ignored while a recording is in
  progress**; Esc does not cancel a recording.

These rules are decision logic, so this spec puts them in a pure, exhaustively unit-tested state
machine (`AppModeMachine`). Every trigger — global shortcut, menu item, status-item click, and the
lifecycle callbacks that later specs (0007 overlay, 0011 recording) will send — goes through it.
AppKit code only feeds events in and performs the returned action.

## User story

As a Snapper user, I can press ⌘⇧4 / ⌘⇧5 / ⌘⇧6 anywhere (or pick the matching menu item) to start a
screenshot, recording, or annotation selection; the menu shows my current bindings; and pressing a
shortcut at the wrong moment (overlay open, or ⌘⇧4/⌘⇧6 during a recording) does nothing, while ⌘⇧5
or clicking the menu bar icon during a recording stops it.

## Scope & changes

### Pure logic (unit-tested, no AppKit, no KeyboardShortcuts import)

- **`Snapper/App/CapturePurpose.swift`** — new:
  ```swift
  enum CapturePurpose: String, CaseIterable, Sendable, Equatable {
      case screenshot, record, annotate
  }
  ```
  The raw values are also the KeyboardShortcuts storage identifiers (see `ShortcutNames`), so they
  **must never change** once shipped (they are persisted as `KeyboardShortcuts_<rawValue>` in
  UserDefaults). Add a doc comment saying so.

- **`Snapper/App/AppModeMachine.swift`** — new. Exactly these types:
  ```swift
  enum AppMode: Equatable, Sendable {
      case idle
      case selecting(CapturePurpose)   // selection overlay is open for this purpose
      case recording                   // from "selection finished for .record" until the session has fully ended
  }

  enum StatusItemButton: Equatable, Sendable { case left, right }

  enum AppModeEvent: Equatable, Sendable {
      case shortcut(CapturePurpose)          // global hotkey pressed
      case menuItem(CapturePurpose)          // Screenshot / Record / Annotate menu item chosen
      case statusItemClicked(StatusItemButton)
      case selectionFinished                 // overlay produced a selection (sent by 0007)
      case selectionCancelled                // Esc, display change, or overlay could not be shown (0006/0007)
      case recordingEnded                    // session fully ended: saved, cancelled, or failed to start/write (sent by 0011)
  }

  enum AppModeAction: Equatable, Sendable {
      case none                              // nothing to do (ignored event, or pure state change)
      case showOverlay(CapturePurpose)       // open the selection overlay for this purpose
      case completeSelection(CapturePurpose) // hand the finished selection to the capture/annotate/record pipeline
      case stopRecording                     // stop and save the current recording
      case openMenu                          // pop up the status-item menu
      case openSettings
  }

  struct AppModeMachine: Sendable {
      private(set) var mode: AppMode = .idle
      /// Applies `event`, updates `mode`, and returns the single action the caller must perform.
      mutating func handle(_ event: AppModeEvent) -> AppModeAction
  }
  ```
  Ignored events return `.none` and leave `mode` unchanged. "Settings…" and "Quit Snapper" menu
  items are **not** routed through the machine (they are always available whenever the menu can be
  shown, and the menu can only be shown in `.idle`).

  **Transition table (normative — implement and test exactly this):**

  | Mode \ Event | `shortcut(p)` / `menuItem(p)` | `statusItemClicked(.left)` | `statusItemClicked(.right)` | `selectionFinished` | `selectionCancelled` | `recordingEnded` |
  |---|---|---|---|---|---|---|
  | `.idle` | → `.selecting(p)`, `.showOverlay(p)` | stay, `.openMenu` | stay, `.openSettings` | stay, `.none` | stay, `.none` | stay, `.none` |
  | `.selecting(q)` | stay, `.none` (PRD §5) | stay, `.none` | stay, `.none` | `q == .record`: → `.recording`, `.completeSelection(.record)`; otherwise → `.idle`, `.completeSelection(q)` | → `.idle`, `.none` | stay, `.none` |
  | `.recording` | `p == .record`: stay, `.stopRecording`; `p == .screenshot`/`.annotate`: stay, `.none` (PRD §7.4) | stay, `.stopRecording` (PRD §4) | stay, `.stopRecording` (PRD §4) | stay, `.none` | stay, `.none` | → `.idle`, `.none` |

  Design notes (put the essentials in doc comments):
  - `.recording` is entered as soon as a `.record` selection finishes, **before** the `SCStream`
    has started, so there is no window in which a second overlay can be opened while a recording
    is starting. If starting fails, 0011 sends `recordingEnded` (after showing its error alert).
    Consequently there is no separate `recordingStarted` event.
  - `.stopRecording` does **not** leave `.recording`: stopping/finalizing is asynchronous, and the
    mode returns to `.idle` only on `recordingEnded`. Repeated stop requests (⌘⇧5 pressed twice,
    icon clicked twice) therefore yield `.stopRecording` each time; 0011's `RecordingSession.stop()`
    must be idempotent. Pill Stop / Cancel buttons (0012) call the session directly; they do not go
    through the machine, and end with `recordingEnded` like every other path.
  - Status-item clicks while `.selecting` are ignored defensively (the overlay normally covers the
    menu bar anyway) — the same rule as shortcuts in PRD §5.
  - Menu items obey exactly the same rules as shortcuts. In practice the menu is only shown in
    `.idle` (left-click while recording stops instead; overlay blocks it while selecting), so the
    menu items are never disabled; the machine still guards them.

### Platform glue (manually verified)

- **`Snapper/App/ShortcutNames.swift`** — new. `import KeyboardShortcuts`:
  ```swift
  extension KeyboardShortcuts.Name {
      static let screenshot = Self(CapturePurpose.screenshot.rawValue, default: .init(.four,  modifiers: [.command, .shift]))
      static let record     = Self(CapturePurpose.record.rawValue,     default: .init(.five,  modifiers: [.command, .shift]))
      static let annotate   = Self(CapturePurpose.annotate.rawValue,   default: .init(.six,   modifiers: [.command, .shift]))
  }
  extension CapturePurpose {
      var shortcutName: KeyboardShortcuts.Name { switch self { … } }
  }
  ```
  Shortcuts are stored only by KeyboardShortcuts (AGENTS.md); `AppSettings` (0003) gets **no**
  shortcut keys.

- **`AppCoordinator`** (`Snapper/App/AppCoordinator.swift`, from 0001) — modify:
  - Own `private var modeMachine = AppModeMachine()` and expose `var mode: AppMode { modeMachine.mode }`
    (read-only, for later specs and the status icon in 0011).
  - Add the single entry point `func send(_ event: AppModeEvent)` which calls
    `modeMachine.handle(event)`, logs `event`, old/new mode and action at `.info` level through
    `os.Logger(subsystem: "com.thera-engineering.snapper", category: "AppMode")`, then performs the
    action via `private func perform(_ action: AppModeAction)`.
  - Replace the 0001 stubs `startScreenshot()` / `startRecording()` / `startAnnotate()` with this
    entry point (callers send `.menuItem(p)` / `.shortcut(p)`). `openSettings()` stays as is.
  - `perform`:
    - `.showOverlay(p)` → `beginSelection(for: p)`. **Stub in this spec:** log
      `"Overlay requested for <p> (not implemented until 0007)"` and then immediately
      `send(.selectionCancelled)` so the mode returns to `.idle` and the app stays usable. Mark the
      top of `beginSelection(for:)` with a `// Permission gate (0006): …` comment — 0006 inserts
      "if Screen Recording permission is missing, open onboarding and `send(.selectionCancelled)`"
      there. 0007 replaces the stub with the real overlay.
    - `.completeSelection(p)` → log only (unreachable in this spec; 0008/0011/0017 fill it in).
    - `.stopRecording` → log only (0011 calls `RecordingSession.stop()`).
    - `.openMenu` → `statusItemController.showMenu()`.
    - `.openSettings` → `openSettings()`.
    - `.none` → nothing.
  - In `start()` (or wherever 0001 wires things up at launch), register the hotkeys:
    ```swift
    for purpose in CapturePurpose.allCases {
        KeyboardShortcuts.onKeyDown(for: purpose.shortcutName) { [weak self] in
            self?.send(.shortcut(purpose))
        }
    }
    ```
    **Use `onKeyDown`, not `onKeyUp`:** it matches the system screenshot shortcuts (which react on
    press), makes the overlay appear without waiting for release, and is safe against repeats
    because the machine ignores shortcuts while `.selecting` or (for ⌘⇧4/⌘⇧6) `.recording`. The
    handlers are already called on the main thread by KeyboardShortcuts; the closure must be
    `@MainActor`-isolated (wrap in `MainActor.assumeIsolated` only if the compiler requires it under
    Swift 6 strict concurrency). Register once; KeyboardShortcuts re-registers the Carbon hotkey by
    itself when the binding changes, which gives PRD §12 "take effect immediately" for free.

- **`StatusItemController`** (`Snapper/App/StatusItemController.swift`, from 0001) — modify:
  - Mouse clicks on the status button no longer decide what to do themselves. The button's action
    (sent on `[.leftMouseUp, .rightMouseUp]`) reads `NSApp.currentEvent?.type` and calls
    `coordinator.send(.statusItemClicked(.right))` for right-mouse-up (and for ⌃-left-click, the
    macOS convention for a secondary click) and `.statusItemClicked(.left)` otherwise. The
    controller must not keep `statusItem.menu` permanently assigned (that would bypass the click
    handler); keep the menu in a property.
  - Add `func showMenu()` that pops up the stored menu under the status button (e.g. temporarily
    assign `statusItem.menu`, call `button.performClick(nil)`, and clear `statusItem.menu` in
    `menuDidClose(_:)`; or `menu.popUp(positioning:at:in:)`). Keep whatever technique 0001 used if
    it already satisfies this.
  - Menu items Screenshot / Record / Annotate call `coordinator.send(.menuItem(p))`.
  - **Live key equivalents:** call `item.setShortcut(for: purpose.shortcutName)` (KeyboardShortcuts'
    `NSMenuItem` extension) on each of the three items when the menu is built. It sets
    `keyEquivalent`/`keyEquivalentModifierMask` from the current binding, clears them when the
    shortcut is unbound, and observes binding changes so the menu updates immediately. Titles stay
    "Screenshot", "Record", "Annotate" — the shortcut appears in the standard key-equivalent column,
    which is the native rendering of PRD §4's "Screenshot (⌘⇧4)". No custom mapping code is needed,
    so no pure `Shortcut → keyEquivalent` helper is extracted.

- No `project.yml` changes beyond adding the new source files; run `xcodegen generate` because
  files are added.
- No PRD change: every rule implemented here is already in docs/PRD.md §4, §5, §7.4.

## Acceptance criteria

- [ ] With the macOS screenshot shortcuts disabled, pressing ⌘⇧4 / ⌘⇧5 / ⌘⇧6 anywhere logs a
      `.shortcut(...)` event and the `.showOverlay(...)` stub for the matching purpose, then returns to `.idle`.
- [ ] Choosing Screenshot / Record / Annotate in the menu produces the same log with `.menuItem(...)`.
- [ ] The menu shows ⌘⇧4, ⌘⇧5, ⌘⇧6 next to Screenshot, Record, Annotate by default; a changed binding
      shows the new keys; an unbound shortcut shows no key equivalent.
- [ ] Left-click on the icon opens the menu; right-click (or ⌃-click) opens Settings — now routed via
      the machine.
- [ ] `AppModeMachine` implements the transition table above exactly; all cells are covered by unit tests.
- [ ] Shortcut name identifiers are `"screenshot"`, `"record"`, `"annotate"`.
- [ ] App builds with Swift 6 strict concurrency and all tests pass.

## Edge cases

- **macOS screenshot shortcuts still enabled:** the system grabs ⌘⇧4/⌘⇧5 first and Snapper's handler
  never fires. Expected until 0006's onboarding asks the user to disable them; not a bug in this spec.
- **Shortcut held down / key repeat:** a repeated key-down while `.selecting` is ignored by the machine.
- **Shortcut pressed while the status menu is open:** the menu item's key equivalent may fire instead
  of (or in addition to) the hotkey; both route through the machine, so at most one overlay is
  requested (the second event arrives in `.selecting` and is ignored). With the stub, which returns to
  `.idle` synchronously, two log lines are acceptable.
- **Unbound shortcut:** `onKeyDown` simply never fires; the menu item has no key equivalent but still works.
- **Two Snapper actions bound to the same keys:** prevented/flagged by Settings in 0010
  (`ShortcutConflictDetector`); not handled here.
- **Stale lifecycle events** (`selectionFinished`/`selectionCancelled` in `.idle` or `.recording`,
  `recordingEnded` outside `.recording`): ignored, mode unchanged.
- **Events re-entering `send` from inside `perform`** (the stub sends `.selectionCancelled`
  synchronously): `send` must apply the machine transition *before* performing the action, so the
  nested call sees the updated mode.

## Test plan

- **Unit tests** (`SnapperTests/AppModeMachineTests.swift`). Helper: build a machine in a given mode by
  replaying events (`.idle` → fresh; `.selecting(p)` → `shortcut(p)`; `.recording` →
  `shortcut(.record)`, `selectionFinished`). For each case assert both the returned action and the
  resulting `mode`.
  - Idle:
    - `shortcut(p)` for each of the 3 purposes → `.showOverlay(p)`, mode `.selecting(p)`.
    - `menuItem(p)` for each purpose → same as shortcut.
    - `statusItemClicked(.left)` → `.openMenu`, stays `.idle`.
    - `statusItemClicked(.right)` → `.openSettings`, stays `.idle`.
    - `selectionFinished`, `selectionCancelled`, `recordingEnded` → `.none`, stays `.idle`.
  - Selecting (for each of the 3 selecting purposes q):
    - `shortcut(p)` and `menuItem(p)` for all 3 p → `.none`, mode unchanged (PRD §5: 3 × 3 × 2 = 18 asserts).
    - `statusItemClicked(.left)` / `(.right)` → `.none`, unchanged.
    - `selectionCancelled` → `.none`, mode `.idle`.
    - `recordingEnded` → `.none`, unchanged.
    - `selectionFinished`: q = `.screenshot` → `.completeSelection(.screenshot)`, `.idle`;
      q = `.annotate` → `.completeSelection(.annotate)`, `.idle`;
      q = `.record` → `.completeSelection(.record)`, `.recording`.
  - Recording:
    - `shortcut(.record)` → `.stopRecording`, stays `.recording`; a second `shortcut(.record)` → `.stopRecording` again.
    - `menuItem(.record)` → `.stopRecording`, stays `.recording`.
    - `shortcut(.screenshot)`, `shortcut(.annotate)`, `menuItem(.screenshot)`, `menuItem(.annotate)` → `.none`, stays `.recording` (PRD §7.4).
    - `statusItemClicked(.left)` and `statusItemClicked(.right)` → `.stopRecording`, stays `.recording` (PRD §4).
    - `selectionFinished`, `selectionCancelled` → `.none`, stays `.recording`.
    - `recordingEnded` → `.none`, mode `.idle`; then `shortcut(.screenshot)` → `.showOverlay(.screenshot)`.
  - Full cycles: idle → `shortcut(.annotate)` → `selectionCancelled` → idle → `menuItem(.screenshot)` →
    `selectionFinished` → idle (actions in order: showOverlay, none, showOverlay, completeSelection).
  - Exhaustiveness: a table-driven test iterating every `AppMode` value (5: idle, 3 selecting, recording)
    × every `AppModeEvent` value (11: 3 shortcut, 3 menuItem, 2 status clicks, 3 lifecycle) — 55
    rows — asserting the expected (action, mode) from a literal expected table in the test.
- **Unit tests** (`SnapperTests/CapturePurposeTests.swift`):
  - `CapturePurpose.allCases` == `[.screenshot, .record, .annotate]`.
  - raw values are exactly `"screenshot"`, `"record"`, `"annotate"` (persisted identifiers; changing
    them would lose users' custom bindings).
  - Testing `KeyboardShortcuts.Name` defaults is **not** required in unit tests (it would need the test
    target to import KeyboardShortcuts); the defaults are checked manually via the menu below.
- **Build check:** `xcodegen generate`, then
  `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (real GUI session; can't be automated):
  1. In System Settings → Keyboard → Keyboard Shortcuts → Screenshots, turn off "Save picture of
     selected area as a file" (⌘⇧4), "Screenshot and recording options" (⌘⇧5) and any other entry bound
     to ⌘⇧4/⌘⇧5/⌘⇧6.
  2. In Terminal run
     `log stream --level info --predicate 'subsystem == "com.thera-engineering.snapper" AND category == "AppMode"'`.
  3. Launch Snapper. Left-click the menu bar icon: the menu shows Screenshot ⌘⇧4, Record ⌘⇧5,
     Annotate ⌘⇧6, then Settings… and Quit Snapper. The log shows `statusItemClicked(left)` → `openMenu`.
  4. Close the menu. Right-click the icon: Settings opens; log shows `openSettings`. Close it; ⌃-click
     the icon: Settings opens again.
  5. With another app (e.g. Finder) frontmost, press ⌘⇧4, ⌘⇧5, ⌘⇧6 in turn. Each produces
     `shortcut(<purpose>)`, `idle → selecting(<purpose>)`, the "Overlay requested … (not implemented
     until 0007)" line, then `selectionCancelled` → `idle`. Nothing else happens.
  6. Choose Screenshot, Record, Annotate from the menu: same log lines with `menuItem(<purpose>)`.
  7. Rebinding (persistence + menu display only — the Settings recorder UI and the *immediate* rebind
     check arrive in 0010): Quit Snapper, then run
     `defaults write com.thera-engineering.snapper KeyboardShortcuts_screenshot -string '{"carbonKeyCode":26,"carbonModifiers":768}'`
     (⌘⇧7) and
     `defaults write com.thera-engineering.snapper KeyboardShortcuts_annotate -bool false` (unbound).
     (If the resolved KeyboardShortcuts version stores bindings differently, check its
     `KeyboardShortcuts.swift` `userDefaultsSet`/`userDefaultsDisable` and adapt.) Relaunch: the menu
     shows Screenshot ⌘⇧7, Record ⌘⇧5, Annotate with no shortcut. ⌘⇧7 logs `shortcut(screenshot)`;
     ⌘⇧4 and ⌘⇧6 log nothing; choosing Annotate from the menu still logs `menuItem(annotate)`.
  8. Restore defaults: quit, `defaults delete com.thera-engineering.snapper KeyboardShortcuts_screenshot`
     and `… KeyboardShortcuts_annotate`, relaunch, and confirm the menu shows ⌘⇧4/⌘⇧5/⌘⇧6 again.
  - Not manually verifiable in this spec (covered by unit tests only, and to be verified for real in
    0007 and 0011): shortcuts ignored while the overlay is open; ⌘⇧4/⌘⇧6 ignored and ⌘⇧5 / icon click
    stopping during a recording.
  - Deferred to 0010: "rebound shortcuts take effect immediately" (PRD §12) via the Settings recorder.

## Out of scope

- Permission check before showing the overlay and the onboarding window — 0006 (seam left in
  `beginSelection(for:)`).
- The selection overlay itself, Esc handling, display-change cancellation — 0007.
- Performing screenshot capture / annotate / recording on `.completeSelection` — 0008, 0017, 0011.
- Stopping the recording, red `record.circle` icon while recording — 0011. Pill Stop/Cancel — 0012.
- Settings → Shortcuts tab with recorder fields and conflict warnings — 0010; system-shortcut conflict
  detection for onboarding — 0006.
