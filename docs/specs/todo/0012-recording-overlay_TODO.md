# 0012 — Show the recording pill and dashed border, and make Cancel discard the recording

> Source: docs/PRD.md §7.3 (lines 95–99), §7.4 (lines 100–105), §12 (lines 197–198)
> Depends on: 0004-display-geometry, 0011-recording-core (and transitively 0001, 0003, 0005, 0007, 0009)

## Context

After 0011, ⌘⇧5 → select → recording starts immediately, the menu bar icon turns into a red
`record.circle`, and Stop works through ⌘⇧5 or a click on the menu bar icon (saves the `.mp4` and
reveals it in Finder). There is no on-screen indication of *what* is being recorded, no elapsed
time, and no way to throw a recording away. No app code exists yet at the time of writing, so every
path below is a file this spec or a lower-numbered sibling creates.

The PRD requires (§7.3):

> A floating pill below the recorded area, or above it if there is no room below. If there is no
> room above either (for example, a full-screen selection), it is placed inside the area, near its
> bottom edge. It shows `elapsed time · Stop · Cancel` and stays on the recorded area's display.
> A thin dashed border marks the recorded area. […] (excluded from the recording)

and (§7.4): **Stop** saves the file (same as ⌘⇧5 / menu bar icon); **Cancel** discards the
recording immediately without confirmation; **Esc does not cancel** a recording. §12: the `.mp4`
contains neither the overlay pill nor the border; Cancel discards.

Types this spec relies on from siblings (canonical names):
- `DisplayInfo`, `DisplayGeometry` (0004, `Snapper/Capture/DisplayGeometry.swift`): AppKit-global
  display frame, display ID, backing scale, and AppKit↔CG conversion.
- `RecordingSession` (0011, `Snapper/Recording/RecordingSession.swift`): owns the `SCStream` +
  `SCRecordingOutput`; its content filter is built with
  `SCContentFilter(…, excludingApplications: [our SCRunningApplication], …)`; exposes `stop()`.
  This spec adds or completes `cancel()` (see below).
- `AppCoordinator` (0001, extended by 0005/0011): `@MainActor final class` that starts/stops
  recordings and drives `AppMode` (0005's `AppModeMachine`: `idle` / `selecting(purpose)` /
  `recording`).
- `SelectionResult` (0007/0009): what the user selected (rectangle on a display, or a window).

## User story

As a Snapper user who is recording, I see a thin dashed border around the area being recorded and
a small floating pill next to it with a running timer, a **Stop** button that saves the video and
a **Cancel** button that throws it away, and none of that ever shows up in the video.

## Scope & changes

### Pure logic (unit-tested, no AppKit imports beyond CoreGraphics/Foundation)

- **`Snapper/Recording/RecordingPillPlacement.swift`** — `enum RecordingPillPlacement` with
  ```swift
  enum Position: Equatable, Sendable { case below, above, inside }
  struct Result: Equatable, Sendable { let frame: CGRect; let position: Position }
  static let defaultMargin: CGFloat = 8
  static func place(pillSize: CGSize,
                    recordedRect: CGRect,      // AppKit global coords (bottom-left origin, y up)
                    visibleFrame: CGRect,      // NSScreen.visibleFrame of the recorded area's display
                    margin: CGFloat = defaultMargin) -> Result
  ```
  All inputs and the output are in **AppKit global coordinates** (y grows upward). Algorithm, in
  this order:
  1. **Below:** `y = recordedRect.minY - margin - pillSize.height`. Fits if
     `y >= visibleFrame.minY` (inclusive).
  2. **Above** (only if below doesn't fit): `y = recordedRect.maxY + margin`. Fits if
     `y + pillSize.height <= visibleFrame.maxY` (inclusive).
  3. **Inside** (neither fits): `y = max(recordedRect.minY, visibleFrame.minY) + margin`, i.e. near
     the bottom edge of the part of the area that is actually visible (a full-screen selection's
     bottom edge is behind the Dock, so it is lifted above the Dock).
  4. Clamp `y` into `[visibleFrame.minY, visibleFrame.maxY - pillSize.height]`.
  5. **Horizontal:** `x = recordedRect.midX - pillSize.width / 2`, clamped into
     `[visibleFrame.minX, visibleFrame.maxX - pillSize.width]`. If the pill is wider than the
     visible frame (not realistic, but must not crash or produce NaN), use `visibleFrame.minX`.
  6. Round `x` and `y` to whole points (`.rounded()`); keep `pillSize` as given.

  **Why `visibleFrame`, not the display's full `frame`:** "room" means room where the pill is
  actually visible and clickable. The menu bar and the Dock sit above normal windows; a pill placed
  in the strip they cover would be hidden behind or confused with them, and an area that ends just
  above the Dock would otherwise get a pill under the Dock. Using `visibleFrame` both for the
  room test and for clamping guarantees the pill is fully visible and stays on the recorded area's
  display (PRD §7.3). The recorded area itself may extend beyond `visibleFrame` (e.g. a full-screen
  selection); that is handled by step 3.

- **`Snapper/Recording/RecordingBorderGeometry.swift`** — `enum RecordingBorderGeometry`:
  ```swift
  static let outset: CGFloat = 3          // window extends 3 pt beyond the recorded rect
  static let strokeInset: CGFloat = 1.5   // line centre, measured from the window edge
  static func windowFrame(recordedRect: CGRect, displayFrame: CGRect) -> CGRect
      // recordedRect.insetBy(dx: -outset, dy: -outset).intersection(displayFrame)
  static func strokeRect(windowSize: CGSize) -> CGRect
      // CGRect(origin: .zero, size: windowSize).insetBy(dx: strokeInset, dy: strokeInset)
  ```
  Everything in AppKit global coordinates (`displayFrame` = the display's full `NSScreen.frame`,
  not `visibleFrame`, since the border may sit under the menu bar/Dock). Result: where the area
  is not at a display edge, the drawn line (centre 1.5 pt outside the area, underlay 2 pt wide →
  0.5–2.5 pt outside) lies **just outside** the recorded rect, so it would not be in the video
  even if exclusion failed. Where the area touches a display edge the window is clamped to the
  display and the line falls 1.5 pt inside the area; there the app-level exclusion (below) is the
  only guarantee, which is acceptable because it is the primary guarantee everywhere.

- **`Snapper/Recording/ElapsedTimeFormatter.swift`** — `enum ElapsedTimeFormatter` with
  `static func string(from duration: Duration) -> String` (an overload taking `TimeInterval` is
  fine). Whole seconds, **truncated** (not rounded). Negative input → `"0:00"`.
  - under one hour: `m:ss` — `"0:00"`, `"0:59"`, `"1:00"`, `"59:59"`
  - one hour or more: `h:mm:ss` — `"1:00:00"`, `"10:05:09"`

  Monospaced digits are a rendering concern of the label (below), not of the formatter.

### Platform glue (AppKit, manually verified)

- **`Snapper/Recording/RecordingPillPanel.swift`** — `@MainActor final class RecordingPillPanel:
  NSPanel`.
  - `styleMask: [.borderless, .nonactivatingPanel]`, `isFloatingPanel = true`,
    `level = .statusBar`, `becomesKeyOnlyIfNeeded = true`, override `canBecomeKey` and
    `canBecomeMain` to return `false` (it must never take key focus, so Esc and other keys keep
    going to the frontmost app), `hidesOnDeactivate = false`, `isMovable = false`,
    `isOpaque = false`, `backgroundColor = .clear`, `hasShadow = true`,
    `isReleasedWhenClosed = false`,
    `collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]`.
  - Content: an `NSVisualEffectView` (`material = .hudWindow`, `blendingMode = .behindWindow`,
    `state = .active`, appearance `.darkAqua`), layer-backed with `cornerRadius = height / 2` and
    `masksToBounds = true`. Height **32 pt**. Inside, a horizontal `NSStackView` (spacing 10,
    edge insets 6/14/6/14 top/left/bottom/right) with, in PRD order:
    1. the elapsed-time label: `NSTextField(labelWithString:)`,
       `NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .medium)`, white;
    2. a **Stop** button (title "Stop");
    3. a **Cancel** button (title "Cancel").
    The PRD's `·` separators denote order; render them as spacing, not as literal glyphs.
  - Buttons are a small `NSButton` subclass (`PillButton`) overriding
    `acceptsFirstMouse(for:) → true`, so one click works without activating Snapper or making
    the panel key. Use `bezelStyle = .inline` (or `.recessed`) so they read well on the HUD.
  - The pill's size is its content's `fittingSize` (height forced to 32). Whenever the fitting
    width changes (the label grows at `10:00` and `1:00:00`), recompute the frame with
    `RecordingPillPlacement.place` so it stays centred and clamped.
  - `func setElapsedText(_:)` updates the label.

- **`Snapper/Recording/RecordingBorderWindow.swift`** — `@MainActor final class
  RecordingBorderWindow: NSWindow`.
  - Borderless, `isOpaque = false`, `backgroundColor = .clear`, `hasShadow = false`,
    `ignoresMouseEvents = true` (click-through), `level = .statusBar`, same
    `collectionBehavior` as the pill, `canBecomeKey`/`canBecomeMain` → `false`,
    `isReleasedWhenClosed = false`.
  - Frame = `RecordingBorderGeometry.windowFrame(recordedRect:displayFrame:)`.
  - A content `NSView` that draws `RecordingBorderGeometry.strokeRect(windowSize:)` twice:
    1. underlay: solid, 2 pt, `NSColor.black.withAlphaComponent(0.35)`;
    2. dashes: 1 pt, `NSColor.white`, dash pattern `[4, 4]`, `lineCapStyle = .butt`.
    This stays visible on light and dark content. Redraw on resize only (no animation).

- **`Snapper/Recording/RecordingOverlayController.swift`** — `@MainActor final class
  RecordingOverlayController`, owned by `AppCoordinator` for the duration of one recording.
  ```swift
  init(display: DisplayInfo, recordedRect: CGRect, startedAt: ContinuousClock.Instant,
       onStop: @escaping @MainActor () -> Void, onCancel: @escaping @MainActor () -> Void)
  func show()
  func update(recordedRect: CGRect)   // for 0013: re-places pill + border (AppKit global coords)
  func dismiss()                      // idempotent; closes both windows, cancels the timer task
  ```
  - `recordedRect` is the recorded area in **AppKit global coordinates**. If the
    `SelectionResult`/`RecordingSession` hands over a CG/SCK (top-left) or display-local rect,
    convert it with `DisplayGeometry` (0004); do not hand-roll the flip here.
  - Looks up the `NSScreen` whose `CGDirectDisplayID`
    (`deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")]`) equals `display.displayID`
    to get `frame` and `visibleFrame`. If the screen is gone, do nothing (0011 stops the recording
    on display disconnect, which calls `dismiss()`).
  - `show()`: creates the border window first, then the pill (so the pill is ordered above it),
    orders both front with `orderFrontRegardless()` (never `makeKeyAndOrderFront`, never
    `NSApp.activate`).
  - **Timer:** a `Task { @MainActor in … }` loop that sleeps 250 ms
    (`try await Task.sleep(for: .milliseconds(250))`), computes
    `ContinuousClock.now - startedAt`, formats it with `ElapsedTimeFormatter`, and sets the label
    only when the string changes. The label therefore changes exactly once per second without
    drift (elapsed time is always derived from the start instant, never accumulated). Chosen over
    `Timer` because it is main-actor isolated under Swift 6 strict concurrency without
    `assumeIsolated`, and it is cancelled in `dismiss()`. The label shows `"0:00"` immediately on
    `show()`.
  - Observes `NSApplication.didChangeScreenParametersNotification` while shown and re-runs
    placement (e.g. the Dock was shown/moved, resolution changed). If the display disappeared,
    it does nothing (see above).
  - Stop/Cancel button actions: first disable both buttons and call `dismiss()` (so a double
    click or Stop-then-Cancel can't fire twice and the overlay never lingers while the stream
    finishes), then call `onStop` / `onCancel`.

- **`AppCoordinator` wiring** (`Snapper/App/AppCoordinator.swift`):
  - Create and `show()` the `RecordingOverlayController` **only after** `RecordingSession` has
    started successfully (after `startCapture` returns without error), so a failed start never
    flashes the pill. `startedAt` = `ContinuousClock.now` captured at that moment.
  - `onStop` → the same stop path ⌘⇧5 and the menu bar icon already use (0011): stop, save, reveal
    in Finder, icon back to `camera.viewfinder`, mode → `idle`.
  - `onCancel` → `cancelRecording()` (below).
  - Every way a recording ends — Stop button, ⌘⇧5, menu bar icon, Cancel, display disconnect
    (0011), write/stream failure (0011's alert path), app quit — calls `overlay.dismiss()` and
    drops the reference. `dismiss()` is idempotent so multiple paths may call it.
  - **Exclusion from capture:** the pill and border are created *after* the `SCContentFilter` was
    built. They are still excluded because 0011 builds the filter with
    `excludingApplications: [our SCRunningApplication]`, which excludes every window owned by our
    process, including ones created later. Do not switch the filter to per-window exclusion
    (`excludingWindows:`) — that would only cover windows that existed when the filter was built.
    Do not rely on `NSWindow.sharingType = .none`; ScreenCaptureKit does not reliably honour it.
    Add a one-line comment at the overlay creation site stating this dependency.

- **Cancel = discard** (`Snapper/Recording/RecordingSession.swift`, `AppCoordinator`): if 0011
  already provides `RecordingSession.cancel()` with these semantics, reuse it; otherwise add it.
  - `cancel() async`: stop the stream/recording output (`stopCapture`, remove the recording
    output), then delete the output file at its URL with `FileManager.removeItem`, ignoring
    "file not found" (Cancel may be clicked before the first frame was written). If 0011 writes to
    a temporary location before moving into the save folder, delete that temporary file instead
    and never move it.
  - No confirmation, no Finder reveal, no alert. Errors while cancelling (stream already failed,
    removal failed) are logged with `os.Logger`, not shown, since the user asked to throw the
    recording away.
  - Afterwards: menu bar icon back to `camera.viewfinder`, `AppMode` → `idle` (through
    `AppModeMachine`, which 0005 already models for `recording → idle`).
  - The pure decision "what happens when a recording ends" stays in 0011; if 0011 has a pure
    end-of-recording mapping (e.g. `.stopped → save & reveal`, `.failed → alert`), add a
    `.cancelled → delete, no reveal, no alert` case to it with a unit test.

- **Esc does nothing** while recording: do not install any global or local key monitor for Esc in
  recording mode. Because neither overlay window can become key, Esc reaches the frontmost app as
  usual. (0007's selection-overlay Esc handling must already be gone once the selection overlay
  closes; the manual check below confirms it.)

- **project.yml:** no changes needed if sources are globbed by folder (0001). New files under
  `Snapper/Recording/` and `SnapperTests/` → run `xcodegen generate`.

- **PRD:** no update needed; this spec implements §7.3/§7.4 as written. Decisions made here are
  implementation details (visibleFrame for "room", 8 pt margin, border styling, timer mechanism).

## Acceptance criteria

- [ ] When a recording starts, a dashed border appears around the recorded area and a pill
      showing `0:00`, **Stop**, **Cancel** appears centred below the area.
- [ ] If there's no room below the area (inside the display's visible frame), the pill is above
      it; if there's no room above either (e.g. full-screen selection), it is inside the area near
      its bottom edge, above the Dock.
- [ ] The pill never leaves the recorded area's display and is never under the menu bar or Dock.
- [ ] The timer ticks once per second (`0:01`, `0:02`, …, `1:00`, …) with digits that don't
      jitter.
- [ ] Clicking **Stop** in the pill saves the `.mp4` and reveals it in Finder, exactly like ⌘⇧5 or
      the menu bar icon; the pill and border disappear.
- [ ] Clicking **Cancel** stops recording immediately with no confirmation, leaves no file (final
      or temporary) behind, doesn't open Finder, and resets the menu bar icon.
- [ ] One click on Stop/Cancel works even when another app is active, and doesn't activate Snapper
      or steal focus from the frontmost app.
- [ ] Esc during a recording does nothing to the recording.
- [ ] Clicks on the border pass through to whatever is underneath.
- [ ] The saved video contains neither the pill nor the border (PRD §12).
- [ ] Pill and border are removed on every end path: Stop, ⌘⇧5, menu bar icon, Cancel, display
      disconnect, recording failure.
- [ ] `RecordingOverlayController.update(recordedRect:)` exists for 0013 and re-places both
      windows.

## Edge cases

- Full-screen selection (area = display `frame`): below/above don't fit → inside, lifted above the
  Dock; border window clamped to the display, line just inside the display edge (exclusion
  covers it).
- Area that ends exactly at `visibleFrame.minY + margin + pillHeight`: below fits (inclusive).
- Area at the bottom of the display just above the Dock → above.
- Area at the left/right edge of the display → pill clamped horizontally, not centred.
- Area narrower than the pill (e.g. 20 × 10 pt) → pill centred on the area, wider than it.
- Secondary display left of / below the main one (negative origin), and a display stacked above
  the main one (positive y origin): all math is in AppKit global coordinates and must work there.
- Secondary display without Dock: `visibleFrame` only excludes the menu bar there; nothing special.
- Auto-hidden Dock: `visibleFrame` reflects it; nothing special.
- Screen parameters change mid-recording (Dock shown, resolution changed): pill re-placed.
- Display disconnected: 0011 stops & saves; overlay dismissed; no crash if the `NSScreen` is gone
  when placement re-runs.
- Double click on Stop, or Stop then Cancel quickly: only the first action happens (buttons
  disabled + `dismiss()` first).
- ⌘⇧5 pressed after Stop was clicked but before the stream finished: must not start a new
  selection on top of a half-stopped session — mode stays `recording` until 0011's stop completes,
  or 0005's machine ignores it; don't show a second overlay.
- Cancel clicked before the first frame was written: file may not exist → ignore not-found.
- Recording on a Space other than the current one / a full-screen app: pill and border join all
  Spaces (`.canJoinAllSpaces`, `.fullScreenAuxiliary`).
- Elapsed time ≥ 1 h → `h:mm:ss`; the pill widens and is re-placed.

## Test plan

- **Unit tests** — `SnapperTests/RecordingPillPlacementTests.swift`. Pill 200 × 32, margin 8.
  Main display `frame` (0, 0, 1440, 900), `visibleFrame` (0, 70, 1440, 805) (Dock 70 pt at the
  bottom, menu bar 25 pt at the top, so `maxY` = 875).
  1. Room below: rect (400, 300, 600, 400) → `.below`, frame (600, 260, 200, 32).
  2. Exactly enough room below: rect (400, 110, 600, 400) → `.below`, y = 70.
  3. Only room above: rect (400, 80, 600, 400) → `.above`, frame (600, 488, 200, 32).
  4. Neither (full-screen `frame`): rect (0, 0, 1440, 900) → `.inside`, frame (620, 78, 200, 32).
  5. Neither (rect = `visibleFrame`): rect (0, 70, 1440, 805) → `.inside`, frame (620, 78, 200, 32).
  6. Near left edge: rect (0, 300, 100, 200) → x clamped to 0, frame (0, 260, 200, 32).
  7. Near right edge: rect (1380, 300, 60, 200) → x clamped to 1240, frame (1240, 260, 200, 32).
  8. Tiny area narrower than the pill: rect (700, 500, 20, 10) → `.below`, frame (610, 460, 200, 32).
  9. Secondary display left of main, negative origin: `visibleFrame` (-1920, -200, 1920, 1055);
     rect (-1000, 100, 400, 300) → `.below`, frame (-900, 60, 200, 32).
  10. Same display, area at its bottom: rect (-1920, -200, 500, 300) → `.above`,
      frame (-1770, 108, 200, 32).
  11. Same display, left-edge clamp: rect (-1920, 300, 50, 50) → x = -1920.
  12. Display stacked above main: `visibleFrame` (0, 900, 1920, 1055); rect (500, 920, 400, 1030)
      → `.inside`, frame (600, 928, 200, 32).
  13. Fractional input rounds: rect (400.4, 300.3, 600, 400) → origin is whole points.
  14. Pill wider than `visibleFrame` → x = `visibleFrame.minX`, no NaN.
- **Unit tests** — `SnapperTests/RecordingBorderGeometryTests.swift`:
  1. rect (400, 300, 600, 400), display (0, 0, 1440, 900) → window frame (397, 297, 606, 406).
  2. Full-display rect (0, 0, 1440, 900) → window frame clamped to (0, 0, 1440, 900).
  3. Negative-origin display (-1920, -200, 1920, 1080), rect (-1920, 0, 500, 500) at its left
     edge → (-1920, -3, 503, 506).
  4. `strokeRect(windowSize: (606, 406))` → (1.5, 1.5, 603, 403).
- **Unit tests** — `SnapperTests/ElapsedTimeFormatterTests.swift`: 0 s → `"0:00"`; 0.999 s →
  `"0:00"` (truncation); 59 s → `"0:59"`; 60 s → `"1:00"`; 61 s → `"1:01"`; 3599 s → `"59:59"`;
  3600 s → `"1:00:00"`; 36309 s → `"10:05:09"`; −5 s → `"0:00"`.
- **Unit tests** — if 0011 has a pure end-of-recording mapping: `.cancelled` → delete, no reveal,
  no alert.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug
  build` and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (real GUI session, Screen Recording permission granted; can't be
  automated):
  1. ⌘⇧5, drag a medium rectangle in the middle of the screen. Expected: dashed border around the
     area, pill centred 8 pt below it showing `0:00`, then `0:01`, `0:02` … once per second; digits
     don't shift sideways.
  2. ⌘⇧5, drag a rectangle whose bottom edge is right above the Dock. Expected: pill above the
     area.
  3. ⌘⇧5, drag over the entire screen (corner to corner). Expected: pill inside the area near the
     bottom, fully above the Dock and fully visible.
  4. ⌘⇧5, drag a small rectangle touching the left (then right) screen edge. Expected: pill fully
     on screen, not cut off.
  5. With a second display: record an area on it. Expected: pill and border on that display only.
  6. While recording, click into another app (e.g. TextEdit) so it is frontmost, type a few
     characters, then click **Stop** once. Expected: the single click stops the recording; typing
     focus was never stolen; Finder opens the save folder with the new `Recording … .mp4`
     selected; pill and border gone; menu bar icon back to `camera.viewfinder`.
  7. Play that `.mp4` in QuickTime. Expected: no pill and no dashed border anywhere in the video
     (also for the full-screen recording from step 3, where the pill was inside the area).
  8. Start a recording, note the file name it will get (time shown in the menu bar clock), click
     **Cancel**. Expected: recording stops at once, no dialog, Finder doesn't open, no new file in
     the save folder; `ls -la ~/Documents/Screenshots` and `ls $TMPDIR` show no leftover
     recording file.
  9. Start a recording, press Esc several times, wait a few seconds, press ⌘⇧5. Expected: Esc did
     nothing (timer kept running); ⌘⇧5 saves a video whose length includes the time after Esc.
  10. Start a recording and click on a link/button that is directly under the dashed border line.
      Expected: the click reaches the underlying app.
  11. Start a recording, stop via the menu bar icon; start another, stop via ⌘⇧5. Expected: pill
      and border disappear in both cases.
  12. Start a recording, switch to another Space / a full-screen app. Expected: pill and border
      remain visible and clickable.
  13. Click **Stop** twice quickly. Expected: one file saved, no error, no second Finder window.

## Out of scope

- Following a moving window (pill/border tracking, ~4 Hz frame polling, window close → stop &
  save): **0013-window-recording**. This spec only provides `update(recordedRect:)`.
- Starting/stopping the stream, file naming, Finder reveal, display-disconnect stop, failure alerts,
  menu bar icon state: **0011-recording-core**.
- Microphone audio: **0014-microphone-audio**.
- Selection overlay and its Esc handling: **0007** / **0009**.
- Pause/resume, countdown, dragging the pill, click highlighting: not in the PRD (§2 non-goals).
