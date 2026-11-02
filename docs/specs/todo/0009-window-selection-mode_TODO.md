# 0009 — Add window selection mode (Space toggle) and shadowless window screenshots

> Source: docs/PRD.md §5 (Window mode, lines 70–73; Esc line 74), §6.2 (line 84), §12 (line 194)
> Depends on: 0004-display-geometry, 0007-selection-overlay-rectangle, 0008-screenshot-rectangle-capture

## Context

Not yet implemented. After 0007 and 0008, the selection overlay (`Snapper/Capture/SelectionOverlayController.swift`, one `SelectionOverlayPanel` per display, pure `Snapper/Capture/RectSelectionTracker.swift`) supports rectangle mode only and produces `SelectionResult.rect(display:, rect)`. `Snapper/Capture/ScreenshotCapturer.swift` (0008) captures a display-local rectangle through `SCScreenshotManager`, and the result flows through the pure `ScreenshotOutputPlan`, `FileNamer`/`SaveFolder` (0002), `Clipboard`, `ShutterSound` and `ErrorAlerts` (all 0008).

The PRD requires a second selection mode:

- §5: "**Space** toggles between rectangle and window mode, either before or during a drag. Toggling during a drag discards that drag; switching back to rectangle mode starts a fresh drag." "The window under the cursor is highlighted, and clicking selects it." "Our own windows and system UI that can't be captured are excluded from window hit-testing." "**Esc** cancels at any time."
- §6.2: "Window captures contain only that window, with no shadow, even if other windows cover it." Format stays PNG at native pixel resolution.
- §12: "Pressing Space switches to window mode, and the resulting capture contains only that window with no shadow, even when it is covered."

This spec adds window mode to the shared overlay (so ⌘⇧4, ⌘⇧5 and ⌘⇧6 all get it) and implements the window **screenshot** path end to end. What ⌘⇧5 and ⌘⇧6 do with a window selection belongs to 0013 and 0017.

### Research notes (binding for the implementer)

- **Z-order.** `SCShareableContent.windows` is **not documented to be z-ordered**, and in practice its order isn't reliable front-to-back. `CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)` returns on-screen windows **front to back**, which macOS window pickers rely on. Use it **only for ordering and alpha**. It is a non-deprecated window-info API, and AGENTS.md bans only `CGWindowListCreateImage` and `CGDisplayStream`. Pixels always come from ScreenCaptureKit.
- **Join key.** `SCWindow.windowID` (a `CGWindowID`) equals the CG entry's `kCGWindowNumber`. Candidates are the SCK windows that also appear in the CG list, ordered by their CG index. SCK windows that are missing from the CG list are dropped (not on screen as far as the WindowServer is concerned). CG entries without an SCK window are dropped too, because they can't be captured, which is one way that "system UI that can't be captured" gets excluded.
- **Coordinates.** `SCWindow.frame` is in CoreGraphics global coordinates (points, origin at the top-left of the primary display, y down), the same space as `kCGWindowBounds`. The overlay works in AppKit global coordinates (bottom-left, y up). All conversions go through `DisplayGeometry` (0004). Don't write ad-hoc `primaryHeight - y` math in this spec's code.
- **Capture API.** `SCContentFilter(desktopIndependentWindow:)` captures a single window independently of what covers it and of its position on the desktop. `SCContentFilter.contentRect` (macOS 14+) gives its size in points, and `SCContentFilter.pointPixelScale` (macOS 14+) gives its pixel scale. `SCStreamConfiguration.ignoreShadowsSingleWindow = true` (macOS 14+) removes the drop shadow, and `ignoreGlobalClipSingleWindow = true` (macOS 14+) includes the parts of the window that extend past a display edge.

## User story

As a Snapper user, after pressing ⌘⇧4 I can press Space, see the window under my cursor highlighted, and click it to get a PNG that contains only that window, with no shadow and nothing that was covering it, saved and/or copied according to my destination setting.

## Scope & changes

### Pure logic (unit-tested, no AppKit/SCK imports beyond CoreGraphics types)

- **`Snapper/Capture/WindowHitTester.swift`** (new) defines:
  - `struct WindowCandidate: Equatable, Sendable` with `windowID: CGWindowID`, `frame: CGRect` (**CG-global points, top-left origin**), `layer: Int`, `ownerPID: pid_t?`, `ownerBundleID: String?`, `isOnScreen: Bool`, `alpha: Double`, and `title: String?`.
  - `enum WindowCandidateBuilder` with `static func build(windows: [WindowCandidate], frontToBackIDs: [CGWindowID], alphaByID: [CGWindowID: Double]) -> [WindowCandidate]`. It returns the windows that appear in `frontToBackIDs`, sorted by their index in that list, with `alpha` taken from `alphaByID` (default 1.0 when missing). Windows that aren't in `frontToBackIDs` are dropped. IDs in `frontToBackIDs` with no matching window are ignored. The input `windows` carry the SCK-derived fields, with `alpha` left as a placeholder.
  - `struct WindowHitTester: Sendable`, initialised with `candidates: [WindowCandidate]` (front to back) and `excludedPID: pid_t` (Snapper's own PID). It has:
    - `func isSelectable(_ c: WindowCandidate) -> Bool`, which is true only when **all** of these hold: `c.ownerPID != nil` and `c.ownerPID != excludedPID`; `c.layer == 0` (the normal window layer — this excludes the menu bar, Dock, Notification Center, Control Center, the screensaver, and our overlay, pill and border panels); `c.isOnScreen`; `c.alpha > 0`; and `c.frame.width >= 4 && c.frame.height >= 4` (this excludes zero-size and 1-pixel helper windows; the threshold mirrors the 4 × 4 pt click threshold in §5).
    - `func window(at point: CGPoint) -> WindowCandidate?`, where `point` is CG-global. It returns the **first** candidate in front-to-back order that is selectable and whose `frame.contains(point)` (half-open, as `CGRect.contains`). A non-selectable window in front **does not block** windows behind it. For example, a transparent full-screen layer-0 window owned by Snapper doesn't hide the app window underneath. It returns `nil` over empty desktop.
  - `enum WindowDisplayRule` with `static func display(for windowFrame: CGRect, displays: [DisplayInfo], primaryDisplayHeight: CGFloat) -> DisplayInfo?`. It converts each `DisplayInfo` frame (AppKit-global) to CG-global with `DisplayGeometry` (0004) and returns the display with the **largest intersection area** with `windowFrame`. Ties go to the earlier display in `displays`. If no display intersects, it returns the display whose CG frame is closest to the window's centre (smallest distance from the centre to the display rect). It returns `nil` only when `displays` is empty. Later specs (0012/0013 pill placement) need this display.
- **`Snapper/Capture/SelectionSession.swift`** (new) is a pure `struct SelectionSession` that owns the overlay's interaction state. It wraps 0007's `RectSelectionTracker` and adds the mode:
  - `enum SelectionMode: Equatable, Sendable { case rectangle, window }`, starting at `.rectangle`.
  - It stores `mode`, `tracker: RectSelectionTracker`, `isMouseDown: Bool`, `isPressStale: Bool` (a press that began before the latest toggle), and `isWindowClickArmed: Bool`.
  - `mutating func toggleMode()` flips `mode`, **replaces `tracker` with a fresh `RectSelectionTracker`** (this discards any in-progress drag, and replacing it rather than calling a reset method keeps us independent of 0007's exact API), sets `isWindowClickArmed = false`, and sets `isPressStale = isMouseDown`.
  - `mouseDown(at:)` (AppKit-global): sets `isMouseDown = true` and `isPressStale = false`. In `.rectangle` it forwards to the tracker. In `.window` it sets `isWindowClickArmed = true`.
  - `mouseDragged(to:)`: forwards to the tracker only in `.rectangle` and only when `!isPressStale`. Otherwise it is ignored.
  - `mouseUp(at:) -> SelectionSessionOutcome`: sets `isMouseDown = false`. If `isPressStale`, it clears the flag and returns `.none` (the release of a discarded drag never selects or captures anything). In `.rectangle` it forwards to the tracker and maps the tracker's result (a completed rect becomes `.rectangle(<0007's rect result>)`, a click or a below-threshold drag becomes `.none`). In `.window` it returns `.windowClick(at: point)` if `isWindowClickArmed` and `.none` otherwise, then disarms.
  - `enum SelectionSessionOutcome: Equatable { case none, rectangle(/* whatever 0007's tracker yields for a finished drag */), windowClick(at: CGPoint) }`.
  - **Interpretation of "switching back to rectangle mode starts a fresh drag" (§5):** the discarded rectangle is never restored, and a new rectangle begins only with a **new mouse-down**. If the button is still held across a toggle, its drag and release are ignored (`isPressStale`). This is the only reading that never captures from a drag the user abandoned.
- **`SelectionResult`** (0007, `Snapper/Capture/`): add `case window(windowID: CGWindowID, frame: CGRect, display: DisplayInfo)`. `frame` is **CG-global points** (document it on the case), and `display` comes from `WindowDisplayRule`. Update every exhaustive `switch` (0007 and 0008 code).

### Platform glue (manually verified)

- **`Snapper/Capture/WindowCandidateProvider.swift`** (new, `@MainActor`): `func loadCandidates() async throws -> [WindowCandidate]`.
  1. `try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)`. Map each `SCWindow` into a `WindowCandidate`: `windowID`, `frame`, `windowLayer`, `owningApplication?.processID`, `owningApplication?.bundleIdentifier`, `isOnScreen`, `title`, and `alpha: 1`. Extract plain values on the SCK side and send only the `Sendable` `[WindowCandidate]` back to the main actor, so no `SCWindow` crosses actors.
  2. Call `CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)` and read `kCGWindowNumber` (in order, which gives `frontToBackIDs`) and `kCGWindowAlpha` (which gives `alphaByID`). Skip any entries that are missing a number.
  3. Return `WindowCandidateBuilder.build(...)`.
- **`SelectionOverlayController`** (0007, modified):
  - When the overlay opens, start `loadCandidates()` **once** in a `Task` running in parallel with showing the panels. Store the resulting `WindowHitTester(candidates:excludedPID: ProcessInfo.processInfo.processIdentifier)`. Until the load finishes (or if it throws), window mode has no hit tester: nothing is highlighted and clicks do nothing. When it finishes, re-run hover hit-testing at the current mouse location. If the overlay closes first, ignore the result (check a session token or the controller's open state). The snapshot isn't refreshed while the overlay is open.
  - Route all mouse events through `SelectionSession` instead of feeding `RectSelectionTracker` directly.
  - **Space** (`keyCode == 49`) in the panel's `keyDown` calls `session.toggleMode()` and ignores auto-repeat (`event.isARepeat`), so holding Space doesn't flicker between modes. After toggling, clear the rectangle drawing, update the cursor, and immediately hit-test at `NSEvent.mouseLocation` so the highlight appears without moving the mouse. Esc handling (0007) is unchanged and works in both modes.
  - **Hover:** the panels must receive `mouseMoved`. Set `acceptsMouseMovedEvents = true` and/or add an `NSTrackingArea` with `.mouseMoved, .activeAlways, .inVisibleRect`. In window mode, on each move convert `NSEvent.mouseLocation` (AppKit-global) to CG-global with `DisplayGeometry` and call `hitTester.window(at:)`. Store the hovered candidate and redraw only when its `windowID` changes or the mode changes.
  - **Click:** on `.windowClick(at:)`, hit-test that point. If it returns `nil` (empty desktop, or the candidates haven't loaded), do nothing and keep the overlay open, just as a click does in rectangle mode. Otherwise build `.window(windowID:, frame: candidate.frame, display: WindowDisplayRule.display(...))` and finish the selection through the same path as `.rect`, so the overlay is removed before capture (§5).
  - **Cursor:** rectangle mode keeps the crosshair (0007). Window mode uses `NSCursor.arrow`. macOS has no public camera cursor, a custom cursor image would add an asset and a design decision the PRD doesn't make, and the arrow clearly signals that the user is now pointing at windows rather than drawing.
- **Overlay view drawing in window mode** (0007's per-display view, modified): no dimming and no W × H readout. If a window is hovered, convert its CG-global frame to AppKit-global with `DisplayGeometry`, intersect it with this panel's display frame, convert that to view-local coordinates, and draw a fill in `NSColor.controlAccentColor` at 25 % alpha plus a 2 pt stroke in `controlAccentColor`. Every panel whose display intersects the window draws its part, so a window spanning two displays is highlighted on both. The highlight covers the window's **full** frame, including the parts that are covered by other windows, because that is what will be captured.
- **`ScreenshotCapturer.captureWindow(windowID: CGWindowID) async throws -> CGImage`** (0008's file, new method):
  - Re-fetch `SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)` and find the `SCWindow` with that ID. Re-fetching avoids holding `SCWindow` objects across actors and the overlay lifetime. If the window is gone, throw `ScreenshotCaptureError.windowUnavailable` (a new case, or an equivalent error added to 0008's error type) with the message "The window is no longer available."
  - `let filter = SCContentFilter(desktopIndependentWindow: scWindow)`. There's no need to exclude our own app, because this filter captures only the target window.
  - Configuration: `width = Int((filter.contentRect.width * CGFloat(filter.pointPixelScale)).rounded())`, `height` likewise, `showsCursor = false`, `ignoreShadowsSingleWindow = true`, `ignoreGlobalClipSingleWindow = true`, and `captureResolution = .best`.
  - `try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)`. Return the `CGImage` without converting it, so callers (0008's output path now, the 0017 annotation path later) treat it exactly like a rectangle capture.
- **`AppCoordinator`** (0001/0008, modified): for purpose `.screenshot`, handle `.window(windowID:, …)` by calling `captureWindow(windowID:)` and then running the **same** output path as `.rect` (`ScreenshotOutputPlan`, PNG write via `FileNamer`/`SaveFolder`, clipboard, shutter sound, and error mapping: missing permission opens onboarding, any other error shows the generic alert). For `.record` and `.annotate`, send `.window` results to whatever currently handles `.rect` for that purpose (stubs at this point). 0011/0013 and 0017 define the real behaviour. Factor out the shared "image → output" step if 0008 inlined it, so that rect and window don't duplicate it.
- **Project:** new files under `Snapper/Capture/` and new tests under `SnapperTests/` mean you must run `xcodegen generate`. There are no `project.yml` changes beyond what the source globs already pick up.
- **PRD:** no change. The cursor, highlight styling and stale-press handling are implementation choices recorded here and consistent with §5.

## Acceptance criteria

- [ ] With the overlay open (any of the three shortcuts), Space switches to window mode, and Space again switches back. This works before a drag and during a drag.
- [ ] Pressing Space during a rectangle drag removes the in-progress rectangle immediately. Releasing the mouse afterwards captures nothing, and back in rectangle mode a new mouse-down starts a fresh drag.
- [ ] In window mode, the topmost selectable window under the cursor is highlighted with an accent fill and border, the highlight follows the cursor, and the cursor is an arrow.
- [ ] Clicking a highlighted window with ⌘⇧4 produces a PNG at native pixel resolution (2× on Retina) that contains only that window, with no drop shadow and none of the windows covering it (§6.2, §12). The file name, destination, clipboard and shutter sound behave exactly as for rectangle captures.
- [ ] Clicking empty desktop in window mode does nothing, and the overlay stays open.
- [ ] Snapper's own windows (overlay, Settings, onboarding, and later the annotation windows and recording pill) are never highlighted or selectable, and neither are the menu bar, the Dock, or other non-layer-0 system UI.
- [ ] Esc closes the overlay on every display in both modes.
- [ ] All code that touches the UI runs on `@MainActor`, and SCK `async` results are consumed on the main actor.
- [ ] `xcodegen generate`, the Debug build and `xcodebuild -scheme Snapper test` all succeed.

## Edge cases

- **Overlapping windows:** the frontmost selectable window wins. A non-selectable window in front (our overlay, a layer-0 Snapper window, an alpha-0 window) is skipped, not treated as blocking.
- **Covered window:** hovering over a visible part of a partly covered window selects it, and the capture shows the whole window without the occluders. A fully covered window can't be hovered (accepted, same as macOS).
- **Multiple displays / negative origins:** displays to the left of or below the primary one have negative AppKit coordinates, and displays above the primary one have negative CG y. Hit-testing must convert the cursor through `DisplayGeometry` with the primary display height. Never compare AppKit points with CG frames directly.
- **Window spanning two displays:** both panels draw their part of the highlight. The capture is the whole window (desktop-independent window capture), so the §5 rule "a selection is confined to one display" applies to rectangle selections, and for a window `display` is chosen by `WindowDisplayRule` (largest overlap) for later pill placement.
- **Window partly off-screen:** `ignoreGlobalClipSingleWindow = true` captures the whole window. The highlight is clipped to the displays.
- **Mixed scales:** the output pixel size comes from `filter.pointPixelScale`, so a window on a 1× display gives a 1× image and a window on a Retina display gives a 2× image.
- **Window closes or moves between hover and click:** the frame snapshot is stale by design. If the window closed, `captureWindow` throws `windowUnavailable` and shows the generic error alert. If it moved, the capture is still correct because it is window-based, not region-based.
- **Window closes between click and capture:** same as above (`windowUnavailable` alert).
- **Candidates still loading, or the load failed:** window mode shows no highlight and ignores clicks. Esc and Space still work. A missing permission is caught earlier by the 0006 gate. If the load throws anyway, log the error and stay in this inert state, with no alert while the overlay is open.
- **Rounded window corners:** the PNG keeps SCK's alpha (the corners are transparent), as in macOS's own shadowless window captures.
- **Holding Space:** auto-repeat is ignored, so each physical press toggles exactly once.
- **Display connected or disconnected while in window mode:** the selection is cancelled exactly as in 0007.
- **Shortcuts while the overlay is open** stay ignored (0005).

## Test plan

- **Unit tests** (`SnapperTests/WindowHitTesterTests.swift`):
  - `testTopmostWindowWins`: windows A (front) and B (back) overlap. A point in the overlap returns A, and a point only in B returns B.
  - `testOwnPIDIsSkippedNotBlocking`: a full-screen candidate owned by `excludedPID` sits in front of window B. A point inside B returns B.
  - `testNonZeroLayerExcluded`: a layer-25 (menu bar-like) and a layer-20 (Dock-like) candidate in front of B. Points inside them return B, or `nil` where B doesn't cover the point.
  - `testZeroSizeTinyAndInvisibleExcluded`: `CGRect.zero`, a 2 × 100 window and an `alpha == 0` window are all skipped.
  - `testOffscreenAndOwnerlessExcluded`: `isOnScreen == false` and `ownerPID == nil` are skipped.
  - `testEmptyDesktopReturnsNil`: a point outside every frame returns `nil`.
  - `testEdgeSemantics`: a point on `minX/minY` is inside and a point on `maxX/maxY` is outside (half-open).
  - `testNegativeCoordinates`: a candidate at CG frame `(-1920, -300, 800, 600)` (a display left of and above the primary display) is hit at `(-1500, 0)` and missed at `(-1000, 0)`.
- **Unit tests** (`SnapperTests/WindowCandidateBuilderTests.swift`):
  - `testOrdersByFrontToBackIDs`: SCK order `[3, 1, 2]` with CG order `[1, 2, 3]` gives the result `[1, 2, 3]`.
  - `testDropsWindowsMissingFromCGList` and `testIgnoresCGIDsWithoutWindow`.
  - `testAlphaJoinedAndDefaultsToOne`.
- **Unit tests** (`SnapperTests/WindowDisplayRuleTests.swift`), using two displays: primary 1440×900 at AppKit `(0,0)` scale 2 and secondary 1920×1080 at AppKit `(-1920, -180)` scale 1:
  - `testWindowFullyOnPrimary`, which gives the primary display.
  - `testWindowFullyOnSecondaryNegativeOrigin`, which gives the secondary display (check the CG conversion of the secondary display frame through `DisplayGeometry`).
  - `testSpanningWindowPicksLargestOverlap`: 70 % on the secondary display gives the secondary display.
  - `testExactTieGoesToFirstDisplay`.
  - `testNoIntersectionPicksNearestDisplay` and `testEmptyDisplaysReturnsNil`.
- **Unit tests** (`SnapperTests/SelectionSessionTests.swift`):
  - `testStartsInRectangleMode`, and `testToggleFlipsModeBothWays`.
  - `testToggleDuringDragDiscardsDrag`: down → dragged → toggle → toggle → up gives `.none`, and the tracker has no in-progress rect.
  - `testStalePressDragIsIgnoredAfterToggleBack`: down → toggle → toggle → dragged → up gives `.none`.
  - `testFreshDragAfterToggleBackSelects`: after the sequence above, a new down → dragged (≥ 4 × 4) → up gives `.rectangle(...)`.
  - `testWindowClickReturnsPoint`: toggle → down(p) → up(p) gives `.windowClick(at: p)`.
  - `testRectPressReleasedInWindowModeDoesNotClick`: down (rectangle) → toggle → up gives `.none`, not `.windowClick`.
  - `testWindowPressReleasedAfterToggleToRectangleDoesNothing`: toggle → down → toggle → up gives `.none`.
  - `testUpWithoutDownInWindowModeIsNone`.
- **Build check:** `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` all pass.
- **Manual verification** (needs a real GUI session with Screen Recording granted; can't be automated):
  1. Open two overlapping app windows (for example Finder and TextEdit). Press ⌘⇧4, then Space **before** dragging. Expected: the cursor becomes an arrow, and the window under the cursor gets a blue (accent) tint and border that follows the cursor from window to window.
  2. Press Space again. Expected: back to the crosshair, the highlight disappears, and dragging draws a rectangle as before.
  3. Press ⌘⇧4, start dragging a rectangle, and press Space **mid-drag**. Expected: the rectangle disappears at once, and releasing the mouse captures nothing. Press Space again and drag anew. Expected: a fresh rectangle that captures normally.
  4. With TextEdit partly covered by Finder, press ⌘⇧4, then Space, and click the visible part of TextEdit. Expected: the overlay disappears, the shutter sound plays, and the PNG in `~/Documents/Screenshots` (and on the clipboard) shows **all** of TextEdit, with no Finder content on top and **no drop shadow** (the corners are transparent). On a Retina display the pixel size is 2× the window's point size (check in Preview → Tools → Show Inspector).
  5. With a second display connected, move a window to it, select it in window mode, and check the capture (correct window, that display's scale). Also hover a window that straddles both displays: the highlight shows on both displays.
  6. Open Snapper's Settings window, press ⌘⇧4 then Space, and hover over Settings. Expected: Settings is not highlighted (the window behind it, or nothing, is).
  7. In window mode, hover over the menu bar and the Dock. Expected: no highlight, and clicking them does nothing.
  8. In window mode, click empty desktop. Expected: nothing happens and the overlay stays open.
  9. In window mode, press Esc. Expected: the overlay closes on every display and nothing is captured.
  10. Set the destination to *Copy to clipboard*, then *Save to file*, and repeat step 4. Expected: the same behaviour as rectangle captures for each destination.
  11. Hold Space down for about a second. Expected: the mode toggles once, without flickering.

## Out of scope

- Rectangle selection, overlay lifecycle, Esc and display-change cancellation: **0007**.
- The rectangle capture path, `ScreenshotOutputPlan`, the clipboard, the shutter sound and the alerts: **0008** (reused here, not reimplemented).
- Recording a selected window (window filter, following the window, stopping on close): **0013**. The pill and border placement that use `SelectionResult.window`'s `display`: **0012/0013**.
- Opening a window capture in the annotation editor: **0017**.
- Refreshing window frames while the overlay is open, and keyboard selection of windows: not in the PRD.
- Window drop shadows: a PRD non-goal (§2).
