# 0011 — Record a selected area to an H.264 .mp4 and reveal it in Finder on Stop

> Source: docs/PRD.md §4 (lines 49, 60), §7 items 1, 2 (video only), 3 (errors bullet, line 99), 4, 5 (lines 89–106), §9 (lines 152–162), §12 (lines 197–198)
> Depends on: 0002-file-naming-save-folder, 0003-app-settings, 0004-display-geometry, 0005-global-shortcuts-app-mode, 0006-permissions-onboarding, 0007-selection-overlay-rectangle, 0008-screenshot-rectangle-capture, 0009-window-selection-mode

## Context

Not yet implemented. After 0001–0010, ⌘⇧5 (and the menu's *Record* item) opens the selection
overlay with purpose `CapturePurpose.record` (0005/0007), and the overlay produces a
`SelectionResult` (`.rect(display:rect:)` from 0007, `.window(...)` from 0009). Today
`AppCoordinator.startRecording()` / the `.record` selection result is a stub: nothing records.

This spec delivers the recording core, as the PRD describes it:

- §7.1: "Recording **starts immediately**, with no countdown."
- §7.2: "`.mp4`, H.264, native resolution, 30 fps, cursor visible." (Microphone is 0014.)
- §7.3 errors: "if Screen Recording permission is missing, the onboarding window opens. Any other
  failure to start or write the recording shows a short alert with the error."
- §7.4: "**Stop** saves the file. Pressing ⌘⇧5 again or clicking the menu bar icon does the same."
  "Esc does **not** cancel a recording." "⌘⇧4 and ⌘⇧6 are ignored while a recording is in
  progress." "If … the recorded display is disconnected, the recording stops and is saved as if
  Stop had been pressed."
- §7.5: "After saving, Finder opens the save folder with the new file **selected**
  (`NSWorkspace.activateFileViewerSelecting`). Recordings are not copied to the clipboard."
- §4: "While recording, [the icon] changes to a red `record.circle`." "While recording, clicking
  the icon (either button) stops the recording and saves it."
- §9: `Recording 2026-11-02 at 19.18.04.mp4`, "recordings use the time the recording started",
  collision suffixes, and the "Couldn't save to <folder>" alert with **Choose Folder…**.

Types this spec relies on from earlier specs (canonical names; use their actual signatures):
`FileNamer`, `CaptureKind.recording`, `SaveFolder` (+ `uniqueFileURL(kind:date:)`,
`SaveFolderError`) from 0002; `AppSettings` from 0003; `DisplayInfo` and `DisplayGeometry` from
0004; `AppMode`, `AppModeMachine`, `CapturePurpose`, `ShortcutNames` from 0005;
`ScreenRecordingPermission` and `OnboardingWindowController` from 0006;
`SelectionOverlayController`, `SelectionResult` from 0007/0009; `ErrorAlerts` from 0008;
`StatusItemController`, `AppCoordinator` from 0001.

## User story

As a Snapper user, I press ⌘⇧5, drag a rectangle (or pick a window), and recording starts at once.
The menu bar icon turns into a red record symbol. When I press ⌘⇧5 again or click the icon, the
recording is saved as `Recording <start time>.mp4` in my save folder and Finder opens with the file
selected.

## Scope & changes

### Pure logic (unit-tested, no AppKit / ScreenCaptureKit imports)

- **`Snapper/Recording/RecordingConfigurationBuilder.swift`** (pure; may import `CoreGraphics`,
  `CoreMedia`, `CoreVideo` for value types only).
  - Input: `display: DisplayInfo`, `selection: CGRect` (AppKit **global** points, bottom-left
    origin), `primaryDisplayHeight: CGFloat` if `DisplayGeometry` needs it, `showsCursor: Bool =
    true`, `framesPerSecond: Int = 30`.
  - Output: `RecordingConfiguration` (`struct`, `Sendable`, `Equatable`):
    - `displayID: CGDirectDisplayID`
    - `sourceRect: CGRect`: **display-local, top-left origin, in points** (what
      `SCStreamConfiguration.sourceRect` expects).
    - `pixelWidth: Int`, `pixelHeight: Int`: output size in pixels, **both even** (H.264 requires
      even dimensions).
    - `minimumFrameInterval: CMTime` = `CMTime(value: 1, timescale: 30)`
    - `pixelFormat: OSType` = `kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange` (`'420v'`, the
      native input format for the H.264 encoder; avoids a BGRA→YUV conversion).
    - `queueDepth: Int` = 5
    - `showsCursor: Bool` = true
    - `videoCodec` / `fileType`: constants the glue uses for `SCRecordingOutputConfiguration`:
      `AVVideoCodecType.h264` and `AVFileType.mp4`. Keep these as plain constants in the glue
      (`RecordingSession`) if importing AVFoundation into the pure file is undesirable. Either way,
      the test asserts on whatever the builder exposes.
  - Algorithm (all conversions through `DisplayGeometry`; no ad-hoc flipping):
    1. Clamp `selection` to `display.frame` (`DisplayGeometry` clamp). Selections are always
       confined to one display (AGENTS.md).
    2. Convert to display-local top-left points: `localX = rect.minX - display.frame.minX`,
       `localY = display.frame.maxY - rect.maxY` (use the `DisplayGeometry` display-local helper
       built for SCK `sourceRect` in 0004).
    3. Snap to the pixel grid: `originPx = round(local * scale)`; `widthPx = floor(w * scale)`,
       `heightPx = floor(h * scale)`, then **round each down to the nearest even number**, with a
       minimum of 2. If the snapped rect would extend past the display edge, shift the origin back
       inside it.
    4. `sourceRect = CGRect(x: originPx.x / scale, y: originPx.y / scale, width: widthPx / scale,
       height: heightPx / scale)`. Deriving `sourceRect` from the even pixel size means SCK never
       rescales: 1 source pixel = 1 output pixel ("native resolution", §7.2).
  - Microphone: **not** an input here. 0014 adds the `recordsMicrophone` input.

- **`Snapper/Recording/RecordingLifecycle.swift`** (pure state machine, `struct`, `Sendable`).
  `RecordingSession` keeps one instance, feeds it events, and executes the effects it returns. That
  way every ordering question (double stop, stop while starting, stream error during stop) is
  decided in tested code.
  - `enum RecordingPhase: Equatable { case idle, starting, recording, stopping, finished(URL),
    failed(RecordingFailure), cancelled }`. `RecordingFailure` is an `Equatable` value (message +
    kind `.permissionDenied` / `.other`) so it can be tested without `Error` equality.
  - Events: `start(url:)`, `captureStarted`, `captureFailedToStart(RecordingFailure)`,
    `stopRequested`, `cancelRequested`, `outputFinished`, `outputFailed(RecordingFailure)`,
    `streamStopped(RecordingFailure)` (`SCStreamDelegate didStopWithError`),
    `stopTimedOut(fileHasContent: Bool)`.
  - Effects (returned as `[RecordingEffect]`): `.startCapture`, `.stopCapture`,
    `.scheduleStopTimeout`, `.reveal(URL)`, `.deleteFile(URL)`, `.showError(RecordingFailure)`,
    `.showOnboarding`, `.ended` (tells the coordinator to return `AppMode` to `.idle` and restore
    the icon).
  - Transitions:
    - `idle --start(url)--> starting` ⇒ `[.startCapture]`.
    - `starting --captureStarted--> recording` ⇒ `[]`. If a stop was requested while starting
      (`pendingStop`), go straight to `stopping` ⇒ `[.stopCapture, .scheduleStopTimeout]`. If a
      cancel was pending, go to `stopping` with the cancel flag.
    - `starting --captureFailedToStart(f)-->` `failed(f)` ⇒ `[.deleteFile(url), .showOnboarding
      or .showError(f), .ended]` (`.showOnboarding` when `f.kind == .permissionDenied`).
    - `starting --stopRequested-->` stays `starting` and sets `pendingStop` (no effects).
      `cancelRequested` sets `pendingCancel` (cancel wins over stop).
    - `recording --stopRequested--> stopping` ⇒ `[.stopCapture, .scheduleStopTimeout]`.
    - `recording --cancelRequested--> stopping(cancel)` ⇒ `[.stopCapture,
      .scheduleStopTimeout]`.
    - `recording --streamStopped(f)--> stopping` ⇒ `[.scheduleStopTimeout]` (the stream already
      stopped; wait for the recording output to finish or fail). The display-disconnect path
      arrives here or through `stopRequested`. Whichever comes first wins and the other is ignored.
    - `stopping --outputFinished-->` `finished(url)` ⇒ `[.reveal(url), .ended]`, or for a cancel,
      `cancelled` ⇒ `[.deleteFile(url), .ended]`.
    - `stopping --outputFailed(f)` or `stopTimedOut(fileHasContent:)-->`: for a cancel, `cancelled`
      ⇒ `[.deleteFile(url), .ended]`. Otherwise, if the file has content, `finished(url)` ⇒
      `[.reveal(url), .showError(f), .ended]`. If it doesn't, `failed(f)` ⇒ `[.deleteFile(url),
      .showError(f), .ended]`. For `outputFailed`, the session checks the file size and passes
      the result in (see glue).
    - `recording --outputFailed(f)-->` (writer failed mid-recording) ⇒ the same as
      `stopping --outputFailed`, plus `.stopCapture` first.
    - **Idempotence:** `stopRequested` / `cancelRequested` in `stopping`, `finished`, `failed`,
      `cancelled`, or `idle` ⇒ no state change, `[]`. Any late callback in a terminal phase ⇒ `[]`.
  - Stop timeout constant: `RecordingLifecycle.stopTimeout = 5` seconds.

- **`Snapper/App/AppModeMachine.swift`** (extend 0005's pure machine if it doesn't already cover
  these; add only what's missing):
  - `.selecting(.record)` + selection completed ⇒ `.recording`, action `startRecording(selection)`.
  - `.recording` + record shortcut (⌘⇧5) ⇒ action `stopRecording`, mode stays `.recording` until
    the session reports `.ended`.
  - `.recording` + screenshot or annotate shortcut ⇒ ignored (no action).
  - `.recording` + status item click (left **or** right) ⇒ `stopRecording` (no menu, no Settings).
  - `.recording` + menu items: not reachable (no menu is shown while recording).
  - `.recording` + `recordingEnded` ⇒ `.idle`.
  - Esc is not an input to the machine while recording (there's no overlay and no key monitor). Esc
    does nothing, as §7.4 requires.

- **Display-disconnect decision** (pure helper, for example a `static func` on
  `RecordingLifecycle` or `DisplayGeometry`):
  `isRecordedDisplayGone(recorded: CGDirectDisplayID, current: [CGDirectDisplayID]) -> Bool`.

### Platform glue (manually verified)

- **`Snapper/Recording/RecordingSession.swift`**: `@MainActor final class`, the facade the
  coordinator talks to.
  - `init(configuration: RecordingConfiguration, outputURL: URL, delegate/onEnded closure)`.
  - `start()`:
    1. Fetch `SCShareableContent.current` (or `excludingDesktopWindows(false,
       onScreenWindowsOnly: true)`). Find the `SCDisplay` with `displayID ==
       configuration.displayID` and the `SCRunningApplication` whose `processID ==
       getpid()`/`bundleIdentifier == Bundle.main.bundleIdentifier`.
    2. Filter: `SCContentFilter(display: scDisplay, excludingApplications: [ownApp],
       exceptingWindows: [])`. Excluding the **application**, not individual windows, also covers
       windows created after the stream starts (the 0012 pill and border). If our own
       `SCRunningApplication` is missing from the shareable content (no on-screen windows yet), fall
       back to `excludingApplications: []`. This is safe because no Snapper window is on screen at
       that moment. 0012 must keep a window on screen before starting, or rebuild the filter with
       `stream.updateContentFilter`. Note this in a code comment.
    3. `SCStreamConfiguration`: `sourceRect`, `width = pixelWidth`, `height = pixelHeight`,
       `minimumFrameInterval`, `pixelFormat`, `queueDepth`, `showsCursor = true`,
       `scalesToFit = false`, `capturesAudio = false`, `captureMicrophone = false` (0014).
    4. `SCRecordingOutputConfiguration`: `outputURL`, `videoCodecType = .h264`,
       `outputFileType = .mp4`. Create `SCRecordingOutput(configuration:delegate:)`.
    5. `let stream = SCStream(filter:configuration:delegate:)`, then
       `try stream.addRecordingOutput(recordingOutput)`, then `try await stream.startCapture()`.
       Success ⇒ `lifecycle.captureStarted`. A thrown error ⇒ `captureFailedToStart` with
       `.permissionDenied` if it's `SCStreamError.userDeclined` (or
       `ScreenRecordingPermission` now reports denied), `.other` otherwise.
  - `stop()` ⇒ `lifecycle.stopRequested`. `cancel()` ⇒ `lifecycle.cancelRequested`. `cancel()`
    has no UI caller until 0012 adds the Cancel button, but the API and its lifecycle path live
    here.
  - Effects executor: `.startCapture` / `.stopCapture` call `stream.startCapture()` /
    `stream.stopCapture()` (an error from `stopCapture` is ignored; the output callbacks decide the
    outcome). `.scheduleStopTimeout` starts a `Task` that sleeps `stopTimeout` seconds, then feeds
    `stopTimedOut(fileHasContent:)`. `.reveal(url)` calls
    `NSWorkspace.shared.activateFileViewerSelecting([url])`. `.deleteFile` calls
    `try? FileManager.default.removeItem(at:)`. `.showError` calls `ErrorAlerts` (0008) with a
    short title, for example "Couldn't record the screen", plus the error's
    `localizedDescription`. `.showOnboarding` opens `OnboardingWindowController` (0006). `.ended`
    calls the coordinator's callback. Nothing is put on the clipboard (§7.5).
  - "File has content": `(try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0 > 0`.
  - **Delegate threading:** `SCStreamDelegate` and `SCRecordingOutputDelegate` callbacks arrive on
    arbitrary queues. Implement them on a small `nonisolated final class RecordingDelegateProxy:
    NSObject, SCStreamDelegate, SCRecordingOutputDelegate, Sendable` that holds a `@Sendable`
    closure and forwards each callback with `Task { @MainActor in … }`. Don't use
    `MainActor.assumeIsolated`, because the callbacks are not on the main queue. The proxy holds no mutable state.
    Callbacks: `recordingOutputDidStartRecording` (log only), `recordingOutputDidFinishRecording` ⇒
    `outputFinished`, `recordingOutput(_:didFailWithError:)` ⇒ `outputFailed`,
    `stream(_:didStopWithError:)` ⇒ `streamStopped`.
  - Display disconnect: observe `NSApplication.didChangeScreenParametersNotification` while
    recording. On each notification, collect `NSScreen.screens` display IDs (via the
    `NSScreenNumber` device description key, the same way 0004/0007 build `DisplayInfo`). If
    `isRecordedDisplayGone`, call `stop()`. Remove the observer on `.ended`.

- **Output file location: write directly to the final URL.** At start, the coordinator resolves
  the save folder with `SaveFolder` (create if missing, writability check) and computes
  `SaveFolder.uniqueFileURL(kind: .recording, date: startDate)`, where `startDate` is the moment the
  selection completed, immediately before `start()`. `SCRecordingOutput` writes to that URL.
  Justification:
  - **Folder errors surface before recording starts** (§7.3 "failure to start"). The user never
    records minutes of video and then loses it to an unwritable folder at the end, which would
    happen if we wrote to a temp file and moved it at stop time.
  - **No cross-volume copy at stop.** A temp file in `NSTemporaryDirectory()` may sit on another
    volume than a user-chosen save folder, which would turn the "move" into a slow copy before the
    reveal.
  - **Collisions:** the name is reserved by the file itself as soon as the writer creates it, so a
    concurrent save can't take the same name. ⌘⇧4/⌘⇧6 are ignored while recording anyway. The
    only window is the moment between `uniqueFileURL` and the writer creating the file, which is
    negligible.
  - **Cancel** deletes the file at that URL (`.deleteFile`), and so does a failed start. Trade-off:
    a crash mid-recording leaves a partial `.mp4` in the save folder. That is acceptable, and the
    user can delete it.
  - If `SaveFolder` throws `SaveFolderError`, recording does not start. The coordinator shows the
    0008 "Couldn't save to <folder>" alert with **Choose Folder…** (§9) and returns `AppMode` to
    `.idle`.

- **`Snapper/App/AppCoordinator.swift`** (extend):
  - On `SelectionResult` with purpose `.record`:
    - `.rect(display, rect)` ⇒ build the configuration from `(display, rect)`.
    - `.window(...)` ⇒ **interim behavior:** take the window's frame (converted to AppKit global
      points via `DisplayGeometry` if 0009 stores it top-left), clamp it to the window's display,
      and record it **as a fixed rectangle on that display**, using the same path as `.rect`. It
      does not follow the window, and covering windows are not excluded. **0013 replaces this** with
      a true window filter and window tracking. Mark it with a `// 0013:` comment.
  - Order: the overlay is already dismissed by 0007 before the result is delivered (so it is never
    captured). Resolve the save folder and URL ⇒ create the `RecordingSession` ⇒ enter `.recording`
    ⇒ swap the status item icon ⇒ `start()`.
  - On `.ended`, release the session, feed `recordingEnded` to `AppModeMachine`, and restore the
    status item.
  - Permission: the 0006 gate already routes a shortcut to onboarding when permission is missing
    before the overlay opens. The `.permissionDenied` start failure covers permission revoked
    between selection and start.

- **`Snapper/App/StatusItemController.swift`** (extend):
  - `setRecording(_ isRecording: Bool)`. When `true`: image `NSImage(systemSymbolName:
    "record.circle", accessibilityDescription: "Stop Recording")` with `isTemplate = false` and a
    red tint (`NSImage.SymbolConfiguration(paletteColors: [.systemRed])`, or
    `button.contentTintColor = .systemRed`). Detach any menu so it doesn't open. Route left **and**
    right mouse-up (`button.sendAction(on: [.leftMouseUp, .rightMouseUp])`) to `onStopClicked`
    (⇒ `AppModeMachine` ⇒ `stopRecording`). Set the tooltip to "Stop Recording". When `false`:
    restore the template `camera.viewfinder` icon and 0001's left-menu / right-Settings
    behavior.

- **`project.yml`**: no new targets or Info.plist keys. Microphone usage text is 0014. New files
  are picked up by the source globs, so run `xcodegen generate`.

- **PRD:** user-approved deferred termination and always-alert write failures are recorded in §7. Implement the approved decisions below, including a test that a non-empty partial file is both revealed and accompanied by an error alert.

## Acceptance criteria

- [ ] ⌘⇧5, then a rectangle drag: recording starts immediately (no countdown), and the menu bar icon
      turns into a red `record.circle`.
- [ ] While recording, ⌘⇧4 and ⌘⇧6 do nothing, and Esc does nothing.
- [ ] Pressing ⌘⇧5 again stops the recording, writes `Recording YYYY-MM-DD at HH.MM.SS.mp4` (the
      start time) to the save folder, and Finder opens with that file selected. The clipboard is
      unchanged.
- [ ] Clicking the menu bar icon (left or right) while recording does the same as ⌘⇧5. No menu or
      Settings window opens.
- [ ] After stopping, the icon returns to `camera.viewfinder`, and left-click opens the menu again.
- [ ] The file is H.264 in an `.mp4` container at 30 fps. Its pixel dimensions equal the selection
      size × the display's backing scale, rounded down to even numbers. The cursor is visible.
- [ ] No Snapper window appears in the video.
- [ ] A second recording started in the same second gets a ` (2)` suffix.
- [ ] Disconnecting the recorded display mid-recording stops and saves the file (with a Finder
      reveal).
- [ ] An unwritable or uncreatable save folder shows "Couldn't save to <folder>" with **Choose
      Folder…**, and no recording starts.
- [ ] Missing permission at start opens the onboarding window. Any other start or write failure shows
      a short alert with the error, and the app returns to idle (normal icon and menu).
- [ ] A `.window` selection with ⌘⇧5 records the window's current frame as a fixed rectangle
      (interim, replaced by 0013).

## Edge cases

- **Retina vs non-Retina:** at 2×, integer-point selections always give even pixel sizes. At 1×,
  odd point sizes (for example 101 pt) drop to 100 px / 100 pt.
- **Fractional points** from the drag (for example 100.5 pt at 2× = 201 px ⇒ 200 px): the origin is
  snapped to the pixel grid, and `sourceRect` is derived from the snapped pixel values.
- **Negative-origin displays** (left of or below the primary display): `sourceRect` is
  display-local and non-negative. The test covers a display at `x: -1920` and one below the primary
  display at a negative `y`.
- **Full-display selection** (for example 1512×982 pt at 2× = 3024×1964 px) stays exact. A
  display whose pixel dimension is odd loses at most one pixel row or column.
- **Minimum size:** the overlay enforces 4×4 pt (§5), so ≥ 4 px. The builder still clamps to ≥ 2
  px.
- **Stop pressed while still starting** (very fast double ⌘⇧5): the stop is deferred until capture
  starts, then applied (lifecycle `pendingStop`).
- **Double stop** (⌘⇧5 plus an icon click, or a display disconnect plus a stream error) is
  ignored after the first.
- **Stream error** (display sleep, disconnect, GPU reset): wait for the output callbacks or the 5 s
  timeout. A non-empty file is revealed. An empty file is deleted and an alert is shown.
- **Our app missing from `SCShareableContent.applications`:** fall back to excluding nothing (see
  the glue). 0012 must handle the filter once the pill exists.
- **Save-folder setting changed mid-recording:** has no effect, because the URL was fixed at start.
- **Permission revoked mid-recording:** surfaces as a stream error ⇒ the same handling as above.

## Test plan

- **Unit tests** — `SnapperTests/RecordingConfigurationBuilderTests.swift`:
  1. Primary display `frame (0,0,1512,982)`, scale 2, selection `(100,100,400,300)` ⇒
     `sourceRect == (100, 982-400=582, 400, 300)`, `pixelWidth == 800`, `pixelHeight == 600`.
  2. Same display, scale 1, selection `(10,10,101,51)` ⇒ `pixelWidth == 100`, `pixelHeight == 50`,
     `sourceRect.size == (100, 50)`, and both are even.
  3. Scale 2, fractional selection `(10.25, 20.5, 100.5, 50.25)` ⇒ both pixel sizes are even,
     `sourceRect.origin * 2` is integral, and `sourceRect.width * 2 == pixelWidth`.
  4. Negative-origin display left of the primary display: `frame (-1920, 0, 1920, 1080)`, scale 1,
     selection `(-1900, 100, 200, 100)` ⇒ `sourceRect == (20, 1080-200=880, 200, 100)`.
  5. Display below the primary display with negative y: `frame (0, -1080, 1920, 1080)`, scale 1,
     selection `(0, -1080, 1920, 1080)` ⇒ `sourceRect == (0, 0, 1920, 1080)`, pixels `1920×1080`.
  6. Selection overhanging the display edge ⇒ clamped and fully inside the display bounds.
  7. Full display at 2× ⇒ the pixel size equals the display's pixel size, and `sourceRect` equals
     the display bounds.
  8. Constants: `minimumFrameInterval == CMTime(value: 1, timescale: 30)`, `showsCursor`,
     `pixelFormat == '420v'`, `queueDepth == 5`, `displayID` passed through. Codec and file type
     are asserted too if the builder exposes them.
- **Unit tests** — `SnapperTests/RecordingLifecycleTests.swift`:
  1. Happy path: `start` ⇒ `[.startCapture]`, `captureStarted` ⇒ `recording`, `stopRequested` ⇒
     `[.stopCapture, .scheduleStopTimeout]`, `outputFinished` ⇒ `finished(url)` with
     `[.reveal(url), .ended]`.
  2. Double stop: a second `stopRequested` in `stopping` ⇒ `[]`, and the phase is unchanged.
  3. Stop during starting ⇒ `[]`. Then `captureStarted` ⇒ `stopping` with `[.stopCapture,
     .scheduleStopTimeout]`.
  4. Cancel during recording ⇒ `outputFinished` ⇒ `cancelled`, `[.deleteFile(url), .ended]`, and
     no `.reveal`.
  5. Cancel and stop both pending during starting ⇒ cancel wins.
  6. Start failure `.permissionDenied` ⇒ `[.deleteFile, .showOnboarding, .ended]`. `.other` ⇒
     `[.deleteFile, .showError, .ended]`.
  7. `streamStopped` while recording ⇒ `stopping`, `[.scheduleStopTimeout]` (no second
     `.stopCapture`). Then `outputFinished` ⇒ reveal.
  8. `stopTimedOut(fileHasContent: true)` ⇒ `finished`, reveal and show the timeout error alert. `false` ⇒ `failed`,
     `[.deleteFile, .showError, .ended]`.
  9. `outputFailed` while recording ⇒ `.stopCapture` first, then a resolution based on file content.
  10. Late callbacks in terminal phases (`outputFinished` after `finished`, `stopRequested` after
      `cancelled`) ⇒ `[]`.
  11. `isRecordedDisplayGone`: `(2, [1,2]) == false`, `(2, [1]) == true`, `(2, []) == true`.
- **Unit tests** — `SnapperTests/AppModeMachineTests.swift` (extend 0005's file): selecting(.record)
  plus a completed selection ⇒ `.recording` with `startRecording`; recording plus the record
  shortcut ⇒ `stopRecording`; recording plus the screenshot or annotate shortcut ⇒ no action and
  mode unchanged; recording plus a status-item click ⇒ `stopRecording`; recording plus
  `recordingEnded` ⇒ `.idle`.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug
  build` and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (needs a real GUI session with Screen Recording granted; can't be
  automated):
  1. Press ⌘⇧5 and drag a rectangle on a Retina display. Recording starts with no countdown, and
     the menu bar icon is a red `record.circle`.
  2. While recording, press ⌘⇧4, ⌘⇧6, and Esc. Nothing happens (no overlay, recording continues).
  3. Move the mouse inside the area for about 5 s, then press ⌘⇧5. Finder opens the save folder
     with `Recording <start time>.mp4` selected, and the name matches the time you finished the
     drag. The clipboard still holds its previous content.
  4. Play the file in QuickTime. The cursor is visible, the content matches the area, and no
     Snapper overlay is visible. Window ▸ Show Movie Inspector shows H.264 and pixel dimensions =
     2× the W × H readout (even). Or run `ffprobe -v error -select_streams v:0 -show_entries
     stream=codec_name,width,height,r_frame_rate <file>` ⇒ `h264`, even width/height,
     `30/1` (or ≤ 30 fps average for static content).
  5. Start another recording. Left-click the menu bar icon: the recording stops, Finder reveals it,
     and no menu opens. Repeat with a right-click: the recording stops, and Settings does **not**
     open.
  6. After stopping, the icon is `camera.viewfinder` again and left-click opens the menu.
  7. On a non-Retina (1×) external display, record a rectangle with odd point dimensions. ffprobe
     shows even width and height.
  8. Two recordings within the same second (start, stop, start immediately): the second file gets
     ` (2)`.
  9. Multi-display: record on the secondary display and unplug it mid-recording. The recording
     stops, the file is saved and revealed, and the icon returns to normal.
  10. Set the save folder to a read-only folder in Settings (or `chmod a-w` it) and press ⌘⇧5 plus a
      drag. The "Couldn't save to <folder>" alert with **Choose Folder…** appears, and no recording
      starts (the icon stays normal).
  11. Press ⌘⇧5, press Space, and click a window. The window's frame area is recorded as a fixed
      rectangle (interim; following arrives in 0013).

## Open questions

None. User confirmed on 2026-11-03: Quitting stops and saves an active recording before exit, using deferred termination. Write failures always show an error alert; preserve and reveal non-empty partial files and delete empty files. This overrides the earlier no-alert partial-file proposal. Recorded in docs/PRD.md.

## Out of scope

- Recording pill (elapsed time · Stop · Cancel), dashed border, and the Cancel button UI: **0012**
  (the `cancel()` API exists here).
- Window recording that follows the window, excludes covering windows, and stops when the window
  closes: **0013**. This spec only records the window's frame as a fixed rectangle.
- Microphone audio and the `recordMicrophone` setting: **0014**.
- System audio, GIF, and highlighted clicks: PRD §2 non-goals.
- Copying recordings to the clipboard (§7.5: never).
