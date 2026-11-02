# 0014 — Record the microphone into recordings when "Record microphone" is on

> Source: docs/PRD.md §7.2 (lines 93–94), §10 *Recording* (line 177), §11 (line 187), §2 non-goals (line 32: no system audio)
> Depends on: 0003-app-settings, 0010-settings-window, 0011-recording-core, 0013-window-recording

## Context

No app code exists yet. When this spec starts, the lower-numbered specs will have delivered:

- `AppSettings` (0003, `Snapper/Settings/AppSettings.swift`) with the Boolean `recordMicrophone` key, default **off**.
- The Settings window's *Recording* tab (0010, `Snapper/Settings/SettingsView.swift`), whose *Record microphone* toggle requests microphone permission when it is turned on, and switches back off with an **Open System Settings** note when permission is denied (PRD §10, line 177). 0010 also adds `NSMicrophoneUsageDescription` (or 0001 does) because requesting permission without it crashes.
- `RecordingSession` (0011, `Snapper/Recording/RecordingSession.swift`), which drives an `SCStream` with an `SCRecordingOutput` writing `.mp4` / H.264 / 30 fps / cursor visible, and the pure `RecordingConfigurationBuilder` (0011, extended for window filters by 0013) that produces the `SCStreamConfiguration`.

What is missing: recordings never contain audio. PRD §7.2 says: *"Audio: the microphone is recorded only if the Record microphone setting is on (off by default). System audio is never recorded."* PRD §11: *"Microphone permission is requested only when Record microphone is turned on."*

### Research findings (ScreenCaptureKit, macOS 15 SDK)

From the SDK headers (`ScreenCaptureKit.framework/Headers/SCStream.h`):

- `SCStreamConfiguration.captureMicrophone: Bool` (macOS 15.0+): *"specifies whether the microphone audio will be captured. By default microphone is not captured."*
- `SCStreamConfiguration.microphoneCaptureDeviceID: String?` (macOS 15.0+): *"This deviceID is the uniqueID from AVCaptureDevice for the microphone. **System Default Microphone will be used if not specified by client.**"* → Snapper leaves it **`nil`**. The PRD has no microphone picker, so the system default input (as set in System Settings → Sound → Input) is correct, and `nil` makes ScreenCaptureKit resolve it at stream start instead of us snapshotting a `uniqueID` that might be stale.
- `SCStream.addRecordingOutput(_:)`: *"Recording will be written into a file url specified in SCRecordingOutput. **Media (Screen/Audio/Microphone) to be recorded will be based on the SCStream configuration.**"* → `SCRecordingOutput` writes the microphone track by itself once `captureMicrophone = true`. **No `addStreamOutput(_:type: .microphone, …)` is required** for the file to contain mic audio. Do not add one: Snapper does not consume sample buffers.
- `capturesAudio` controls **system/app audio** (`SCStreamOutputType.audio`). It must stay `false` at all times (PRD §7.2, §2).
- `SCRecordingOutputConfiguration` exposes no audio codec setting. The mic track in an `.mp4` is expected to be AAC; the manual checks confirm this with `ffprobe` rather than asserting it in code. (`mixesAudioWithMicrophone` exists only on macOS 27+ and only matters when system audio is also captured, so it is irrelevant here.)
- Risk: an Apple Developer Forums thread ("ScreenCaptureKit recording output is corrupted when captureMicrophone is true", thread 805892, no resolution) reports unplayable files with `SCRecordingOutput` + `captureMicrophone = true`. That reporter also had `capturesAudio` on in some runs, HEVC, and extra `.audio`/`.microphone` stream outputs. Snapper's configuration (H.264, no system audio, no audio stream outputs) differs, but manual check 3 below must verify the file plays. If it does not, stop and report the failure. Don't switch to an `AVAssetWriter` pipeline without asking, because that would replace 0011's architecture.

## User story

As a Snapper user who turned on *Record microphone* in Settings, I get my voice narration in the `.mp4` alongside the screen video. With the setting off, or if I've since revoked microphone permission, recordings are silent video, and no system audio (music, app sounds) is ever captured directly.

## Scope & changes

- **Pure, unit-tested: `Snapper/Recording/MicrophoneCapturePolicy.swift` (new).**
  - `enum MicrophoneAuthorization: Sendable, Equatable { case authorized, denied, restricted, notDetermined }`. This is a Foundation-only mirror of `AVAuthorizationStatus`, so the policy doesn't import AVFoundation. If 0010 already introduced an equivalent pure type for the Settings toggle, reuse it instead of adding a second one, and note the reuse in the commit.
  - `struct MicrophoneCaptureInputs: Sendable, Equatable { var recordMicrophoneSetting: Bool; var authorization: MicrophoneAuthorization; var hasAudioInputDevice: Bool }`.
  - `enum MicrophoneCapturePolicy { static func shouldCaptureMicrophone(_ inputs: MicrophoneCaptureInputs) -> Bool }`. It returns `true` **only** when `recordMicrophoneSetting && authorization == .authorized && hasAudioInputDevice`. Every other combination returns `false`.
  - Never request permission from this path. Per PRD §11, the request only happens when the toggle is turned on (0010). `.notDetermined` therefore means "record video only".
- **Pure, unit-tested: extend `RecordingConfigurationBuilder` (`Snapper/Recording/RecordingConfigurationBuilder.swift`, from 0011/0013).**
  - Add a `microphone: MicrophoneCaptureInputs` parameter (or field on the builder's existing input struct, matching 0011's shape).
  - In the produced `SCStreamConfiguration`, set:
    - `captureMicrophone = MicrophoneCapturePolicy.shouldCaptureMicrophone(microphone)`;
    - `microphoneCaptureDeviceID = nil` (system default; see research);
    - `capturesAudio = false` **unconditionally**. Set it explicitly, not by relying on the default, so the intent is visible and tested.
  - Do not change any video setting (size, `sourceRect`, frame interval, cursor, pixel format) or the window-filter logic from 0013.
- **Platform glue: `Snapper/Support/MicrophonePermission.swift` (new, unless 0010 already added an equivalent helper, in which case extend that one).**
  - `@MainActor enum MicrophonePermission { static var authorization: MicrophoneAuthorization; static var hasAudioInputDevice: Bool }`.
  - `authorization` maps `AVCaptureDevice.authorizationStatus(for: .audio)` → `MicrophoneAuthorization` (`@unknown default` → `.denied`).
  - `hasAudioInputDevice` is `AVCaptureDevice.default(for: .audio) != nil`.
  - This helper is read-only and never calls `requestAccess`.
- **Platform glue: `RecordingSession` (0011).** Right before building the configuration, read `AppSettings.recordMicrophone`, `MicrophonePermission.authorization`, and `MicrophonePermission.hasAudioInputDevice` on the main actor. Pass them to the builder as `MicrophoneCaptureInputs`. Read them fresh at every recording start. Don't cache them, because the user can change the setting or permission between recordings. Changing the setting during a recording does not affect the recording in progress.
  - Don't add any `SCStreamOutput` of type `.microphone` or `.audio`.
  - The existing 0011 error path (start/write failure → short alert, PRD §7.3) covers any ScreenCaptureKit error raised by microphone capture. Add no new UI.
- **`project.yml`: verify, and add if missing.**
  - `info.properties.NSMicrophoneUsageDescription: "Snapper records your microphone during screen recordings when you turn it on in Settings."` If 0001/0010 already set a string, keep theirs.
  - If the app target enables the hardened runtime (`ENABLE_HARDENED_RUNTIME: YES`), make sure the target's entitlements contain `com.apple.security.device.audio-input: true`. Without it, a hardened-runtime build silently gets no microphone. Add the entitlements file (`Snapper/Snapper.entitlements`, referenced via `entitlements:` in `project.yml`) only if none exists.
  - Run `xcodegen generate` (a new source file is added).
- **No PRD change** in this spec. See *Open questions* for the gap about revoked permission and missing input devices.

## Acceptance criteria

- [ ] With *Record microphone* **off** (default), a ⌘⇧5 recording's `.mp4` has exactly one stream (video) and no audio stream.
- [ ] With *Record microphone* **on** and microphone permission granted, the `.mp4` has a video stream and one audio stream (expected codec AAC) containing the microphone input from the system default input device.
- [ ] System audio is never recorded: `capturesAudio` is `false` in every configuration the builder produces. Music playing during a recording is absent from the file, except what the microphone picks up acoustically from the speakers.
- [ ] Starting a recording never triggers a microphone permission prompt.
- [ ] With the setting on but permission denied, restricted, or not determined, or with no audio input device, the recording starts normally and is saved as video only, with no alert.
- [ ] The microphone-enabled file plays in QuickTime Player with synced audio, and window recordings (0013) and the overlay exclusion (0012) work unchanged.
- [ ] `NSMicrophoneUsageDescription` is present in the built app's `Info.plist`.

## Edge cases

- **Permission revoked after the toggle was turned on** (System Settings → Privacy & Security → Microphone, Snapper off): `authorization == .denied` → video only. The toggle state itself is 0010's concern.
- **Permission reset** (`tccutil reset Microphone com.thera-engineering.snapper`) while the setting is on → `.notDetermined` → video only, and no prompt at recording start (PRD §11).
- **No input device** (e.g. a Mac mini with no mic or headset): `hasAudioInputDevice == false` → video only, so the stream doesn't fail to start because no default microphone exists.
- **Input device unplugged mid-recording, or default input changed mid-recording:** ScreenCaptureKit's behavior here is not documented. Don't add handling. If the stream errors, 0011's stop/error path applies. Observe the result in manual check 7 and report it.
- **Setting toggled while recording:** no effect on the current recording. It applies at the next start.
- **Window recording (0013):** mic capture is independent of the content filter and must work identically for rectangle and window recordings.
- **Cancel (0012):** the discarded file goes away with its audio. Nothing mic-specific is needed.

## Test plan

- **Unit tests: `SnapperTests/MicrophoneCapturePolicyTests.swift`**
  - Full decision table: iterate over `recordMicrophoneSetting ∈ {false, true}` × `authorization ∈ {.authorized, .denied, .restricted, .notDetermined}` × `hasAudioInputDevice ∈ {false, true}` (16 cases). Assert `shouldCaptureMicrophone` is `true` for exactly one case, (`true`, `.authorized`, `true`), and `false` for the other 15.
  - Named regression cases for readability:
    - `testSettingOffNeverCaptures` (off + authorized + device → false);
    - `testRevokedPermissionRecordsVideoOnly` (on + denied + device → false);
    - `testNotDeterminedDoesNotCapture` (on + notDetermined + device → false);
    - `testNoInputDeviceDoesNotCapture` (on + authorized + no device → false).
- **Unit tests: `SnapperTests/RecordingConfigurationBuilderTests.swift` (extend 0011's file)**
  - `testCapturesAudioAlwaysFalse`: for all 16 microphone input combinations, for both a rectangle input and a window input (0013), the built `SCStreamConfiguration.capturesAudio == false`.
  - `testCaptureMicrophoneFollowsPolicy`: the built `captureMicrophone` equals `MicrophoneCapturePolicy.shouldCaptureMicrophone(inputs)` for all 16 combinations.
  - `testMicrophoneDeviceIDIsSystemDefault`: `microphoneCaptureDeviceID == nil` in both the mic-on and mic-off configurations.
  - `testMicrophoneDoesNotAlterVideoSettings`: the configurations built with mic on and mic off have identical `width`, `height`, `sourceRect`, `minimumFrameInterval`, `showsCursor`, and `pixelFormat`.
- **Build check:** `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and `xcodebuild -scheme Snapper test` all succeed. Also confirm that `/usr/libexec/PlistBuddy -c 'Print :NSMicrophoneUsageDescription' <built Snapper.app>/Contents/Info.plist` prints the string. If the hardened runtime is on, confirm that `codesign -d --entitlements - <built Snapper.app>` lists `com.apple.security.device.audio-input`.
- **Manual verification** (needs a real GUI session with Screen Recording permission granted; can't be automated; `ffprobe` is from `brew install ffmpeg`):
  1. **Setting off (default).** In Settings → Recording, make sure *Record microphone* is off. Press ⌘⇧5, drag a rectangle, talk for ~5 s, and press Stop. Run `ffprobe -v error -show_entries stream=codec_type,codec_name -of csv=p=0 "<file>.mp4"`. Expected: a single `h264,video` line and **no** audio line. No microphone indicator (orange dot) appears in the menu bar during the recording.
  2. **Setting on, first time.** Turn *Record microphone* on and grant permission when macOS prompts (prompt comes from 0010, at toggle time). Expected: the toggle stays on.
  3. **Setting on + granted.** Record ~10 s of a rectangle while speaking. Expected: the orange microphone indicator shows during the recording. `ffprobe` (as above) shows `h264,video` **and** one audio line (expected `aac,audio`). The file opens in QuickTime Player, plays without corruption, and your voice is audible and in sync with the video. If the file is unplayable or has no audio track, stop and report it (see the research note on Apple forum thread 805892).
  4. **No system audio.** With the setting on, use headphones (so the mic can't hear the speakers), play music in Music/Safari, and record ~10 s while silent. Expected: the audio track contains only room noise and no music. Repeat with the setting **off**: no audio track at all.
  5. **Window recording.** With the setting on, press ⌘⇧5, press Space, click a window, talk, and press Stop. Expected: video plus audio track as in step 3, and only the window is recorded.
  6. **Revoked permission.** With the setting still on in Snapper, turn Snapper off under System Settings → Privacy & Security → Microphone (relaunch Snapper if macOS asks). Record ~5 s. Expected: no permission prompt and no alert. The recording saves and reveals in Finder as usual, and `ffprobe` shows **no** audio stream. Then run `tccutil reset Microphone com.thera-engineering.snapper` and record again. Expected: same result, with **no** prompt at recording start.
  7. **Input device change (observe only).** With the setting on and a USB/Bluetooth headset as the default input, start a recording and unplug or disconnect the headset after ~3 s, then press Stop. Record what happens (recording continues / audio track switches / stream errors with an alert). This is observation for the report. No specific outcome is required.

## Open questions

1. **Setting on but microphone not authorized, or no input device, at recording start.** The PRD (§7.2, §10, §11) doesn't say what a recording does when *Record microphone* is on but permission was later revoked or reset, or when the Mac has no audio input. **Recommendation (as specified above): record video only, with no prompt and no alert.** PRD §11 forbids prompting outside the toggle, and an alert after every recording would be noisy. The Settings toggle (0010) is the place to surface the problem. If accepted, add one sentence to PRD §7.2 ("If microphone permission is missing or no input device exists, the recording is saved without audio and no alert is shown."). The spec already implements this recommendation. If the user prefers an alert, add a pure "should notify" flag to `MicrophoneCapturePolicy` and a one-time non-blocking alert after saving.

## Out of scope

- The *Record microphone* toggle, the permission request, and the denied-state note with **Open System Settings**: 0010-settings-window.
- Stream lifecycle, file naming/saving, Finder reveal, error alerts: 0011-recording-core (with 0002 for naming).
- Pill, border, Cancel: 0012-recording-overlay. Window filter and frame tracking: 0013-window-recording.
- A microphone device picker, input level meter, mute control, or separate audio tracks: not in the PRD.
- System/app audio capture: PRD §2 non-goal.
