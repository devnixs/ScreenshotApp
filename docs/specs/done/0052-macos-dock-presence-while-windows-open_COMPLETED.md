# 0052 — Show Snapper in the Dock and ⌘Tab while one of its windows is open

> Source: docs/PRD.md §3 (*Dock and ⌘Tab while a window is open*, added in this change), §12 (updated
> and new criteria); user bug report ("the setup window doesn't show up when I ⌘Tab, and it doesn't
> show up in the Dock, which makes it hard to find when I switch focus to another window")
> Depends on: 0001-xcodegen-app-shell (app delegate, test target). Independent of 0051-macos-app-icon,
> but the Dock icon only looks right once 0051 is done.

## Context
Snapper is an agent app: `INFOPLIST_KEY_LSUIElement: YES` (`project.yml:32`) and
`NSApp.setActivationPolicy(.accessory)` at launch (`Snapper/App/AppDelegate.swift:9`). An
`.accessory` app has no Dock icon and is never listed in the ⌘Tab switcher. So once the user clicks
another app, Snapper's windows are only reachable by hunting for them on screen. This affects every
real window Snapper opens:

- **Onboarding**: `Snapper/Settings/OnboardingWindowController.swift:15-42` (created lazily, reused,
  `isReleasedWhenClosed = false`, already the window's delegate, `windowWillClose` at `:48`). It's
  shown at launch from `Snapper/App/AppCoordinator.swift:34-38` and when a shortcut fires without
  permission (`:61-63`, `:216`, all via `showOnboarding()` at `:273`).
- **Settings**: `Snapper/Settings/SettingsWindowController.swift:7-27` (lazy, reused,
  `isReleasedWhenClosed = false`, **no delegate today**). Opened via `openSettings()`
  (`AppCoordinator.swift:275`).
- **Annotation windows**: `Snapper/Annotate/AnnotationWindowManager.swift:14-33` creates one
  `AnnotationWindowController` per capture; the controller is the window delegate
  (`Snapper/Annotate/AnnotationWindowController.swift:38-41`), `windowShouldClose` (`:210`) can veto
  the close with "Discard changes?", and `windowWillClose` (`:294-300`) calls `onClose`.

The app already builds a full main menu (`AppDelegate.swift:11-14`, `Snapper/App/MainMenuLayout.swift`)
that only becomes visible when Snapper is a regular app.

macOS lists an app in ⌘Tab only if its activation policy is `.regular`, which also gives it a Dock
icon. There's no supported way to get one without the other.

**User decisions (2026-11-04), recorded in PRD §3 and §12:**
1. While **at least one onboarding, Settings, or annotation window is open** (minimized counts),
   Snapper runs as `.regular`: Dock icon, ⌘Tab entry, menu bar menus. When the last one closes, it
   goes back to `.accessory`. Selection overlays, the recording pill/border, the recording anchor
   panel, and `NSAlert`s don't count.
2. **Clicking the Dock icon** brings Snapper's windows to the front and restores a minimized window
   when none is visible (standard macOS reopen behavior).

## User story
As a Snapper user, when I switch away from the setup, Settings, or an annotation window, I can get
back to it with ⌘Tab or by clicking Snapper in the Dock. When I close those windows, Snapper
disappears from the Dock and ⌘Tab again and lives only in the menu bar.

## Scope & changes

### Pure logic (unit-tested) — `Snapper/App/DockPresence.swift` (new)
- `enum DockPresenceWindowKind: Hashable, Sendable { case onboarding, settings, annotation }` — kept
  for logging/readability; all three kinds count equally.
- `struct DockPresence: Equatable, Sendable` tracking the set of open tracked windows by an opaque
  `ObjectIdentifier` (or any `Hashable` token; the type is generic-free and takes
  `ObjectIdentifier` so tests can use dummy objects):
  - `private(set) var openWindows: Set<ObjectIdentifier>`
  - `var wantsRegularApp: Bool { !openWindows.isEmpty }`
  - `mutating func windowOpened(_ id: ObjectIdentifier) -> DockPresenceChange?` — inserts; returns
    `.becomeRegular` only when the set goes from empty to non-empty, else `nil` (re-opening an already
    tracked window is a no-op).
  - `mutating func windowClosed(_ id: ObjectIdentifier) -> DockPresenceChange?` — removes; returns
    `.becomeAccessory` only when the set goes from non-empty to empty, else `nil` (closing an
    untracked or already-removed window is a no-op).
  - `enum DockPresenceChange: Equatable, Sendable { case becomeRegular, becomeAccessory }`
- `enum DockReopenDecision` with
  `static func action(hasVisibleWindows: Bool, hasMinimizedTrackedWindow: Bool) -> DockReopenAction`
  returning `.bringToFront` when a tracked window is visible, `.deminiaturizeOne` when none is visible
  but one is minimized, and `.nothing` otherwise (accessory state; the Dock icon shouldn't exist).

### Platform glue (manual verification) — `Snapper/App/DockPresenceController.swift` (new)
- `@MainActor final class DockPresenceController` owning a `DockPresence` and applying its changes:
  - `func track(_ window: NSWindow)`: calls `windowOpened`; on `.becomeRegular` it calls
    `NSApp.setActivationPolicy(.regular)` **before** the caller's `NSApp.activate()` /
    `makeKeyAndOrderFront`, so the window comes up with Snapper active and its menu bar showing.
    It subscribes once per window to `NSWindow.willCloseNotification` (object: that window) and
    calls `windowClosed` from it, removing the observer afterwards. Using the notification (rather
    than each controller's delegate) covers the Settings window, which has no delegate, and fires
    only for real closes — a vetoed "Discard changes?" close never posts it.
  - On `.becomeAccessory`: `NSApp.setActivationPolicy(.accessory)`. Don't explicitly activate
    another app; macOS hands focus to the next app as it does today.
  - If the menu bar doesn't appear on the first `.accessory → .regular` switch (a known AppKit
    quirk), the implementer may re-activate after the policy change; record what was needed in the
    commit message. Don't add delays or `DispatchQueue.asyncAfter` hacks without noting why.
  - `func reopen() -> Bool` implementing `DockReopenDecision` over the tracked windows
    (`isVisible`, `isMiniaturized`): bring the most recently tracked visible window to front with
    `NSApp.activate()` + `makeKeyAndOrderFront`, or `deminiaturize` the most recently tracked
    minimized window. Returns `false` (handled) per `applicationShouldHandleReopen` semantics.
- Owned by `AppCoordinator` (lazy, like the other controllers). Wire it in:
  - `OnboardingWindowController.show()` (`OnboardingWindowController.swift:36-41`), `SettingsWindowController.show()`
    (`SettingsWindowController.swift:23-27`), and `AnnotationWindowManager.open` (`AnnotationWindowManager.swift:26-29`):
    call `dockPresence.track(window)` right before `NSApp.activate()`. Pass the controller in through
    each initializer (or a closure) — don't make it a global singleton. Tracking a window that's
    already tracked (Settings/onboarding re-shown while open) must be a no-op.
  - `AppDelegate.applicationShouldHandleReopen(_:hasVisibleWindows:)` (new) forwards to
    `coordinator?.handleDockReopen()` → `dockPresenceController.reopen()`.
- Launch policy is unchanged: `LSUIElement = YES` and `.accessory` at launch stay, so a launch with
  no onboarding never flashes a Dock icon. When onboarding shows at launch, `track` switches to
  `.regular` right there.
- Not tracked: `SelectionOverlayController` panels, `RecordingPillPanel`, `RecordingBorderWindow`, the
  recording anchor `NSPanel` (`RecordingSession.swift:208`), and `NSAlert`s from
  `Snapper/Support/ErrorAlerts.swift` / `AppCoordinator.swift:183-190`.
- Files added → run `xcodegen generate`.

### PRD
- docs/PRD.md §3 updated: *Distribution* now says "no Dock icon by default", and a new
  *Dock and ⌘Tab while a window is open* bullet records decisions 1–2.
- docs/PRD.md §12: the first criterion now reads "…with no Dock icon, while none of its windows are
  open", and a new criterion covers Dock/⌘Tab presence while a window is open.

## Acceptance criteria
- [ ] With no onboarding, Settings, or annotation window open, Snapper has no Dock icon and isn't in
      ⌘Tab (unchanged from today).
- [ ] When the onboarding window opens (at launch or via a shortcut without permission), Snapper
      appears in the Dock and in ⌘Tab, and ⌘Tab to Snapper brings the onboarding window to front.
- [ ] Same for the Settings window and for annotation windows.
- [ ] With several tracked windows open, closing one keeps the Dock icon; closing the last one removes
      it and Snapper leaves ⌘Tab.
- [ ] A minimized annotation window keeps the Dock icon; clicking the Dock icon restores it.
- [ ] Clicking the Dock icon with a tracked window behind other apps brings it to front.
- [ ] Cancelling "Discard changes?" keeps the window and the Dock icon.
- [ ] Taking a plain screenshot or recording (overlay, pill, border) never shows a Dock icon.
- [ ] While regular, Snapper's menu bar menus (Snapper / File / Edit …) show when it's active, and
      Quit from the Dock menu or ⌘Q goes through the existing quit flow (unsaved-annotations prompt,
      recording stop).

## Edge cases
- Settings or onboarding `show()` called while already open: no duplicate tracking, no policy flip.
- Re-showing Settings after it was closed: the same `NSWindow` instance is reused
  (`isReleasedWhenClosed = false`), so it must be tracked again and re-subscribed to `willClose`.
- Annotation close vetoed by "Discard changes?": no `willClose`, state unchanged.
- Copy & Close (⌘↩) and Save-then-close: normal close path, counted as closed.
- Onboarding closed by "Don't show again" (`OnboardingWindowController.swift:26-29`): normal close.
- Switching to `.accessory` while an `NSAlert` from `ErrorAlerts` is up after the last window closed:
  the alert stays usable (it's modal and activates the app itself, `ErrorAlerts.swift:10,20`).
- Quit with tracked windows open: no policy change needed; termination proceeds as today.
- A shortcut-triggered selection overlay while Snapper is `.regular` (e.g. Settings open): the
  overlay still works and the policy doesn't change.
- Unit test host: `AppDelegate` returns early under tests (`AppDelegate.swift:8`); `DockPresence`
  tests must not touch `NSApp`.

## Test plan
- **Unit tests** (`SnapperTests/DockPresenceTests.swift`):
  - `testStartsAccessory`: a new `DockPresence` has `wantsRegularApp == false`.
  - `testFirstOpenBecomesRegular`: opening A returns `.becomeRegular`.
  - `testSecondOpenIsNoChange`: opening A then B returns `nil` for B.
  - `testReopenSameWindowIsNoChange`: opening A twice returns `nil` the second time; the set has 1 item.
  - `testClosingOneOfTwoIsNoChange`: open A, B; close A → `nil`, still `wantsRegularApp`.
  - `testClosingLastBecomesAccessory`: open A, B; close A, close B → `.becomeAccessory` on B.
  - `testClosingUntrackedIsNoChange`: close C on an empty and a non-empty state → `nil`, state unchanged.
  - `testReopenAfterAllClosedBecomesRegularAgain`: open A, close A, open A → `.becomeRegular`.
  - `DockReopenDecision`: visible → `.bringToFront`; not visible + minimized → `.deminiaturizeOne`;
    neither → `.nothing`; visible + minimized → `.bringToFront`.
- **Build check:** `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and
  `xcodebuild -scheme Snapper test` all succeed.
- **Manual verification** (real GUI session; can't be automated):
  1. Revoke Screen Recording for Snapper (or reset onboarding) and launch. The "Set up Snapper" window
     appears and Snapper's icon is in the Dock.
  2. Click another app (e.g. Finder). Press ⌘Tab: Snapper is in the switcher; pick it → the setup
     window comes to front.
  3. Click another app again, then click Snapper's Dock icon → the setup window comes to front.
  4. Close the setup window → the Dock icon disappears, Snapper is no longer in ⌘Tab, the menu bar
     item still works.
  5. Right-click the menu bar item to open Settings → Dock icon appears; ⌘Tab works; with Snapper
     active, the menu bar shows Snapper's menus. Close Settings → Dock icon goes away.
  6. Press ⌘⇧6, select an area → annotation window opens focused and the Dock icon appears. Open a
     second annotation window. Close one → Dock icon stays. Minimize the other → Dock icon stays;
     click it → the window restores. Close it → Dock icon goes away.
  7. Draw in an annotation window, close it, choose Cancel on "Discard changes?" → window and Dock
     icon stay.
  8. With no Snapper windows open, take a ⌘⇧4 screenshot and a ⌘⇧5 recording → no Dock icon at any
     point.
  9. With Settings open and a dirty annotation window, choose Quit from the Dock icon's menu → the
     existing "Quit and discard N unsaved annotations?" prompt (PRD §8.1) appears as before.

## Out of scope
- The app icon artwork itself (0051-macos-app-icon).
- A user setting to always show the Dock icon.
- The Windows app (tray app with normal taskbar windows; PRD §13 is unchanged).

## Verification status
- 2026-11-04: `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and
  `xcodebuild -scheme Snapper test` green: 424 tests in 75 suites passed, including the new
  `DockPresenceTests` (DockPresence open/close transitions and DockReopenDecision).
- Implementation notes: tracking happens before `showWindow`/`NSApp.activate()`, so the policy is
  `.regular` before the window comes up. No re-activation or delay hack was added for the menu bar;
  check step 5 for the first `.accessory → .regular` switch.
- Manual verification (real GUI session):
  - [ ] not yet run by user — 1. Launch without Screen Recording permission: setup window appears with a Dock icon.
  - [ ] not yet run by user — 2. Switch to Finder, ⌘Tab back to Snapper: setup window comes to front.
  - [ ] not yet run by user — 3. Switch away, click Snapper's Dock icon: setup window comes to front.
  - [ ] not yet run by user — 4. Close the setup window: Dock icon and ⌘Tab entry disappear; menu bar item still works.
  - [ ] not yet run by user — 5. Open Settings: Dock icon appears, ⌘Tab works, Snapper's menus show when active; close Settings → Dock icon goes away.
  - [ ] not yet run by user — 6. Two annotation windows: closing one keeps the Dock icon; minimizing the other keeps it; Dock click restores it; closing it removes the Dock icon.
  - [ ] not yet run by user — 7. Cancel "Discard changes?" on a dirty annotation window: window and Dock icon stay.
  - [ ] not yet run by user — 8. With no Snapper windows open, a ⌘⇧4 screenshot and ⌘⇧5 recording never show a Dock icon.
  - [ ] not yet run by user — 9. With Settings open and a dirty annotation window, Quit from the Dock menu shows the existing "Quit and discard N unsaved annotations?" prompt.
