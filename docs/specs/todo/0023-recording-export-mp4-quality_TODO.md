# 0023 — Add an MP4 quality setting with a post-Stop re-encode step

> Source: user request ("let's add the ability to select the compression level of the mp4 file"); docs/PRD.md §7 items 2, 5, 6 ("Format and quality"), §10 Recording, §12 ("MP4 Medium and Low produce smaller files…")
> Depends on: 0011-recording-core, 0013-window-recording, 0014-microphone-audio (all done). 0024-recording-gif-format builds on this spec.

## Context

Today every recording is written directly into the save folder by `SCRecordingOutput` as H.264 `.mp4`:

- `AppCoordinator.startRecording` reserves the final URL with `folder.uniqueFileURL(for: .recording, date: startDate)` (`Snapper/App/AppCoordinator.swift:104-106`) and passes it to `RecordingSession`.
- `RecordingSession.startCapture` points `SCRecordingOutputConfiguration.outputURL` at that URL (`Snapper/Recording/RecordingSession.swift:174-177`). `SCRecordingOutputConfiguration` exposes only codec and file type — **there is no bitrate control**.
- `RecordingLifecycle` (`Snapper/Recording/RecordingLifecycle.swift:28-96`) is the pure state machine. On a normal finish it emits `[.reveal(url), .ended]` (`:71-77`); on write failure with content it reveals the partial file (`:93-94`). The `.ended` effect releases SCK objects and calls `onEnded` (`RecordingSession.swift:114-124`), which returns the coordinator to idle and lets a pending quit proceed (`AppCoordinator.swift:170-176`, `RecordingTerminationDecision.swift`).
- Settings have no recording-quality preference (`Snapper/Settings/AppSettings.swift:6-20`); the Recording tab only has the microphone toggle (`Snapper/Settings/SettingsView.swift:81-89`).

PRD §7 item 6 (added in this change set) defines: *MP4 quality* High (default, file as recorded, no re-encode) / Medium / Low. Medium and Low re-encode after Stop as H.264 at the same pixel size and timing, average bitrate = width × height × 30 × 0.10 (Medium) or × 0.05 (Low) bits/s, microphone audio passed through. While converting: pill and border already gone, menu bar icon stays in its recording state, icon click / ⌘⇧5 do nothing, ⌘⇧4/⌘⇧6 stay ignored, quit waits. Cancel deletes the temp file without converting. On conversion failure: save the original recording to the save folder as `Recording … .mp4`, reveal it, and alert **"Couldn't compress the recording"**. A partial file kept after a write failure is saved the same way, unconverted.

This spec builds the shared "record to temp → convert after Stop" pipeline. 0024 plugs GIF into it.

## User story

As a Snapper user, I can choose High, Medium, or Low MP4 quality in Settings → Recording, and Medium/Low recordings come out noticeably smaller at the same resolution.

## Scope & changes

### Pure logic (unit-tested)

- **New `Snapper/Settings/RecordingFormat.swift`**: `enum RecordingFormat: String, CaseIterable, Sendable { case mp4, gif }` with `title` ("MP4", "GIF") and `fileExtension` ("mp4", "gif"). Only `.mp4` is selectable in the UI in this spec; the `.gif` case exists so settings storage and naming are final (0024 adds the picker and encoder).
- **New `Snapper/Settings/RecordingQuality.swift`**: `enum RecordingQuality: String, CaseIterable, Sendable { case high, medium, low }` with `title` ("High", "Medium", "Low").
- **`AppSettings`** (`Snapper/Settings/AppSettings.swift`): add keys `recordingFormat`, `mp4Quality`, `gifQuality`; defaults `.mp4`, `.high`, `.medium`; stored with the existing `decodeEnum` pattern (unknown raw values fall back to the default).
- **`CaptureKind`** (`Snapper/Support/CaptureKind.swift`): change to `case screenshot, recording(RecordingFormat)`; `fileExtension` returns the format's extension. Replace `CaseIterable` with an explicit `static let allCases: [CaptureKind] = [.screenshot, .recording(.mp4), .recording(.gif)]` only if something still uses `allCases` (grep; otherwise drop the conformance). Update callers (`AppCoordinator.swift:106`, `FileNamerTests`).
- **New `Snapper/Recording/RecordingOutputPlan.swift`** — maps settings to what happens after capture:
  ```swift
  enum RecordingTranscode: Equatable, Sendable {
      case mp4(averageBitRate: Int)
      // 0024 adds: case gif(GIFEncodingSettings)
  }
  struct RecordingOutputPlan: Equatable, Sendable {
      let format: RecordingFormat
      let transcode: RecordingTranscode?   // nil = SCRecordingOutput writes the final file directly
      var finalExtension: String { format.fileExtension }
      static func make(format: RecordingFormat, mp4Quality: RecordingQuality, gifQuality: RecordingQuality,
                       pixelWidth: Int, pixelHeight: Int, pointPixelScale: CGFloat) -> RecordingOutputPlan
  }
  enum MP4Bitrate {
      static let framesPerSecond = 30
      static func bitsPerPixelPerFrame(_ q: RecordingQuality) -> Double?   // high: nil, medium: 0.10, low: 0.05
      static func averageBitRate(pixelWidth: Int, pixelHeight: Int, quality: RecordingQuality) -> Int?
  }
  ```
  `averageBitRate` = `Int((Double(w * h * 30) * bpp).rounded())`, `nil` for `.high`. In this spec `make(format: .gif, …)` may `preconditionFailure`/fall back to MP4 High — 0024 replaces it; prefer returning the MP4 plan and leaving a `// 0024` comment. `pointPixelScale` is unused until 0024 but is part of the signature now so callers don't change twice.
- **`RecordingLifecycle`** (`Snapper/Recording/RecordingLifecycle.swift`) gains a conversion stage. Keep the existing behavior bit-for-bit when no conversion is requested:
  - `RecordingEvent.start(url: URL, converts: Bool)` (existing call sites pass `converts: false`; `url` is the capture file — temp when converting).
  - New phase `.converting`. New events `.conversionFinished(URL)` and `.conversionFailed(RecordingFailure, savedOriginal: URL?)`. New effects `.releaseCapture` (tear down SCK, stop the timeout, remove the screen observer — everything `.ended` does today *except* `onEnded`), `.convert(URL)`, `.saveOriginal(URL)` (move a capture file into the save folder as MP4, then reveal it).
  - When `converts == true`:
    - Normal finish (`(.recording, .outputFinished)` and `(.stopping, .outputFinished)` without cancel/stream failure) → phase `.converting`, effects `[.stopCapture?, .releaseCapture, .convert(url)]` (keep `.stopCapture` exactly where today's branch has it).
    - `(.converting, .conversionFinished(final))` → `.finished(final)`, `[.deleteFile(url), .reveal(final), .ended]`.
    - `(.converting, .conversionFailed(f, savedOriginal: u))` → if `u != nil`: `.finished(u)`, `[.reveal(u), .showError(f), .ended]`; else `.failed(f)`, `[.deleteFile(url), .showError(f), .ended]`.
    - `resolveFailure` with `fileHasContent == true` → `[.saveOriginal(url), .showError(failure), .ended]` instead of revealing the temp file. Cancel paths still `.deleteFile(url)`.
    - `stopRequested`, `cancelRequested`, `stopTimedOut`, and stream/output events during `.converting` are ignored (return `[]`).
  - Terminal-phase guard stays: `.finished/.failed/.cancelled` ignore everything.
  - `RecordingFailure` gains a `title` used by the alert: default "Couldn't record the screen"; add `static func compression(_ message: String)` with title "Couldn't compress the recording". `RecordingSession` uses `failure.title` instead of the hard-coded string at `RecordingSession.swift:112`. (0024 adds the GIF title.)

### Platform glue (manually verified)

- **New `Snapper/Recording/MP4Transcoder.swift`**: `nonisolated` async function `transcode(input: URL, output: URL, averageBitRate: Int) async throws`. Uses `AVAssetReader` + `AVAssetWriter` (AVFoundation, system framework — no new dependency):
  - Video: read decoded frames (`kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange`), write H.264 with `AVVideoWidthKey/HeightKey` = the source track's natural size, `AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: bitrate, AVVideoExpectedSourceFrameRateKey: 30, AVVideoProfileLevelKey: H264 High AutoLevel]`. Append samples with their original timestamps (SCK output is variable-frame-rate; don't retime).
  - Audio (if a track exists): passthrough (`outputSettings: nil`).
  - Output type `.mp4`. Writes to a temp file first, then the caller moves it into place.
  - Runs off the main actor; must not touch UI.
- **`RecordingSession`** (`Snapper/Recording/RecordingSession.swift`):
  - `init` takes a `RecordingOutputPlan` and `finalURL` (the reserved save-folder URL) instead of only `outputURL`. When `plan.transcode == nil`, capture URL = `finalURL` (today's behavior). Otherwise capture URL = `FileManager.default.temporaryDirectory/Snapper-<UUID>.mp4`.
  - `start()` sends `.start(url: captureURL, converts: plan.transcode != nil)`.
  - Split today's `.ended` handling into `.releaseCapture` (lines 115-123 minus `onEnded()`) and `.ended` (calls `.releaseCapture` work if not done, then `onEnded()`).
  - `.convert(url)`: `Task.detached` the transcoder writing to `<temp>-out.mp4`; on success move to the final URL (if `finalURL` now exists, re-resolve with `SaveFolder.uniqueFileURL(for: .recording(plan.format), date: startDate)`), then hop to `@MainActor` and `send(.conversionFinished(final))`. On failure: try `.saveOriginal` logic (move the capture file to a unique `.recording(.mp4)` URL in the save folder); send `.conversionFailed(.compression(error.localizedDescription), savedOriginal: movedURL or nil)`. Delete any partial `-out` file.
  - `.saveOriginal(url)`: move to a unique `.recording(.mp4)` URL in the save folder and reveal it; if the move fails, reveal nothing and log.
  - Settings snapshot (format and both qualities) is read once at start, like the microphone inputs (`RecordingSession.swift:148-152`).
  - The session needs `startDate` for re-resolving names; pass it in.
- **`AppCoordinator.startRecording`** (`Snapper/App/AppCoordinator.swift:81-135`): build the plan with `RecordingOutputPlan.make(...)` from `settings` and the source's pixel size and scale (rectangle: `RecordingConfiguration.pixelWidth/Height`, scale = `pixelWidth / sourceRect.width`; window: pixel size isn't known until `startCapture` — pass the plan inputs into the session and let it call `make` after `buildWindow`, so `averageBitRate` uses the real window pixel size). Reserve `finalURL` with `.recording(plan.format)`. No change to the overlay/stop/cancel wiring: `onStopping` already dismisses the pill; `onEnded` only fires after conversion, so the status icon stays red, `AppModeMachine` stays in `.recording` (shortcuts ignored), and `RecordingTerminationDecision` makes quit wait.
  - Icon click / ⌘⇧5 during conversion call `stopRecording()`, which sends `.stopRequested` → ignored by the lifecycle. Verify nothing else happens (no second reveal).
- **Settings UI**: `SettingsModel` gets `mp4Quality` get/set. `SettingsView.recording` adds, above the microphone toggle, `Picker("MP4 quality", selection: $model.mp4Quality)` with High / Medium / Low (`.radioGroup` or menu style, matching General's destination picker) and a one-line secondary caption: "Medium and Low re-encode after you stop, for smaller files."

### Files / project

- New files under `Snapper/Settings/` and `Snapper/Recording/` and new tests → run `xcodegen generate`.
- docs/PRD.md updated in this change set: §2 (GIF no longer a non-goal), §7 items 2 and 6, §9, §10 Recording, §12. No further PRD change needed by this spec.

## Acceptance criteria

- [ ] Settings → Recording shows **MP4 quality** with High / Medium / Low; default High; the choice persists across relaunches.
- [ ] With High, recording behaves exactly as before (file written directly to the save folder, no conversion step, same reveal).
- [ ] With Medium or Low, Stop produces `Recording <start> .mp4` in the save folder at the same pixel dimensions and duration, smaller than a High recording of the same content; Low is smaller than Medium.
- [ ] Microphone audio, when recorded, survives the re-encode in sync.
- [ ] While converting: pill and border are gone, the icon stays red, clicking it or ⌘⇧5 does nothing, ⌘⇧4/⌘⇧6 are ignored; when done, Finder reveals the file and the icon returns to normal.
- [ ] Quitting during conversion waits for it, reveals the file, then quits.
- [ ] Cancel with Medium/Low leaves no file in the save folder or the temp directory.
- [ ] A conversion failure saves the original MP4 in the save folder, reveals it, and shows "Couldn't compress the recording" with the error.
- [ ] No temp files remain after success, failure, or cancel.

## Edge cases

- Window recordings: bitrate must use the window's fixed output pixel size from `buildWindow`, not the AppKit frame.
- Very short recordings (a single frame, or zero video frames because nothing changed): the transcoder must still finish or fail cleanly; a zero-frame source counts as a conversion failure → original saved.
- Variable frame rate: static screens give few frames; keep source timestamps so duration is preserved.
- Name collision appearing between start and conversion end (another file created in the folder): re-resolve with a ` (2)` suffix.
- Save folder deleted or made unwritable during conversion: the final move fails → treat as conversion failure; `saveOriginal` will also fail → `.failed`, alert shown, temp deleted. (Don't crash; don't leave temp files.)
- Write failure mid-recording with content (PRD §7 item 3) when converting: the partial temp file is moved to the save folder as MP4 and revealed, alert shown, no conversion attempted.
- Stop timeout (`RecordingLifecycle.stopTimeout`) applies only to SCK finalization, not to conversion; conversion has no timeout.
- Settings changed while recording or converting: no effect on the current recording (snapshot at start).
- Display disconnect during conversion: irrelevant (capture already released); must not trigger a second stop.

## Test plan

- **Unit tests**
  - `SnapperTests/AppSettingsTests.swift`: defaults on an empty suite are `.mp4`, `.high`, `.medium`; round-trip for `recordingFormat`, `mp4Quality`, `gifQuality`; an unknown stored raw value (`"ultra"`) falls back to the default; `init` writes nothing (extend existing `initWritesNothing`).
  - `SnapperTests/RecordingOutputPlanTests.swift` (new):
    - `MP4Bitrate.averageBitRate(2880, 1800, .high) == nil`; `.medium == 15_552_000`; `.low == 7_776_000`; `1920×1080 .medium == 6_220_800`.
    - `make(format: .mp4, mp4Quality: .high, …)` → `transcode == nil`, `finalExtension == "mp4"`.
    - `make(format: .mp4, mp4Quality: .medium, pixelWidth: 1000, pixelHeight: 500, …)` → `.mp4(averageBitRate: 1_500_000)`.
    - `gifQuality` has no effect on an MP4 plan.
  - `SnapperTests/RecordingLifecycleTests.swift`: all existing tests still pass with `converts: false`. New cases with `converts: true`:
    - stop → outputFinished → phase `.converting`, effects contain `.releaseCapture` and `.convert(tmp)`, **not** `.reveal` or `.ended`.
    - spontaneous outputFinished while recording → `.stopCapture`, `.releaseCapture`, `.convert(tmp)`.
    - converting + `conversionFinished(final)` → `.finished(final)`, `[.deleteFile(tmp), .reveal(final), .ended]`.
    - converting + `conversionFailed(f, savedOriginal: saved)` → `.finished(saved)`, `[.reveal(saved), .showError(f), .ended]`.
    - converting + `conversionFailed(f, savedOriginal: nil)` → `.failed(f)`, `[.deleteFile(tmp), .showError(f), .ended]`.
    - `stopRequested`, `cancelRequested`, `stopTimedOut` during `.converting` → `[]`, phase unchanged.
    - cancel → outputFinished → `.cancelled`, `[.deleteFile(tmp), .ended]`, no `.convert`.
    - outputFailed with content → `[.saveOriginal(tmp), .showError, .ended]`; without content → `[.deleteFile(tmp), .showError, .ended]`.
    - window stream failure then outputFinished → behaves like today's failure path but with `.saveOriginal(tmp)` instead of `.reveal(tmp)`.
  - `SnapperTests/FileNamerTests.swift`: update `.recording` → `.recording(.mp4)`; add `.recording(.gif)` → `"Recording 2026-11-02 at 19.18.04.gif"` and a collision case `… (2).gif`.
  - `SnapperTests/RecordingFailureTests` (or inside lifecycle tests): default `title` is "Couldn't record the screen"; `.compression("x").title == "Couldn't compress the recording"`.
  - `SnapperTests/MP4TranscoderTests.swift` (headless, no TCC needed): synthesize a short H.264 MP4 with `AVAssetWriter` (e.g. 320×240, 30 frames of solid colors with VFR timestamps), transcode at a low bitrate, assert the output exists, has one video track with natural size 320×240, duration within one frame of the source, and the temp `-out` file is gone after a move. A second case: a non-media input file → throws.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (real GUI session, Screen Recording permission granted):
  1. Settings → Recording: MP4 quality shows High selected. Record a 10 s rectangle of a busy area (e.g. scrolling a web page), Stop → file revealed immediately, as before. Note its size.
  2. Set Medium, repeat the same recording → after a short pause Finder reveals a smaller `.mp4`; QuickTime → Inspector shows the same dimensions as step 1.
  3. Set Low, repeat → smaller than Medium.
  4. Turn on Record microphone, Low, talk while recording → audio plays back in sync.
  5. Low, record ~60 s of a full Retina display, Stop, then immediately press ⌘⇧4 and ⌘⇧5 and click the menu bar icon → nothing happens; icon is red until the file is revealed, then returns to the camera icon.
  6. Low, start a long recording, Stop, then immediately ⌘Q → Snapper stays until the file is revealed, then quits.
  7. Low, record, press Cancel → no new file in the save folder; `ls $TMPDIR | grep Snapper-` shows nothing.
  8. Window recording (Space to window mode) with Medium → output dimensions match a High window recording of the same window.
  9. Relaunch Snapper → MP4 quality setting kept.

## Out of scope

- GIF format, GIF quality picker, microphone gating for GIF, and the GIF encoder — **0024-recording-gif-format**.
- Changing capture resolution, frame rate, codec (HEVC), or progress UI for the conversion.
- A cancel button for an in-progress conversion.
