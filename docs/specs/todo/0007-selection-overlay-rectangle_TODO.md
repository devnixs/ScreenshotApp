# 0007 — Show the rectangle selection overlay on every display

> Source: docs/PRD.md §5 (lines 62–77, rectangle mode, Esc, shortcuts ignored, display changes, overlay removed before capture), §12 (lines 195–196, Esc and multi-display criteria)
> Depends on: 0004-display-geometry, 0005-global-shortcuts-app-mode, 0006-permissions-onboarding (and transitively 0001–0003)

## Context

No selection UI exists yet. After 0001–0006:

- `AppCoordinator` (`Snapper/App/AppCoordinator.swift`, from 0001) has `startScreenshot()`, `startRecording()`, `startAnnotate()`. 0005 routes the three shortcuts and the menu items through the pure `AppModeMachine` (`Snapper/App/AppModeMachine.swift`), whose `AppMode` is `idle` / `selecting(CapturePurpose)` / `recording`, and which already encodes "while selecting, all Snapper shortcuts are ignored" (docs/PRD.md §5 line 76). 0006 gates every shortcut on Screen Recording permission: without permission the onboarding window opens instead (docs/PRD.md §11 line 186).
- `DisplayGeometry` (`Snapper/Capture/DisplayGeometry.swift`, from 0004) provides `DisplayInfo` (displayID, AppKit-global frame, backingScale) and the pure helpers this spec relies on: display containing a point, clamp a rect/point to a display, normalized rect from two points.
- What is missing is the thing between "shortcut accepted" and "capture": the overlay itself.

PRD behavior delivered here (docs/PRD.md §5):

- A borderless full-screen overlay appears on **every** connected display.
- Rectangle mode: crosshair cursor; dragging draws a rectangle; the area outside it is dimmed; a live **W × H** readout in points appears near the cursor; the rectangle is confined to the display where the drag started; a click, or a drag smaller than 4 × 4 points, does nothing and the overlay stays open.
- Esc cancels at any time and closes the overlay (on every display).
- After a selection, the overlay is removed before capture.
- While the overlay is open, all Snapper shortcuts are ignored.
- If a display is connected or disconnected while the overlay is open, the selection is cancelled.

Window mode and the Space toggle (docs/PRD.md §5 lines 70–73) are **not** in this spec (see 0009), but the types below are shaped so 0009 can add them without redesign.

## User story

As a Snapper user, when I press ⌘⇧4, ⌘⇧5, or ⌘⇧6 (or pick the matching menu item), every display dims and my cursor becomes a crosshair; I drag out a rectangle on one display and see its size in points next to the cursor; releasing the mouse finishes the selection, and Esc backs out cleanly on every display.

## Scope & changes

### Pure logic (unit-tested, no AppKit)

- **`Snapper/Capture/SelectionResult.swift`** — the value the overlay hands back:
  ```swift
  enum SelectionResult: Equatable, Sendable {
      /// `rect` is in AppKit global coordinates (points, bottom-left origin),
      /// already clamped to `display.frame`, width and height >= 4.
      case rect(display: DisplayInfo, rect: CGRect)
      // 0009 adds: case window(...)
  }
  ```
  Keep the rect in AppKit-global points. Consumers (0008 capture, 0011 recording) convert it to a display-local, top-left `sourceRect` with `DisplayGeometry`'s display-local helper; this spec does no CG/SCK conversion. Switches over `SelectionResult` must be exhaustive (no `default:`), so 0009 adding `.window` makes the compiler flag every consumer.

- **`Snapper/Capture/RectSelectionTracker.swift`** — `struct RectSelectionTracker` (value type, `Sendable`), owns the drag state for rectangle mode. All points are AppKit-global points.
  - State: `idle` or `dragging(startDisplay: DisplayInfo, start: CGPoint, current: CGPoint)` (a private enum).
  - `mutating func mouseDown(at point: CGPoint, displays: [DisplayInfo])` — picks the start display with `DisplayGeometry`'s display-containing-point helper. If no display contains the point (AppKit can report the cursor exactly on a frame's `maxX`/`maxY`, which half-open containment rejects), fall back to the display whose frame is nearest to the point (smallest distance from the point to the frame). If `displays` is empty, stay `idle`. The start point is clamped to the start display's frame. Enters `dragging` with `current == start`.
  - `mutating func drag(to point: CGPoint) -> CGRect?` — no-op returning `nil` when `idle`. Otherwise clamps `point` to the **start display's** frame (never another display, even if the cursor is on one), stores it as `current`, and returns `currentRect`.
  - `var currentRect: CGRect?` — `nil` when idle; otherwise the normalized rect from `start` to `current` (via `DisplayGeometry`), which by construction lies within `startDisplay.frame`.
  - `var currentPoint: CGPoint?` — the clamped `current` (used to place the readout).
  - `var startDisplay: DisplayInfo?`.
  - `mutating func mouseUp(at point: CGPoint) -> RectSelectionOutcome` — applies `drag(to: point)` first, then:
    - `idle` → `.ignored` (no drag was in progress, e.g. it was discarded by `reset()`).
    - clamped rect with `width < 4 || height < 4` → `.tooSmall` (the PRD's "smaller than 4 × 4 points counts as a click"; a click with no movement is `0 × 0` and lands here too).
    - otherwise → `.selected(.rect(display: startDisplay, rect: rect))`.
    - In every case the tracker returns to `idle`.
  - `mutating func reset()` — discards any in-progress drag and returns to `idle`. Used by the controller on cancel, and by 0009 when Space toggles modes mid-drag (docs/PRD.md §5 line 71: "Toggling during a drag discards that drag").
  - `static let minimumSize: CGFloat = 4` (points).
  - `enum RectSelectionOutcome: Equatable { case selected(SelectionResult), tooSmall, ignored }`.
  - The threshold is applied to the **clamped** rect, so a drag that starts 2 pt from a display edge and continues off the display is `2 × H` and therefore `.tooSmall`.

- **`Snapper/Capture/SelectionReadout.swift`** — `enum SelectionReadout` (namespace), pure:
  - `static func text(for size: CGSize) -> String` → `"\(w) × \(h)"` where `w`/`h` are the width and height in points rounded with `.toNearestOrAwayFromZero` and printed as integers, separated by space, U+00D7 MULTIPLICATION SIGN, space. Example: `CGSize(width: 1440, height: 900)` → `"1440 × 900"`. No locale grouping separators.
  - `static func origin(labelSize: CGSize, cursor: CGPoint, display: CGRect) -> CGPoint` — returns the label's bottom-left origin in AppKit-global coordinates:
    1. Default: below-right of the cursor: `x = cursor.x + offset`, `y = cursor.y - offset - labelSize.height`.
    2. If the label's right edge would exceed `display.maxX - margin`, flip to the left: `x = cursor.x - offset - labelSize.width`.
    3. If the label's bottom would be below `display.minY + margin`, flip above: `y = cursor.y + offset`.
    4. Finally clamp so the whole label lies within `display.insetBy(dx: margin, dy: margin)` (if the label is larger than that, pin to the inset's min edge).
    - Constants: `static let offset: CGFloat = 12`, `static let margin: CGFloat = 4`.
    - `cursor` is the tracker's clamped `currentPoint`, and `display` is the **start display's** frame, so the readout always stays on the start display even when the real cursor has crossed onto another one.

- **`Snapper/Capture/DisplayConfigurationChange.swift`** — `enum DisplayConfigurationChange` with `static func requiresCancel(old: [DisplayInfo], new: [DisplayInfo]) -> Bool`. Returns `true` when the set of display IDs differs (connect/disconnect, as required by docs/PRD.md §5 line 77) **or** when any display with the same ID has a different frame or backing scale (the panels and the start display would be stale). Order-insensitive. Returns `false` when both lists describe the same displays. See Open questions for the "frame/scale changed" half.

### Platform glue (AppKit, manually verified)

- **`Snapper/Capture/NSScreen+DisplayInfo.swift`** (only if 0004 did not already add an equivalent adapter; if it did, use that one) — `extension NSScreen { var displayInfo: DisplayInfo? }` built from `deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")]` as `CGDirectDisplayID`, `frame`, and `backingScaleFactor`; and a `static var currentDisplayInfos: [DisplayInfo]` over `NSScreen.screens`.

- **`Snapper/Capture/SelectionOverlayPanel.swift`** — `final class SelectionOverlayPanel: NSPanel`, one per `NSScreen`, `@MainActor`:
  - `styleMask: [.borderless, .nonactivatingPanel]`, `backing: .buffered`, `defer: false`.
  - Frame: exactly `screen.frame` (including menu bar and Dock areas). Call `setFrame(screen.frame, display: false)` after init, and override `constrainFrameRect(_:to:)` to return the rect unchanged so AppKit does not push the panel below the menu bar.
  - **Level: `.screenSaver`.** Justification: the menu bar is at `NSWindow.Level.mainMenu` (24), the status bar at `.statusBar` (25), the Dock below that, and open menus/pop-ups at `.popUpMenu` (101). `.screenSaver` (1000) sits above all of them, so the dimming and the crosshair cover the menu bar, the Dock, and any app's open pop-up menu — matching the system screenshot UI — while staying below the cursor and the system shielding window.
  - `collectionBehavior: [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]` so the overlay also appears over full-screen apps and on whichever Space is active, and is skipped by ⌘\`/Mission Control.
  - `isOpaque = false`, `backgroundColor = .clear`, `hasShadow = false`, `ignoresMouseEvents = false`, `acceptsMouseMovedEvents = true`, `isReleasedWhenClosed = false`, `hidesOnDeactivate = false`, `animationBehavior = .none`, `isMovable = false`.
  - `override var canBecomeKey: Bool { true }` (borderless panels return `false` by default, which would swallow Esc); `override var canBecomeMain: Bool { false }`.
  - Content view: `SelectionOverlayView`.

- **`Snapper/Capture/SelectionOverlayView.swift`** — `final class SelectionOverlayView: NSView`, `@MainActor`. Renders and forwards input only; it owns no selection logic.
  - Inputs set by the controller: `selectionRect: CGRect?` and `readout: (text: String, origin: CGPoint)?`, both in **AppKit-global** coordinates; the view converts to its own coordinates by subtracting its panel's `frame.origin` (both are bottom-left, so this is a plain translation). Setting either calls `needsDisplay = true`.
  - Drawing (`draw(_:)`): fill `bounds` with black at 35% opacity; if `selectionRect` intersects this panel, clear it (`NSRect.fill(using: .copy)` with `.clear`, or an even-odd path) and stroke a 1 pt white border inside it; if `readout` is set, draw the text (system font 12 pt, monospaced digits, white) on a rounded rect (radius 4, black at 75% opacity, 6 pt horizontal / 3 pt vertical padding). The label size used by `SelectionReadout.origin` is computed from the same font and padding (the controller measures it with `NSAttributedString.size()`). Panels on displays other than the start display receive `nil` for both and stay fully dimmed.
  - Mouse: `mouseDown`, `mouseDragged`, `mouseUp` convert the event location to AppKit-global with `window.convertPoint(toScreen: event.locationInWindow)` and forward it to the controller. AppKit keeps delivering `mouseDragged`/`mouseUp` to the panel that received `mouseDown` even after the cursor crosses onto another display, so global conversion is what makes cross-display clamping work. Only the left button is handled; right/other-button events are ignored. `acceptsFirstMouse(for:)` returns `true`.
  - Crosshair: add an `NSTrackingArea` over the bounds with `[.cursorUpdate, .mouseMoved, .activeAlways, .inVisibleRect]` and set `NSCursor.crosshair` in `cursorUpdate(with:)` and `mouseMoved(with:)` (cursor rects alone don't apply on non-key panels). The controller also calls `NSCursor.crosshair.set()` right after showing the panels, and `NSCursor.arrow.set()` after closing them.
  - Keys: `acceptsFirstResponder` is `true`. `keyDown(with:)` forwards the event to the controller (Esc, `keyCode == 53` / `kVK_Escape`, cancels; every other key is ignored for now — 0009 adds Space). `cancelOperation(_:)` also cancels.

- **`Snapper/Capture/SelectionOverlayController.swift`** — `@MainActor final class SelectionOverlayController`, owned by `AppCoordinator`. It owns the panels and one `RectSelectionTracker`.
  - API: `func selectRegion() async -> SelectionResult?` — shows the overlay, suspends until the user finishes or cancels, and returns the result (`nil` = cancelled). Implemented with a `CheckedContinuation` that is resumed **exactly once** (guard with an optional continuation that is set to `nil` before resuming). If called while already presenting, log a fault and return `nil` immediately (the mode machine should make this impossible).
  - **Presenting:**
    1. Snapshot `NSScreen.currentDisplayInfos` as the session's displays and remember `NSWorkspace.shared.frontmostApplication` (unless it is Snapper itself).
    2. Create one `SelectionOverlayPanel` per `NSScreen`, `orderFrontRegardless()` each.
    3. Make the overlay keyboard-reachable: call `NSApp.activate()` and then `makeKeyAndOrderFront(nil)` on the panel whose frame contains `NSEvent.mouseLocation` (fallback: the first panel), and make its view first responder. A `.nonactivatingPanel` with `canBecomeKey == true` can take key status, and the explicit activation makes Esc reach it reliably for an `LSUIElement` app even when another app was frontmost; the shortcut event that triggered the overlay is a user action, so the cooperative activation is honoured.
    4. Set the crosshair; register for `NSApplication.didChangeScreenParametersNotification`.
  - **Input handling:** `mouseDown(at:)` → `tracker.mouseDown(at:displays:)`; `mouseDragged(to:)` → `tracker.drag(to:)`; after each, push `selectionRect` and `readout` to the start display's view (text from `SelectionReadout.text(for: rect.size)`, origin from `SelectionReadout.origin(labelSize:cursor: tracker.currentPoint, display: startDisplay.frame)`), and `nil` to every other view. `mouseUp(at:)` → `tracker.mouseUp(at:)`:
    - `.selected(result)` → **finish** with `result`.
    - `.tooSmall` / `.ignored` → clear the selection and readout on all views; the overlay stays open, ready for a new drag.
  - **Esc** → **cancel**.
  - **Screen parameters changed** → compare the session's displays with `NSScreen.currentDisplayInfos` using `DisplayConfigurationChange.requiresCancel(old:new:)`; if `true`, **cancel**.
  - **Closing (shared by finish and cancel):** `tracker.reset()`; remove the notification observer; `orderOut(nil)` every panel and drop them; `CATransaction.flush()`; `NSCursor.arrow.set()`; reactivate the remembered previous app (`activate()`), so keyboard focus returns to where the user was (0017 will activate Snapper again when it opens an annotation window).
  - **Removal before capture:** on **finish**, after closing, wait for the window server to drop the panels before resuming the continuation: `await Task.yield()` (one main-actor turn) followed by `try? await Task.sleep(for: .milliseconds(50))`. Define the delay as `static let dismissalDelay: Duration = .milliseconds(50)`. This is belt-and-braces: the primary guarantee is that 0008/0011 build their `SCContentFilter` with `excludingApplications: [our SCRunningApplication]`, so our windows are never captured even if they were still on screen. On **cancel**, resume immediately with `nil` after closing.

- **`Snapper/App/AppCoordinator.swift`** (modify) — replace the stubs' "selection" step:
  - When a shortcut or menu item is accepted (permission gate from 0006 passed, `AppModeMachine` moved to `.selecting(purpose)`), start `Task { @MainActor in let result = await overlay.selectRegion(); … }`.
  - `nil` → feed `AppModeMachine` `.selectionCancelled`.
  - non-`nil` → feed `.selectionFinished`, then log the purpose and result with `os.Logger(subsystem: "com.thera-engineering.snapper", category: "selection")` (e.g. `"Selection for screenshot: display 1, rect (100, 200, 640, 480)"`). Capture arrives in 0008 (screenshot/annotate) and 0011 (record). In this spec the app must end up back in `idle` for **every** purpose: if 0005's machine moves to `.recording` on `.selectionFinished` for `.record`, immediately feed it the event that ends a recording, with a `// TODO(0011): start RecordingSession here` comment, so the menu and shortcuts keep working.
  - Shortcuts pressed while the overlay is open reach `AppModeMachine`, which already ignores them in `.selecting` (0005); no extra code here beyond making sure the mode is `.selecting` **before** the panels are shown.

- `project.yml`: no target or setting changes; new files are picked up by the existing source globs. Run `xcodegen generate` after adding them.

- No PRD change in this spec.

## Acceptance criteria

- [ ] ⌘⇧4, ⌘⇧5, ⌘⇧6 and the Screenshot / Record / Annotate menu items (with permission granted) each show a dimmed overlay on **every** connected display, covering the menu bar and the Dock, also over a full-screen app.
- [ ] The cursor is a crosshair over the overlay on every display.
- [ ] Dragging draws a rectangle: the inside is undimmed with a thin white border, everything else (including all other displays) stays dimmed.
- [ ] A live `W × H` readout in integer points, using the `×` character, follows the cursor during the drag and never leaves the start display, flipping sides near its right and bottom edges.
- [ ] A drag that continues onto another display is clamped to the edge of the display where it started; the readout and the result use the clamped rect.
- [ ] A click, or a drag smaller than 4 × 4 points (either side < 4), does nothing: nothing is logged, the overlay stays open, and a new drag works.
- [ ] Releasing a ≥ 4 × 4 drag closes the overlay on all displays and logs the purpose, the display ID, and the rect.
- [ ] Esc closes the overlay on every display at any time (before or during a drag), and nothing is logged as a selection.
- [ ] While the overlay is open, ⌘⇧4/⌘⇧5/⌘⇧6 do nothing (no second overlay, no stacked panels).
- [ ] Connecting or disconnecting a display while the overlay is open cancels it on all displays.
- [ ] After the overlay closes (finish or cancel), the previously frontmost app has keyboard focus again and all shortcuts work again.
- [ ] The overlay panels are ordered out, and the dismissal delay has elapsed, before the result is delivered.

## Edge cases

- **Negative-origin displays** (a display left of or below the main one, so its AppKit frame has negative x/y): containment, clamping and readout placement all work in AppKit-global coordinates and must be correct there (covered by tests).
- **Mixed scales** (Retina 2× + external 1×): everything here is in points; scale only matters downstream (0008). The readout shows points, not pixels.
- **Drag starts near an edge and leaves the display**: the rect is clamped; the size check uses the clamped rect.
- **Cursor exactly on a frame's `maxX`/`maxY`** at mouse-down: nearest-display fallback, start point clamped.
- **Mouse-down on one display, mouse-up on another**: the result is still on the start display (drag events stay with the mouse-down panel; positions are global).
- **Mouse-up without a preceding mouse-down** (e.g. after `reset()`): `.ignored`, overlay stays open.
- **Screen parameters notification with no real change** (it can fire spuriously): `requiresCancel` returns `false`, overlay stays.
- **Selection already in progress when a shortcut fires**: ignored by `AppModeMachine`; `selectRegion()` is never re-entered (and returns `nil` with a logged fault if it somehow is).
- **Permission revoked mid-session**: out of scope here — the gate runs before the overlay; capture errors are 0008's.
- **The previously frontmost app quit while the overlay was open**: reactivation is skipped if it has terminated.

## Test plan

- **Unit tests** (`SnapperTests/RectSelectionTrackerTests.swift`). Fixtures: `main = DisplayInfo(id: 1, frame: (0, 0, 1440, 900), scale: 2)`, `right = DisplayInfo(id: 2, frame: (1440, 0, 1920, 1080), scale: 1)`, `leftBelow = DisplayInfo(id: 3, frame: (-1920, -200, 1920, 1080), scale: 1)`.
  1. `mouseDown(100,100)` on `[main, right]`, `mouseUp(400,300)` → `.selected(.rect(display: main, rect: (100,100,300,200)))`.
  2. Reverse drag: down (400,300), up (100,100) → same normalized rect.
  3. Cross-display clamp: down (1000,500) on `main`, `drag(to: (2000,800))` returns `(1000,500,440,300)`; `mouseUp(2000,800)` → `.selected` with `display == main` and that rect.
  4. Clamp below/above: down (100,100), up (300,-50) → rect `(100,0,200,100)`.
  5. Start display is fixed: down on `right` at (1500,100), drag to (500,500) → rect `(1440,100,60,400)`, display `right`.
  6. Negative origin: down (-100,100) on `[main, leftBelow]`, up (50,-500) → `.selected(.rect(display: leftBelow, rect: (-100,-200,100,300)))`.
  7. Too small: down (100,100), up (103.9,110) → `.tooSmall`; down (100,100), up (110,103.9) → `.tooSmall`; down (100,100), up (104,104) → `.selected` with size `4 × 4`.
  8. Click: down (100,100), up (100,100) → `.tooSmall`.
  9. Clamped size is what counts: down (1438,100) on `main`, up (1600,300) → rect width 2 → `.tooSmall`.
  10. After `.tooSmall` the tracker is idle (`currentRect == nil`) and a new down/up pair produces `.selected`.
  11. `reset()` during a drag → `currentRect == nil`, `drag(to:)` returns `nil`, `mouseUp` → `.ignored`.
  12. `drag(to:)` and `mouseUp` with no prior `mouseDown` → `nil` / `.ignored`.
  13. Edge point: `mouseDown(at: (1440, 900))` on `[main]` alone → start display `main` (nearest fallback), start clamped; up (1300,800) → `.selected` with rect `(1300,800,140,100)`.
  14. `mouseDown` with `displays == []` → stays idle; `mouseUp` → `.ignored`.
- **Unit tests** (`SnapperTests/SelectionReadoutTests.swift`):
  1. `text(for: (1440, 900))` == `"1440 × 900"` and the separator is U+00D7 (assert on `"\u{00D7}"`).
  2. `text(for: (0, 0))` == `"0 × 0"`; `text(for: (99.5, 10.4))` == `"100 × 10"`; `text(for: (12345, 4))` == `"12345 × 4"` (no grouping separator).
  3. Placement default: display `(0,0,1440,900)`, label `60×20`, cursor (500,500) → `(512, 468)`.
  4. Near right edge: cursor (1430,500) → `x == 1430 - 12 - 60 == 1358`, `y == 468`.
  5. Near bottom edge: cursor (500,10) → `y == 22` (above), `x == 512`.
  6. Bottom-right corner: cursor (1430,10) → `(1358, 22)`.
  7. Top-left corner: cursor (0,900) → `(12, 868)`.
  8. Negative-origin display `(-1920,-200,1920,1080)`: cursor (-5,-195) → flips both ways → `(-77, -183)`; result lies within the display inset by 4.
  9. Label wider than the display inset: pinned to `display.minX + 4`.
  10. For a grid of cursor points over each fixture display, the returned label rect is always inside `display.insetBy(dx: 4, dy: 4)` (when it fits).
- **Unit tests** (`SnapperTests/DisplayConfigurationChangeTests.swift`):
  1. Same displays, different order → `false`.
  2. Display added → `true`; display removed → `true`.
  3. Same ID, moved frame → `true`; same ID, scale 2 → 1 → `true`.
  4. Both empty → `false`.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (real GUI session, Screen Recording permission granted, ideally two displays with one Retina; watch logs with `log stream --predicate 'subsystem == "com.thera-engineering.snapper"'` or Console.app):
  1. Press ⌘⇧4. Every display dims, including menu bar and Dock; the cursor is a crosshair on each display. Press Esc: the overlay disappears on every display, no "Selection" log line.
  2. Repeat step 1 with ⌘⇧5, ⌘⇧6, and each of the three menu items.
  3. ⌘⇧4, drag a rectangle on the main display. The inside is clear with a white border, the rest stays dimmed, and the other display stays fully dimmed. The readout shows e.g. `640 × 480` near the cursor and updates live. Release: overlay closes everywhere; the log shows `screenshot`, the display ID and the rect.
  4. ⌘⇧4, start a drag near the right edge of one display and move onto the other display. The rectangle stops at the edge of the start display and the readout stays on the start display. Release: the logged rect lies within the start display.
  5. ⌘⇧4, drag toward the bottom-right corner of a display: the readout flips to the left/above the cursor and never goes off-screen.
  6. ⌘⇧4, click without moving: nothing happens, overlay stays. Make a ~3-point drag: same. Then make a normal drag: it is logged.
  7. ⌘⇧4, then press ⌘⇧4, ⌘⇧5, ⌘⇧6 while the overlay is open: nothing changes (no extra panels; one Esc closes everything).
  8. ⌘⇧4 during a drag, press Esc: overlay closes on every display.
  9. With Safari frontmost (also try a full-screen Safari window): press ⌘⇧4. The overlay covers the full-screen Space. Esc works without clicking first. After closing, typing goes to Safari again.
  10. ⌘⇧4, then connect or disconnect a display: the overlay closes on all displays and the app is back to idle (⌘⇧4 opens a fresh overlay that covers the new display set).
  11. ⌘⇧5, finish a selection: the log shows `record`, and afterwards ⌘⇧4 still opens the overlay (the app returned to idle; recording itself is 0011).
  12. Revoke Screen Recording permission and press ⌘⇧4: the onboarding window opens instead of the overlay (0006 gate still in front).

## Open questions

None. On 2026-11-02, the user confirmed that changing a display’s arrangement, resolution, or scale cancels an open selection, as does connecting or disconnecting a display. Recorded in PRD §5.
