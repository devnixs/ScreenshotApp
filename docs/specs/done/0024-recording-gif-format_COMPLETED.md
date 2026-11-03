# 0024 — Record to GIF with a GIF quality setting

> Source: user request ("add a setting so screen recording does gif instead of mp4 … select the compression level of the … gif file"); docs/PRD.md §2 (GIF removed from non-goals), §7 items 2 and 6, §9, §10 Recording, §12 ("With the GIF format, ⌘⇧5 saves a looping `.gif`…")
> Depends on: 0023-recording-export-mp4-quality (record-to-temp → convert pipeline, `RecordingFormat`, `RecordingQuality`, settings keys, `CaptureKind.recording(_:)`, `RecordingOutputPlan`).

## Context

After 0023:

- `AppSettings` already stores `recordingFormat` (default `.mp4`) and `gifQuality` (default `.medium`), but the UI only exposes *MP4 quality*.
- `RecordingOutputPlan.make` returns an MP4 plan even for `.gif` (placeholder marked `// 0024`); `RecordingTranscode` has only `.mp4(averageBitRate:)`.
- `RecordingLifecycle` handles `converts: true` with `.convert(url)` / `.conversionFinished` / `.conversionFailed(savedOriginal:)`; `RecordingSession` runs `MP4Transcoder` for `.convert` and saves the original MP4 on failure.
- `MicrophoneCapturePolicy.shouldCaptureMicrophone` (`Snapper/Recording/MicrophoneCapturePolicy.swift`) knows nothing about format.

PRD §7 item 6, GIF quality:
- *High*: 15 fps at native pixels. *Medium* (default): 10 fps at 1× (pixel size ÷ the capture's backing scale). *Low*: 8 fps at 1×, then scaled so the longest side is ≤ 640 px. Keep aspect ratio, round to whole pixels (min 1 × 1), never upscale. Loops forever; frame delays from real frame timing.
- Failure → original saved as `Recording … .mp4`, revealed, alert **"Couldn't create the GIF"**.
- §7 item 2: GIF recordings never capture the microphone. §10: with GIF, the *Record microphone* toggle is disabled (value kept) with the note "GIFs don't include sound."
- §9: GIF files are named `Recording <date> at <time>.gif`.

Capture itself is unchanged: SCK records H.264 MP4 at native resolution, 30 fps, into a temp file; the GIF is produced from it after Stop.

## User story

As a Snapper user, I can switch the recording format to GIF in Settings and pick High/Medium/Low, and ⌘⇧5 then saves a looping `.gif` I can drop straight into a chat or a PR.

## Scope & changes

### Pure logic (unit-tested)

- **New `Snapper/Recording/GIFEncodingSettings.swift`**:
  ```swift
  struct GIFEncodingSettings: Equatable, Sendable {
      let framesPerSecond: Int        // 15 / 10 / 8
      let usesPointSize: Bool         // false / true / true
      let maxLongestSide: Int?        // nil / nil / 640
      static func forQuality(_ q: RecordingQuality) -> GIFEncodingSettings
      func outputPixelSize(sourceWidth: Int, sourceHeight: Int, pointPixelScale: CGFloat) -> (width: Int, height: Int)
  }
  ```
  `outputPixelSize`: start from source pixels; if `usesPointSize` and scale > 1, divide both by scale and round; then if `maxLongestSide` and the longest side exceeds it, multiply both by `max / longest` and round; clamp each to `1...source`. Never upscale (scale ≤ 1 leaves pixels untouched).
- **New `Snapper/Recording/GIFFrameSampler.swift`**:
  ```swift
  struct GIFFrame: Equatable, Sendable { let sourceIndex: Int; let delayCentiseconds: Int }
  enum GIFFrameSampler {
      static func plan(timestamps: [Double], duration: Double, framesPerSecond: Int) -> [GIFFrame]
  }
  ```
  - `timestamps` are source frame presentation times in seconds (ascending; SCK output is variable-frame-rate, so gaps are normal). `duration` is the asset duration.
  - Selection: with interval `i = 1 / fps`, a frame is selected when its time ≥ the next slot; after selecting at time `t`, the next slot is `(floor(t / i) + 1) * i`. The first frame is always selected.
  - Delays: each selected frame lasts until the next selected frame; the last one lasts until `max(duration, lastTime + i)`. Convert with **cumulative rounding** so the total matches the real duration: `delay_k = round(100 * end_k) − round(100 * start_k)`, with times relative to the first selected frame. Clamp each delay to ≥ 2 cs (browsers treat smaller values as 10 cs).
  - Empty `timestamps` → `[]` (the encoder treats this as a failure).
- **`RecordingOutputPlan`** (`Snapper/Recording/RecordingOutputPlan.swift`): add `case gif(GIFEncodingSettings, width: Int, height: Int)` to `RecordingTranscode` (the computed output size). `make(format: .gif, …)` returns `.gif(.forQuality(gifQuality), width/height from outputPixelSize(pixelWidth, pixelHeight, pointPixelScale))`; `mp4Quality` is ignored for GIF. Remove the 0023 placeholder.
- **`MicrophoneCapturePolicy`**: add `format: RecordingFormat` to `MicrophoneCaptureInputs`; `shouldCaptureMicrophone` returns `false` for `.gif` regardless of setting/permission/device.
- **`RecordingFailure`**: add `static func gifConversion(_ message: String)` with title "Couldn't create the GIF". `RecordingSession` picks `.gifConversion` vs `.compression` from the plan.
- **New `Snapper/Settings/RecordingSettingsPresentation.swift`** (pure, keeps `SettingsView` dumb):
  - `static func microphoneToggleEnabled(format:) -> Bool` (`.mp4` → true, `.gif` → false)
  - `static func microphoneNote(format:) -> String?` (`.gif` → "GIFs don't include sound.", `.mp4` → nil)
  - `static func qualityCaption(format:) -> String` — MP4: "Medium and Low re-encode after you stop, for smaller files." GIF: "High: 15 fps, full size. Medium: 10 fps, 1×. Low: 8 fps, up to 640 px."

### Platform glue (manually verified)

- **New `Snapper/Recording/GIFEncoder.swift`**: `nonisolated` async `encode(input: URL, output: URL, settings: GIFEncodingSettings, width: Int, height: Int) async throws`.
  - Pass 1: `AVAssetReader` on the video track with `outputSettings: nil` (no decoding) to collect presentation timestamps; read `asset.load(.duration)`. Run `GIFFrameSampler.plan`. Empty plan → throw.
  - Pass 2: `AVAssetReader` decoding to `kCVPixelFormatType_32BGRA`; for each selected `sourceIndex`, draw the frame into a `width × height` sRGB `CGContext` with `.high` interpolation and add it to a `CGImageDestination` (`UTType.gif`, ImageIO — system framework, no new dependency) with `kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: cs/100, kCGImagePropertyGIFUnclampedDelayTime: cs/100]`.
  - File properties: `kCGImagePropertyGIFLoopCount: 0` (loop forever). `CGImageDestinationFinalize` failure → throw.
  - Release each frame's image after adding (autoreleasepool per frame) to bound memory on long recordings.
  - Off the main actor; no UI.
- **`RecordingSession`** `.convert(url)`: switch on `plan.transcode` — `.mp4` → `MP4Transcoder` (0023), `.gif` → `GIFEncoder` writing to `<temp>-out.gif`, then move to the final `.gif` URL (re-resolve with `.recording(.gif)` on collision). Failure path unchanged from 0023 (save original as `.recording(.mp4)`), with the GIF failure title.
- **Window recordings**: `RecordingOutputPlan.make` is called after `buildWindow` (0023), passing `filter.pointPixelScale`, so Medium/Low GIF sizes use the real window scale. Rectangle recordings pass the display's backing scale.
- **`RecordingSession.startCapture`**: build `MicrophoneCaptureInputs` with the snapshotted format (`RecordingSession.swift:148-152`).
- **Settings UI** (`Snapper/Settings/SettingsModel.swift`, `SettingsView.swift`):
  - `SettingsModel` gets `recordingFormat` and a `recordingQuality` computed property that reads/writes `mp4Quality` or `gifQuality` depending on `recordingFormat`.
  - Recording tab, top to bottom: `Picker("Format", …)` MP4 / GIF (`.segmented`); `Picker("Quality", selection: $model.recordingQuality)` High / Medium / Low with the `qualityCaption` below in secondary style (this replaces 0023's "MP4 quality" picker); the microphone toggle with `.disabled(!microphoneToggleEnabled)` and the `microphoneNote` below it. The toggle's stored value is never changed by switching formats. The existing denied-permission note still shows when applicable.

### Files / project

- New Swift files and tests → `xcodegen generate`.
- docs/PRD.md already updated for this feature in the 0023/0024 change set; no further PRD change.

## Acceptance criteria

- [ ] Settings → Recording shows Format (MP4 / GIF) and Quality (High / Medium / Low). Quality shows the selected format's own level; switching formats switches the shown level; defaults MP4 High, GIF Medium; persisted across relaunches.
- [ ] With GIF, ⌘⇧5 → Stop saves `Recording <start>.gif` in the save folder, reveals it in Finder, and it loops forever in Safari/Quick Look.
- [ ] GIF High is native pixels at ~15 fps; Medium is 1× at ~10 fps; Low is 1× capped at 640 px longest side at ~8 fps. Playback duration matches the recording within ~0.1 s.
- [ ] The GIF contains neither the pill nor the border.
- [ ] With GIF, the microphone toggle is disabled (keeping its value) with "GIFs don't include sound.", and no microphone is captured (no mic indicator in the menu bar during recording).
- [ ] During GIF conversion, the conversion behavior from 0023 applies (icon red, clicks/shortcuts ignored, quit waits, Cancel leaves nothing).
- [ ] A GIF conversion failure saves the original `.mp4`, reveals it, and shows "Couldn't create the GIF".

## Edge cases

- Static screen: SCK may deliver only one or two frames for the whole recording → a GIF with few frames and long delays, correct total duration.
- Single-frame recording → one frame lasting `max(duration, 1/fps)`.
- Zero video frames → conversion failure → original MP4 saved (or nothing, if the temp file is empty) with the GIF alert.
- Tiny selections (e.g. 4×4 pt at 2× → 8×8 px): Medium → 4×4; Low → 4×4 (no upscale); never 0.
- Non-Retina (scale 1): Medium and High produce the same pixel size, only fps differs.
- Portrait shapes for Low: the cap applies to the height (e.g. 800×1600 at 1× → 320×640).
- Mixed-scale displays and window moved between displays: GIF size is computed once from the starting scale; SCK letterboxes inside the fixed capture size (PRD §7 item 2).
- Long recordings at High (full Retina): large files and slow conversion are expected; memory must stay bounded (per-frame autorelease), no timeout.
- Microphone setting on + switching to GIF + back to MP4: the toggle is on again and mic recording resumes for MP4.

## Test plan

- **Unit tests**
  - `SnapperTests/GIFEncodingSettingsTests.swift` (new):
    - `forQuality(.high)` = (15, false, nil); `.medium` = (10, true, nil); `.low` = (8, true, 640).
    - High: 2880×1800 @2 → 2880×1800.
    - Medium: 2880×1800 @2 → 1440×900; 1000×500 @1 → 1000×500; 1001×501 @2 → 501×251 (rounding).
    - Low: 2880×1800 @2 → 640×400; 1000×500 @1 → 640×320; 800×1600 @1 → 320×640; 300×200 @2 → 150×100 (under the cap, untouched).
    - Tiny: 8×8 @2 Low → 4×4; 2×2 @2 Medium → 1×1; never returns 0.
    - Scale < 1 or == 1 never upscales.
  - `SnapperTests/GIFFrameSamplerTests.swift` (new):
    - 30 uniform 30 fps timestamps (0, 1/30, …, 29/30 s), duration 1.0, fps 10 → 10 frames (source indices 0, 3, 6, …, 27), delays all 10, sum 100.
    - Same input, fps 15 → 15 frames (every other source frame), each delay ∈ {6, 7}, sum 100.
    - VFR: timestamps [0, 0.5, 2.0], duration 3.0, fps 10 → 3 frames, delays [50, 150, 100].
    - Burst: timestamps [0, 0.01, 0.02, 0.03, 0.2], duration 0.2, fps 10 → indices [0, 4], delays [20, 10] (the last frame lasts one interval because the duration ends at its timestamp).
    - First timestamp non-zero (e.g. starts at 0.35): delays are relative; first frame selected.
    - Delay clamp: timestamps [0.095, 0.1], duration 0.3, fps 10 → both selected (0.1 hits the next slot); first delay == 2 (clamped from 0–1), second delay == 20 ± 1.
    - Empty → `[]`.
  - `SnapperTests/RecordingOutputPlanTests.swift`: `make(format: .gif, gifQuality: .medium, pixelWidth: 2880, pixelHeight: 1800, pointPixelScale: 2)` → `.gif(.forQuality(.medium), width: 1440, height: 900)`, `finalExtension == "gif"`; `mp4Quality` has no effect on GIF plans.
  - `SnapperTests/MicrophoneCapturePolicyTests.swift`: existing cases get `format: .mp4`; new case: all inputs favorable + `.gif` → false.
  - `SnapperTests/RecordingSettingsPresentationTests.swift` (new): toggle enabled/note/caption for each format, with the exact PRD strings.
  - `RecordingFailure` title: `.gifConversion("x").title == "Couldn't create the GIF"`.
  - `SnapperTests/GIFEncoderTests.swift` (headless): synthesize a 320×240 H.264 MP4 with `AVAssetWriter` (30 frames of alternating colors at 30 fps), encode at Medium-like settings to 160×120 @10 fps → output exists; `CGImageSourceCreateWithURL` reports type `com.compuserve.gif`, 10 frames, each 160×120, loop count 0, delays summing to ~1.0 s. Non-media input → throws.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (real GUI session, Screen Recording permission granted):
  1. Settings → Recording: Format MP4, Quality High. Switch to GIF → Quality shows Medium; the microphone toggle is disabled with "GIFs don't include sound.". Switch back to MP4 → Quality shows High; toggle enabled with its previous value.
  2. GIF Medium on a Retina display: record a 5 s rectangle of ~400×300 pt while moving a window in it, Stop → Finder reveals `Recording … .gif`; Quick Look plays it looping; Preview → Tools → Show Inspector shows 400×300 px (1×), not 800×600. Duration ≈ 5 s.
  3. GIF High, same area → 800×600 px, smoother motion, larger file.
  4. GIF Low, full-screen selection → longest side 640 px.
  5. Window mode (Space) with GIF Medium → the GIF shows only that window, no pill/border.
  6. Record microphone on, format GIF: during recording no orange microphone indicator appears in the menu bar.
  7. GIF High, record ~30 s full screen, Stop, immediately press ⌘⇧4 and click the icon → ignored; icon stays red until Finder reveals the GIF.
  8. GIF, record, Cancel → no file in the save folder; no `Snapper-*` file left in `$TMPDIR`.
  9. Relaunch → Format GIF and per-format quality levels are kept.

## Out of scope

- The temp-file pipeline, MP4 quality, lifecycle conversion stage, and quit/shortcut behavior during conversion — done in **0023**.
- Custom palettes/dithering, frame-rate or size sliders, trimming, GIF-to-clipboard, and progress UI.
- Capturing directly to GIF frames via `SCStreamOutput` (we keep `SCRecordingOutput` per `AGENTS.md`).

## Verification status

Automated verification on 2026-11-03: `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and `xcodebuild -scheme Snapper test` all passed. The full suite passed **405 tests in 73 suites**, including GIF size/sampling/presentation decisions, per-format quality persistence, microphone gating, and four headless GIF encoder tests. The encoder tests check output dimensions, color order, ten frames per second, looping, sparse variable-frame timing, a held single frame, and invalid input. Compressed samples use CoreMedia's output presentation timestamps to account for H.264 reordering offsets; zero-sample drain markers are skipped.

PRD §7, §10, and §12 already specify the implemented behavior. AGENTS.md's testability list now includes GIF sizing, frame sampling, and headless encoding.

GUI behavior has not been verified. With Screen Recording permission granted:

- [ ] not yet run by user: In Settings → Recording, confirm MP4/High defaults. Switch to GIF and confirm Medium and the disabled microphone toggle with “GIFs don't include sound.” Switch back to MP4 and confirm High and the previous microphone value return.
- [ ] not yet run by user: With GIF Medium on Retina, record a five-second rectangle around 400 × 300 points with movement. Confirm Finder reveals a looping GIF around 400 × 300 pixels and five seconds long.
- [ ] not yet run by user: Repeat the same area with GIF High. Confirm 800 × 600 pixels, smoother motion, and a larger file.
- [ ] not yet run by user: Record a full-screen GIF at Low. Confirm the longest output side is 640 pixels.
- [ ] not yet run by user: Record a window using Space and GIF Medium. Confirm only that window appears, with no recording pill or border.
- [ ] not yet run by user: Enable Record microphone, switch to GIF, and record. Confirm no orange microphone indicator appears.
- [ ] not yet run by user: Stop a thirty-second full-screen GIF High recording and immediately try ⌘⇧4 and the menu bar icon. Confirm they are ignored and the icon stays red until Finder reveals the GIF.
- [ ] not yet run by user: Cancel a GIF recording. Confirm no new save-folder file or `Snapper-*` temporary file remains.
- [ ] not yet run by user: Relaunch Snapper. Confirm GIF format and both formats' independent quality levels are retained.

Rebuilding with a different signature can reset Screen Recording permission.
