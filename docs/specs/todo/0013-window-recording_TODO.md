# 0013 — Record a single window that the capture, pill, and border follow

> Source: docs/PRD.md §7.1 (window mode), §7.3 (pill and border follow the window, ~4 polls/s), §7.4 (recorded window closes → stop and save), §12 ("Window recordings follow the window, and closing the recorded window stops and saves the recording")
> Depends on: 0004-display-geometry, 0009-window-selection-mode, 0011-recording-core, 0012-recording-overlay

## Context

Nothing is implemented yet. This spec builds on types introduced by lower-numbered specs:

- `DisplayInfo` and the AppKit ↔ CG conversions in `Snapper/Capture/DisplayGeometry.swift` (0004).
- `SelectionResult.window` and `WindowCandidate` (`Snapper/Capture/WindowHitTester.swift`) from 0009. A window selection carries the `CGWindowID` and the window's frame. 0009 also defines the "display with the largest overlap" rule that picks a window's display.
- `RecordingSession` (`Snapper/Recording/RecordingSession.swift`, `SCStream` + `SCRecordingOutput`) and the pure `RecordingConfigurationBuilder` from 0011. As an **interim** measure, 0011 records a window selection by treating the window's frame as a rectangle on its display. That recording does not follow the window and includes anything covering it.
- The recording overlay from 0012: `RecordingPillPanel`, `RecordingBorderWindow`, the pure `RecordingPillPlacement`, and an `update(recordedRect:)` entry point that repositions the pill and border.

What the PRD requires and is missing:

- §7.1: "Window mode records only that window. The capture follows the window as it moves, and anything covering it is excluded."
- §7.3: "In window mode, the pill and the border follow the window as it moves (its frame is polled about 4 times per second)."
- §7.4: "If the recorded window closes … the recording stops and is saved as if Stop had been pressed."
- §7.2 still applies: `.mp4`, H.264, native resolution, 30 fps, cursor visible.

## User story

As a Snapper user, I press ⌘⇧5, press Space, and click a window. Snapper records only that window, even when I move it, drag it to another display, or cover it with another window. The pill and the dashed border move with the window. If I close the window, the recording is saved and revealed in Finder, just as if I had pressed Stop.

## Scope & changes

### Pure logic (unit-tested)

- **`RecordingConfigurationBuilder` window variant** (`Snapper/Recording/RecordingConfigurationBuilder.swift`, extend 0011's type). Add a pure function that returns the output pixel size for a window recording:
  - Inputs: the window's content size in points (`SCContentFilter.contentRect.size`) and `SCContentFilter.pointPixelScale`.
  - Output: `width = evenFloor(round(points.width × scale))` and `height = evenFloor(round(points.height × scale))`, each at least 2. `evenFloor` rounds an odd pixel count down to the nearest even number, because H.264 needs even dimensions. Use the same even-rounding helper as 0011's rect variant so both paths agree. If 0011 already caps the maximum encoder size, apply the same cap here, scaled down with the aspect ratio preserved.
  - Return a small value type (for example, `WindowRecordingConfiguration { pixelWidth, pixelHeight, scalesToFit: true, preservesAspectRatio: true, ignoresShadowsSingleWindow: true, showsCursor: true, frameRate: 30 }`) that the glue copies onto `SCStreamConfiguration`. This makes the flags part of a tested value instead of being set ad hoc.
- **Decision: the output size is fixed at the start, and resizes are letterboxed.** An H.264 `.mp4` track can't change dimensions mid-file. The output size is therefore computed once, from the window's size when recording starts. If the window is later resized (or moves to a display with a different scale), ScreenCaptureKit scales the window into the fixed frame with its aspect ratio preserved (`scalesToFit = true`, `preservesAspectRatio = true`, both available since macOS 14), and the leftover area is letterboxed. The recording is at native resolution as long as the window keeps its starting size, which is what §7.2 asks for. The PRD doesn't say anything else about resizes.
- **Window polling model** (new file `Snapper/Recording/WindowTracking.swift`, all pure, no AppKit):
  - `enum WindowPollResult: Equatable { case unavailable; case gone; case present(cgBounds: CGRect, isOnscreen: Bool) }`
    - `.unavailable` means the `CGWindowListCopyWindowInfo` call itself returned `nil`. That is a transient failure, and the window is not treated as closed.
    - `.gone` means the call succeeded and returned no entry for the window ID.
  - `enum WindowInfoParser`, `static func parse(_ list: [[String: Any]]?, windowID: CGWindowID) -> WindowPollResult`. It finds the entry whose `kCGWindowNumber` equals `windowID`, reads `kCGWindowBounds` with `CGRect(dictionaryRepresentation:)`, and reads `kCGWindowIsOnscreen` (a missing key counts as `false`). An entry with unreadable bounds counts as `.unavailable`. The parser takes plain dictionaries, so tests can feed it fakes.
  - `struct WindowTrackingState: Equatable { var appKitFrame: CGRect; var isVisible: Bool; var isClosed: Bool }`, seeded from the selection frame with `isVisible = true`.
  - `enum WindowTrackingEvent: Equatable { case unchanged; case moved(CGRect); case hidden; case shown(CGRect); case closed }`. All rects are in AppKit global coordinates.
  - `enum WindowTrackingReducer`, `static func reduce(_ state: WindowTrackingState, poll: WindowPollResult, primaryDisplayHeight: CGFloat) -> (WindowTrackingState, WindowTrackingEvent)`:
    - If `state.isClosed`, the result is always `.unchanged`, so `.closed` is emitted only once.
    - `.unavailable` → `.unchanged`.
    - `.gone` → `.closed`, and `isClosed` becomes `true`.
    - `.present(bounds, onscreen)`: convert `bounds` from CG top-left to AppKit bottom-left **using `DisplayGeometry`** (0004). Don't write the conversion inline.
      - `onscreen == false` while visible → `.hidden` (see Open question 1).
      - `onscreen == true` while hidden → `.shown(frame)`.
      - visible and the frame differs from `state.appKitFrame` → `.moved(frame)`.
      - Otherwise → `.unchanged`.
      - Frames are compared after rounding to whole points, so sub-point jitter doesn't cause redraws.
- **Pill display for a moving window.** The pill's display is the display with the largest overlap with the window's current AppKit frame. Use the helper that 0009 introduced for this rule. If that rule isn't exposed in `DisplayGeometry`, move it there (keeping 0009's tests green) rather than duplicating it. If the window overlaps no display, keep the last display.

### Platform glue (manually verified)

- **`WindowFrameTracker`** (new file `Snapper/Recording/WindowFrameTracker.swift`, a `@MainActor final class`):
  - `init(windowID: CGWindowID, initialFrame: CGRect, onEvent: @MainActor (WindowTrackingEvent) -> Void)`, plus `start()` and `stop()`.
  - Uses a `Timer` with a 0.25 s interval, added to `RunLoop.main` in `.common` mode so it keeps firing while a menu is open. The timer closure is `@Sendable`, so it calls back in with `MainActor.assumeIsolated`.
  - Each tick calls `CGWindowListCopyWindowInfo([.optionIncludingWindow], windowID)`, then passes the result through `WindowInfoParser`, then through `WindowTrackingReducer` with the current primary display height (read from `NSScreen.screens` via the 0004 helper). It forwards any event other than `.unchanged`. `CGWindowListCopyWindowInfo` isn't deprecated, and it's used here only for bounds. Never use `CGWindowListCreateImage`.
  - After `.closed`, the tracker invalidates its timer. `stop()` is idempotent.
- **Window stream in `RecordingSession`** (extend 0011). When the selection is `.window`:
  1. Fetch `SCShareableContent` and find the `SCWindow` whose `windowID` matches. If it's missing (the window closed between the click and the start), show the generic error alert from 0008's `ErrorAlerts` and return to idle. This follows §7.3: "Any other failure to start … shows a short alert".
  2. Build `SCContentFilter(desktopIndependentWindow: scWindow)`. This filter captures only that window, independent of what covers it and of which display it's on. Our pill and border are therefore excluded by construction, and the `excludingApplications:` rule (which applies to display filters) isn't needed here. Add a code comment that says so.
  3. Compute the output size with the builder from `filter.contentRect.size` and `filter.pointPixelScale`, and copy the builder's configuration onto `SCStreamConfiguration`: `width`/`height`, `scalesToFit`, `preservesAspectRatio`, `ignoreShadowsSingleWindow = true`, `showsCursor = true`, `minimumFrameInterval = 1/30`, and the microphone settings as 0011/0014 set them.
  4. Remove 0011's interim "window frame as a rect" path for recordings. 0011's display-filter path for rectangle selections is unchanged.
- **Wiring in `AppCoordinator`** (extend 0011/0012). For a window recording, create a `WindowFrameTracker` once the stream has started:
  - `.moved(rect)` and `.shown(rect)`: call the overlay's `update(recordedRect:)` with the new frame and the largest-overlap display. If 0012's `update(recordedRect:)` doesn't take a display, extend it to `update(recordedRect:on: DisplayInfo)` so the pill re-places itself with `RecordingPillPlacement` against the window's **current** display. Show the border and pill again on `.shown`.
  - `.hidden`: follow the recommendation in Open question 1 until the user answers it. Recording continues, the border is hidden, and the pill stays at its last position so Stop and Cancel stay reachable.
  - `.closed`: run exactly the same path as Stop (stop the stream, finalize the file, reveal it in Finder with `NSWorkspace.activateFileViewerSelecting`, restore the `camera.viewfinder` icon).
  - The tracker is stopped on every exit path: Stop, ⌘⇧5, the menu bar icon, Cancel, window closed, and errors.
- **Stream stops with an error in window mode.** If `SCStreamDelegate.stream(_:didStopWithError:)` fires during a window recording, hop to the main actor and poll the window once synchronously through the parser. If the result is `.gone`, treat it as `.closed` (stop and save, no alert). Otherwise, follow 0011's error path (the alert from §7.3). The stop path must be **idempotent**: if the tracker and the delegate both report the closure, only one save and one Finder reveal happen. Guard on the session state, for example a `RecordingSession` that is already stopping ignores a second stop.
- **Display disconnects in window mode.** 0011 stops and saves when the *recorded display* disconnects. A window recording has no recorded display, because macOS moves the window to a remaining display and the tracker follows it. During window recordings, 0011's display-disconnect stop is therefore skipped, and the window tracker alone decides. See Open question 2.
- **`project.yml`**: no changes to targets or keys. Run `xcodegen generate` after adding the new source and test files.

## Acceptance criteria

- [ ] A window recording (⌘⇧5 → Space → click a window) produces an `.mp4` that contains only that window, without its shadow, with the cursor visible, at 30 fps.
- [ ] The output's pixel size equals the window's starting size × its display scale, rounded down to even numbers. For example, an 800 × 600 pt window on a 2× display gives 1600 × 1200.
- [ ] Moving the window during the recording doesn't move the content in the video. The window stays framed.
- [ ] Windows that cover the recorded window don't appear in the video.
- [ ] Neither the pill nor the dashed border appears in the video.
- [ ] The pill and the border follow the window within about 0.25 s of it moving, including when it's dragged to another display. The pill is placed with 0012's below/above/inside rule against the window's current display.
- [ ] Closing the recorded window stops the recording and saves it as if Stop had been pressed: the file is saved, Finder reveals it, and the menu bar icon returns to `camera.viewfinder`. Exactly one file is saved and one Finder reveal happens.
- [ ] Stop (button, ⌘⇧5, menu bar icon) and Cancel behave exactly as in rectangle mode, and the tracker stops in every case.
- [ ] Resizing the window during the recording doesn't fail the recording. The content is scaled to fit inside the fixed frame and letterboxed.
- [ ] Rectangle recordings (0011/0012) behave exactly as before.

## Edge cases

- **Secondary display with a negative origin** (left of or below the main display): the CG → AppKit conversion of the polled bounds must go through `DisplayGeometry`, and it is covered by tests.
- **A window that spans two displays:** the border uses the full window frame. The pill goes on the display with the largest overlap.
- **A window dragged to a display with a different scale** (2× → 1×): the output size stays fixed, and ScreenCaptureKit scales the content into it. There's no restart and no error.
- **The window closes before the stream starts:** a short error alert appears, and the app returns to idle without leaving a file behind.
- **A transient `nil` from `CGWindowListCopyWindowInfo`:** this is `.unavailable` → `.unchanged`. It never counts as a close.
- **The tracker and the stream delegate both detect the close:** one save and one reveal only.
- **Minimized window, window on another Space, or app hidden** (`kCGWindowIsOnscreen == false`): `.hidden`, handled as described in Open question 1. It never counts as a close.
- **A very large window** (for example, a 5K full-size window at 2×): apply 0011's encoder size cap, if it has one, with the aspect ratio preserved.
- **The recorded window belongs to an app that quits:** the window disappears from the list → `.closed` → stop and save.
- **Cancel during a window recording:** the tracker stops and the file is discarded, as in 0012.

## Test plan

- **Unit tests**
  - `SnapperTests/WindowTrackingReducerTests.swift`:
    - Same bounds as the state → `.unchanged`, and the state is unchanged.
    - New bounds on the primary display (primary height 900, CG `(100, 100, 800, 600)`) → `.moved(AppKit (100, 200, 800, 600))`.
    - Secondary display to the left with a negative x (AppKit `(-1920, 0, 1920, 1080)`, primary height 900): CG `(-1820, -80, 800, 600)` → `.moved((-1820, 380, 800, 600))`.
    - Secondary display below the primary (AppKit `(0, -1080, 1920, 1080)`, primary height 900): CG `(100, 1000, 400, 300)` → `.moved((100, -400, 400, 300))`.
    - Sub-point jitter (bounds differ by 0.3 pt) → `.unchanged`.
    - `.gone` → `.closed`, and `isClosed` becomes `true`. A second `.gone` (or any poll) after that → `.unchanged`.
    - `.unavailable` → `.unchanged`, and the state is unchanged.
    - `isOnscreen == false` while visible → `.hidden`. `isOnscreen == true` again (moved bounds) → `.shown(newFrame)`. Still hidden on the next off-screen poll → `.unchanged`.
  - `SnapperTests/WindowInfoParserTests.swift`:
    - `nil` list → `.unavailable`. An empty list → `.gone`. A list with only other window numbers → `.gone`.
    - A matching entry with a bounds dictionary (built with `CGRect.dictionaryRepresentation`) and `kCGWindowIsOnscreen = true` → `.present(rect, true)`. A missing on-screen key → `isOnscreen == false`.
    - A matching entry with malformed bounds → `.unavailable`.
  - `SnapperTests/RecordingConfigurationBuilderTests.swift` (extend 0011's file):
    - 800 × 600 pt at 1× → 800 × 600. At 2× → 1600 × 1200.
    - 801 × 601 pt at 1× → 800 × 600 (odd values rounded down to even).
    - 400.5 × 300.25 pt at 2× → 801 × 600.5 → 800 × 600 after rounding.
    - 1 × 1 pt at 1× → 2 × 2 (the minimum).
    - The window configuration has `scalesToFit`, `preservesAspectRatio`, `ignoresShadowsSingleWindow`, and `showsCursor` all `true`, and a frame rate of 30.
    - If 0011 defines an encoder size cap: an over-cap window is scaled down to fit inside the cap, with both dimensions even and the aspect ratio preserved within 1 px.
  - If the largest-overlap helper is moved into `DisplayGeometry`: its existing 0009 tests still pass. Add one case where the window spans two displays with 70/30 overlap (the larger display wins) and one where the window overlaps no display (`nil`, and the caller keeps the last display).
- **Build check:** `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and `xcodebuild -scheme Snapper test` all succeed.
- **Manual verification** (needs a real GUI session with Screen Recording permission granted; can't be automated):
  1. Open a TextEdit window. Press ⌘⇧5, press Space, and click the TextEdit window. **Expected:** recording starts immediately, the menu bar icon turns into a red `record.circle`, and the dashed border and pill surround the window.
  2. Drag the TextEdit window around for a few seconds. **Expected:** the border and pill follow it (with a slight lag of at most about 0.25 s), and the pill stays below the window, flipping above or inside near the screen edges.
  3. Drag another app's window over TextEdit, then type in TextEdit. Stop with the pill's Stop button. **Expected:** Finder opens with `Recording … .mp4` selected. In QuickTime, the video shows only the TextEdit window (no shadow, no desktop, no covering window), stays framed while the window moves, shows the cursor over it, and contains no pill or border. In Finder's Get Info, the dimensions are the window's size × 2 on Retina.
  4. With two displays: start a window recording and drag the window onto the other display (try a non-Retina external display if you have one). **Expected:** the pill and border move to that display, the recording continues, and the video stays framed (scaled if the scale changed).
  5. Start a window recording and close the window (⌘W). **Expected:** within about 0.25 s, the pill and border disappear, the icon returns to `camera.viewfinder`, and Finder reveals one new `.mp4` that plays up to the moment of closing.
  6. Start a window recording and quit the window's app (⌘Q). **Expected:** same as step 5.
  7. Start a window recording and press ⌘⇧5. **Expected:** it is saved and revealed. Repeat, clicking the menu bar icon. **Expected:** same. Repeat, clicking Cancel. **Expected:** no file is saved, and the overlay disappears.
  8. Start a window recording and resize the window larger and smaller. **Expected:** the recording continues, the border and pill track the new frame, and the video shows the window scaled to fit inside the original frame, with bars where the aspect ratio differs.
  9. Start a window recording and minimize the window (⌘M), wait, then restore it from the Dock. **Expected (pending Open question 1):** the recording keeps going, the border is hidden while the window is minimized and reappears on restore, and the pill stays usable throughout.
  10. Regression: a rectangle recording (⌘⇧5, drag) still works as in 0011/0012.

## Open questions

None. User confirmed on 2026-11-03: Minimized, hidden, or off-screen windows keep recording, with the border hidden and pill reachable at its last position; restore the border on return. Window recordings follow the window to a remaining display after disconnection; only rectangle recordings stop for recorded-display disconnection. Recorded in docs/PRD.md.

## Out of scope

- Selecting and highlighting windows, the Space toggle, and window hit-testing: 0009.
- The recording session lifecycle, the menu bar icon state, file naming, saving, revealing in Finder, and rectangle recordings: 0011 (plus 0002 for naming).
- Pill and border UI, the elapsed-time readout, the placement rule, and Cancel: 0012. This spec only drives `update(recordedRect:)`.
- The microphone track: 0014. Window recordings inherit whatever audio configuration 0011/0014 apply.
- Changing the output size mid-recording, or restarting the stream on resize. This isn't possible within one H.264 track, as decided above.
- Recording several windows, or a whole app: not in the PRD.
