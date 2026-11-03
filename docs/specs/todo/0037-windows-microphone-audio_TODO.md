# 0037 — Windows: record the microphone (AAC) into MP4 recordings and add the Settings Recording tab

> Source: docs/PRD.md §7 item 2 (line 98, *Audio*), §10 *Recording* (line 199, *Record microphone*), §11 (line 209), §13.1 (line 244, *Microphone*), §13.9 (line 327, *Microphone*), §13.10 (line 333), §2 non-goals (no system audio)
> Depends on: 0033-windows-settings-window, 0034-windows-recording-core (and, through them, 0025, 0026, 0027, 0029, 0031)

## Context

The lower-numbered Windows specs deliver:

- `AppSettings` (0027, `windows/src/Snapper.Core/Settings/AppSettings.cs`) with the Boolean `RecordMicrophone` value, default **false**, persisted in `%APPDATA%\Snapper\settings.json`, plus `RecordingFormat` (`Mp4`/`Gif`, default `Mp4`). If 0027 named the JSON key differently, keep its name. If `RecordMicrophone` is missing, add it here with the JSON key `recordMicrophone` and default `false` (a missing or invalid value falls back to `false`, per 0027's per-value fallback).
- The WPF `SettingsWindow` (0033, `windows/src/Snapper.Windows/Settings/SettingsWindow.xaml(.cs)`) with **General** and **Shortcuts** tabs only. 0033 leaves the Recording tab to this spec, 0045, and 0046.
- `RecordingSession` (0034, `windows/src/Snapper.Windows/Recording/RecordingSession.cs`), which captures with Windows.Graphics.Capture and writes H.264/30 fps MP4 through `Mp4Writer` (0034, `windows/src/Snapper.Windows/Recording/Mp4Writer.cs`, an `IMFSinkWriter` wrapper). Its video frames are timestamped with their capture time (`Direct3D11CaptureFrame.SystemRelativeTime`, QPC-based, 100-ns units), at most 30 fps, and no frames arrive while nothing changes or a recorded window is minimized (PRD §13.6). 0034 also owns Stop/Cancel, quit and shutdown save, Explorer reveal, and the write-failure path.

What's missing: recordings never contain audio, and Settings has no Recording tab.

PRD behavior delivered here:

- §7 item 2: *"the microphone is recorded only if the Record microphone setting is on (off by default) and the format is MP4. GIF recordings never capture the microphone. System audio is never recorded. If microphone permission is missing or no input device exists at recording start, record video only without prompting or showing an alert."*
- §13.1: *"Microphone: Media Foundation audio capture from the default input device."* AAC audio in the MP4.
- §13.9: *"turning on Record microphone checks the Windows microphone privacy setting. If access is denied, the toggle switches back off and a note appears with an **Open Privacy Settings** button (`ms-settings:privacy-microphone`). The GIF rule from section 10 applies unchanged."*
- §13.10: *"Microphone permission is checked only when Record microphone is turned on (13.9) and when a recording starts (section 7, item 2)."*

### macOS reference (port these, don't redesign)

- `Snapper/Recording/MicrophoneCapturePolicy.swift:1-12`: `MicrophoneCaptureInputs` + `shouldCaptureMicrophone` (setting && authorized && device). After macOS 0024 (implemented before this spec), the inputs also carry `format`, and `.gif` always returns false (`docs/specs/todo/0024-recording-gif-format_TODO.md:54`).
- `Snapper/Settings/MicrophoneToggleDecision.swift:1-50`: `MicrophoneAuthorization`, `MicrophoneToggleOutcome`, `MicrophoneToggleDecision` (`userToggled`, `accessRequestFinished`, `reconcileOnAppear`), and `MicrophoneRequestState` (keeps a slow permission result from overriding a later toggle-off).
- `Snapper/Support/MicrophonePermission.swift:1-17`: read-only status + "has an input device" adapter. It never requests access.
- Settings glue: `Snapper/Settings/SettingsModel.swift:67-88` (toggle → decision → request → apply; reconcile on appear) and `Snapper/Settings/SettingsView.swift:81-89` (toggle, note "Snapper doesn't have access to the microphone.", button).
- Recording glue: `Snapper/Recording/RecordingSession.swift:148-152` reads the setting, authorization, and device fresh at every start.
- Tests: `SnapperTests/MicrophoneCapturePolicyTests.swift:1-40`, `SnapperTests/MicrophoneToggleDecisionTests.swift:1-64`.
- Spec: `docs/specs/done/0014-microphone-audio_COMPLETED.md`.

**macOS facts that don't apply on Windows:** there is no TCC prompt, no `NSMicrophoneUsageDescription`, no entitlement, and ScreenCaptureKit wrote the audio track by itself. On Windows, Snapper must open the device, read PCM, encode AAC, and align timestamps itself. That's the bulk of this spec.

### Windows research notes (binding design decisions)

- **Privacy check.** `Windows.Security.Authorization.AppCapabilityAccess.AppCapability.Create("microphone").CheckAccess()` (WinRT, available through the `net10.0-windows10.0.22621.0` TFM projections, no NuGet) returns `AppCapabilityAccessStatus`: `Allowed`, `DeniedByUser`, `DeniedBySystem`, `UserPromptRequired`, `NotDeclaredByApp`. Snapper is an **unpackaged** desktop app. Windows has no per-app consent prompt for those, only the *"Let desktop apps access your microphone"* switch (and the global *Microphone access* switch), so `CheckAccess` can report `UserPromptRequired` or `NotDeclaredByApp` even when the device opens fine. **The result of activating the device is authoritative.** `E_ACCESSDENIED` (`0x80070005`) from `MFCreateDeviceSource`, `MFCreateSourceReaderFromMediaSource`, `SetCurrentMediaType`, or the first `ReadSample` means **denied**. Snapper never calls `AppCapability.RequestAccessAsync`, and it never shows its own prompt.
- **Device.** Snapper uses the default capture endpoint for the console role (the *Input* device in Settings → System → Sound). It creates the source with `MFCreateAttributes` + `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID` + `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ROLE = eConsole`, then `MFCreateDeviceSource`. "Has an input device" is `IMMDeviceEnumerator.GetDefaultAudioEndpoint(eCapture, eConsole)` succeeding. `E_NOTFOUND` (`0x80070490`) or any failure means no device. Snapper adds no device picker (none in the PRD).
- **Reader.** `MFCreateSourceReaderFromMediaSource` (synchronous mode), stream `MF_SOURCE_READER_FIRST_AUDIO_STREAM`, output type PCM 16-bit interleaved via `SetCurrentMediaType`. The source reader inserts the resampler and format converter. The format is chosen by the pure `MicrophoneAudioFormat` (below): 48 kHz first, then 44.1 kHz (the only rates the MF AAC encoder accepts), with 1 channel for a mono device and 2 channels otherwise.
- **Encoder.** Snapper adds a second stream to 0034's `IMFSinkWriter` **before `BeginWriting`**. The output type is `MFAudioFormat_AAC` (`MF_MT_AUDIO_SAMPLES_PER_SECOND`, `MF_MT_AUDIO_NUM_CHANNELS`, `MF_MT_AUDIO_BITS_PER_SAMPLE = 16`, `MF_MT_AUDIO_AVG_BYTES_PER_SECOND` from `MicrophoneAudioFormat`, `MF_MT_AAC_PAYLOAD_TYPE = 0`, `MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION = 0x29`). The input type is `MFAudioFormat_PCM` with the same rate and channels, 16-bit, `MF_MT_AUDIO_BLOCK_ALIGNMENT = channels × 2`, and `MF_MT_AUDIO_AVG_BYTES_PER_SECOND = rate × channels × 2`. Because the stream must exist before `BeginWriting`, the microphone is **opened before the writer starts**, and the "audio or not" decision is final for that recording.
- **Shared time base (binding).** All recording timestamps are **QPC time in 100-ns units**, the domain of `Direct3D11CaptureFrame.SystemRelativeTime` and `MFGetSystemTime()`. The MP4 timeline origin **T0** is the QPC time of the first video frame 0034 writes (output time 0). Video output time = frame QPC − T0 (0034, unchanged). Audio output time is derived from each audio sample's QPC time by the pure `AudioClockMapper` + `AudioTimeline` (below). Audio is placed by frame count, so the track stays continuous for AAC, and it is re-anchored to QPC whenever it drifts more than 20 ms.
  - The reader's `llTimestamp` domain isn't documented for audio capture sources. `AudioClockMapper` detects it once, on the first sample. If the timestamp is within 1 s of `MFGetSystemTime()` at receipt, it is already QPC time. Otherwise it is relative, and gets anchored so that the first sample's **end** equals its receipt time.
- **Video gaps.** No video frames arrive while the screen is static or a window is minimized. The MPEG-4 sink then buffers audio while it waits for video to interleave. So `Mp4Writer` sends `IMFSinkWriter.SendStreamTick(videoStream, t)` whenever the audio timeline advances more than 500 ms past the last video sample's time. If 0034 already ticks or repeats frames, reuse that and don't add a second mechanism.

## User story

As a Snapper user on Windows who turned on *Record microphone*, my MP4 recordings contain my voice in sync with the screen. With the setting off, the microphone blocked in Windows privacy settings, or no microphone plugged in, recordings are silent video with no prompt and no alert. System audio is never recorded.

## Scope & changes

### Pure logic (`Snapper.Core`, xUnit-tested)

- **`windows/src/Snapper.Core/Settings/MicrophoneToggleDecision.cs` (new):** port of `MicrophoneToggleDecision.swift:1-50`.
  - `public enum MicrophoneAuthorization { NotDetermined, Authorized, Denied, Restricted }`.
  - `public readonly record struct MicrophoneToggleOutcome(bool StoredValue, bool ShowsDeniedNote, bool ShouldRequestAccess)`.
  - `public static class MicrophoneToggleDecision`:
    - `UserToggled(bool requested, MicrophoneAuthorization status)`: identical to Swift. Off → `(false, false, false)`. Authorized → `(true, false, false)`. NotDetermined → `(false, false, true)` (on Windows, "request access" means "run the activation probe"; see glue). Denied/Restricted → `(false, true, false)`.
    - `AccessRequestFinished(bool granted)`: identical to Swift, `(granted, !granted, false)`.
    - `ReconcileOnAppear(bool stored, MicrophoneAuthorization status)`. **Windows deviation from Swift:** stored false → `(false, false, false)`. Stored true + Authorized → `(true, false, false)`. Stored true + Denied/Restricted → `(false, true, false)`. Stored true + **NotDetermined → `(true, false, false)`** (keep the user's choice). For an unpackaged app, NotDetermined is a normal steady state and doesn't mean "reset". The recording start resolves it by activating the device. Add a doc comment explaining this.
  - `public sealed class MicrophoneRequestState`: port as is (`UserToggled(bool)`, `bool BeginRequest()`, `bool FinishRequest()`, `bool IsPending`, `bool WantsMicrophone`).
- **`windows/src/Snapper.Core/Support/MicrophoneAccessMapping.cs` (new, Windows-only mapping, no WinRT types):**
  - `public enum MicrophoneCapabilityStatus { Allowed, DeniedByUser, DeniedBySystem, UserPromptRequired, NotDeclaredByApp, Unavailable }`. It mirrors `AppCapabilityAccessStatus`, and `Unavailable` means the WinRT call threw.
  - `public enum MicrophoneOpenResult { Opened, AccessDenied, NoDevice, Failed }`.
  - `public static class MicrophoneAccessMapping`:
    - `MicrophoneAuthorization FromCapability(MicrophoneCapabilityStatus s)`: Allowed → Authorized, DeniedByUser → Denied, DeniedBySystem → Restricted, UserPromptRequired/NotDeclaredByApp/Unavailable → NotDetermined.
    - `bool ProbeGrantsAccess(MicrophoneOpenResult r)`: only `AccessDenied` → false. `Opened`, `NoDevice`, and `Failed` → true, because those aren't permission problems and the recording start handles them.
    - `MicrophoneOpenResult ClassifyHResult(int hresult)`: `0x80070005` (E_ACCESSDENIED) → AccessDenied, `0x80070490` (E_NOTFOUND) and `MF_E_NO_CAPTURE_DEVICES_AVAILABLE` (`0xC00DABE0`) → NoDevice, any other failure → Failed. The implementer verifies the MF constants against the SDK headers and keeps them as named constants.
- **`windows/src/Snapper.Core/Recording/MicrophoneCapturePolicy.cs` (new):** port of `MicrophoneCapturePolicy.swift:1-12` including macOS 0024's format input.
  - `public readonly record struct MicrophoneCaptureInputs(bool RecordMicrophoneSetting, MicrophoneAuthorization Authorization, bool HasAudioInputDevice, RecordingFormat OutputFormat)`. `RecordingFormat` comes from 0027.
  - `public static bool ShouldAttemptMicrophone(MicrophoneCaptureInputs i)` returns true only when `RecordMicrophoneSetting && OutputFormat == RecordingFormat.Mp4 && HasAudioInputDevice && Authorization is Authorized or NotDetermined`. **Windows deviation:** NotDetermined attempts to open the device, because opening it never prompts for an unpackaged app (research notes), and `AccessDenied` from the attempt is then handled as denied. Denied/Restricted never attempt. GIF never attempts (PRD §7 item 2).
  - Note in the doc comment: *"GIF recordings never capture the microphone (PRD §7.2). Until 0046 the session always passes `RecordingFormat.Mp4`, because it only produces MP4."*
- **`windows/src/Snapper.Core/Recording/MicrophoneAudioFormat.cs` (new):**
  - `public readonly record struct AudioFormatChoice(int SampleRate, int Channels, int BitsPerSample, int AacAverageBytesPerSecond)` with `BlockAlign => Channels * BitsPerSample / 8` and `PcmAverageBytesPerSecond => SampleRate * BlockAlign`.
  - `public static IReadOnlyList<AudioFormatChoice> Candidates(int nativeChannels)` returns, in order, 48000 Hz and then 44100 Hz. Each uses `Channels = nativeChannels <= 1 ? 1 : 2`, `BitsPerSample = 16`, and `AacAverageBytesPerSecond = Channels == 1 ? 12000 : 24000` (96/192 kbps, both values the MF AAC encoder accepts). `nativeChannels <= 0` is treated as 2.
- **`windows/src/Snapper.Core/Recording/AudioClockMapper.cs` (new):** maps reader timestamps to QPC 100-ns time.
  - `public sealed class AudioClockMapper(long toleranceTicks = 10_000_000)`.
  - `long ToQpc(long readerTimestamp, long durationTicks, long qpcNowAtReceipt)`. On the first call, if `|readerTimestamp − qpcNowAtReceipt| ≤ tolerance`, it sets `Domain = SystemTime` with offset 0. Otherwise it sets `Domain = Relative` with `offset = qpcNowAtReceipt − (readerTimestamp + durationTicks)`. Every call returns `readerTimestamp + offset`. The domain and offset are fixed after the first call.
  - `public AudioClockDomain? Domain { get; }` (`enum AudioClockDomain { SystemTime, Relative }`).
- **`windows/src/Snapper.Core/Recording/AudioTimeline.cs` (new):** places PCM samples on the MP4 timeline.
  - `public sealed class AudioTimeline(int sampleRate, int jitterToleranceFrames /* default sampleRate / 50 = 20 ms */)`.
  - `void SetOrigin(long t0Qpc)` (once; T0 as defined above), `void SetEnd(long endQpc)` (optional, set at Stop), `long FramesWritten`, `long CurrentEndTicks` (`FramesWritten × 10_000_000 / sampleRate`, integer math without overflow).
  - `IReadOnlyList<AudioPlacement> Place(long sampleQpc, int frameCount)`, where `AudioPlacement = readonly record struct (long OutputTimeTicks, int SkipFrames, int FrameCount, bool IsSilence)`. It throws `InvalidOperationException` if the origin isn't set. Algorithm:
    1. `start = round((sampleQpc − T0) × sampleRate / 10_000_000)` in frames (128-bit-safe math, e.g. `Int128` or `Math.BigMul`). `delta = start − FramesWritten`.
    2. If `delta > tolerance`: emit a silence placement of `delta` frames at the current end, then the sample at `start`.
    3. If `|delta| ≤ tolerance`: place the sample contiguously at the current end, absorbing jitter.
    4. If `delta < −tolerance`: skip `min(frameCount, −delta)` leading frames (overlap or pre-origin audio). If nothing is left, return an empty list.
    5. If an end is set, trim the placement so it ends at or before `endFrame = round((end − T0) × rate / 1e7)`. Samples starting at or after the end return an empty list.
    6. `FramesWritten` advances by every emitted frame, silence included. `OutputTimeTicks` is always `framesBefore × 1e7 / rate`, so AAC input stays gap-free.
- **No change** to 0034's pure recording types (`RecordingConfiguration`, `RecordingLifecycle`, `RecordingTerminationDecision`). The microphone doesn't change video size, fps, cursor, or termination decisions. If 0034's `RecordingConfiguration` is the natural carrier, add `bool IncludesMicrophone` to it and assert in the tests that the video fields are unaffected.

### Platform glue (`Snapper.Windows`, manually verified)

- **`windows/src/Snapper.Windows/Support/MicrophonePermission.cs` (new):** a read-only adapter (port of `MicrophonePermission.swift`).
  - `static MicrophoneCapabilityStatus CapabilityStatus()` wraps `AppCapability.Create("microphone").CheckAccess()` in `try/catch` and returns `Unavailable` on any exception.
  - `static MicrophoneAuthorization Authorization => MicrophoneAccessMapping.FromCapability(CapabilityStatus())`.
  - `static bool HasAudioInputDevice` uses `IMMDeviceEnumerator.GetDefaultAudioEndpoint(eCapture, eConsole)`, releases the COM objects, and returns false on any failure.
  - `static Task<MicrophoneOpenResult> ProbeAsync()` runs on a background (MTA) thread. It runs `MicrophoneSource.TryOpen` and disposes the source immediately without reading samples, then returns the result. This is the Windows "request access" used by the Settings toggle. It never calls `RequestAccessAsync`.
- **`windows/src/Snapper.Windows/Recording/MicrophoneSource.cs` (new, `IDisposable`):** Media Foundation audio capture.
  - `static MicrophoneOpenResult TryOpen(out MicrophoneSource? source)` creates the device source (AUDCAP GUID + console role), wraps it in a synchronous source reader, deselects all streams except `MF_SOURCE_READER_FIRST_AUDIO_STREAM`, reads the native channel count, and tries `MicrophoneAudioFormat.Candidates` in order with `SetCurrentMediaType` (PCM 16-bit). It exposes `AudioFormatChoice Format`. Every failing HRESULT is classified with `MicrophoneAccessMapping.ClassifyHResult`, and all partial COM objects are released on failure (`IMFMediaSource.Shutdown` + release).
  - `void Start(Action<MicrophoneChunk> onChunk, Action<MicrophoneOpenResult> onEnded)` starts a dedicated background thread (name `"Snapper microphone"`, MTA) that loops `ReadSample(FIRST_AUDIO_STREAM, 0, …)`. For each sample it calls `ConvertToContiguousBuffer` and copies the bytes into a pooled `byte[]`. It computes `durationTicks = frames × 1e7 / rate`, `qpcNow = MFGetSystemTime()`, and `qpc = clockMapper.ToQpc(llTimestamp, durationTicks, qpcNow)`, then calls `onChunk(new MicrophoneChunk(qpc, frames, bytes))`. The flags are handled like this: `MF_SOURCE_READERF_STREAMTICK` and null samples are skipped. `ERROR`, `ENDOFSTREAM`, or a failed HRESULT ends the loop and calls `onEnded` with the classified result. An `E_ACCESSDENIED` on the **first** read reports `AccessDenied`.
  - `void Stop()` sets a flag, waits for the thread to exit (bounded by 1 s), then shuts down the media source. `Dispose` calls `Stop` and releases everything. Callbacks run on the microphone thread and never touch UI.
- **`Mp4Writer` (0034), extended:**
  - Its creation takes an optional `AudioFormatChoice? audio`. When it's set, `AddStream` adds the AAC output type and `SetInputMediaType` sets PCM, both before `BeginWriting`. It exposes `bool HasAudio`.
  - It exposes the timeline origin, `long? TimelineOriginQpc` (T0, set when the first video frame is written), and the video's last written time. If 0034 already exposes equivalents, use them.
  - `void WriteAudio(AudioPlacement p, ReadOnlySpan<byte> pcm)` creates an `IMFSample` with a memory buffer, sets sample time `p.OutputTimeTicks` and duration `p.FrameCount × 1e7 / rate`, and calls `WriteSample(audioStream, …)`. Silence placements write zeroed buffers (split into chunks of at most 100 ms).
  - It sends a video `SendStreamTick` when the audio end runs more than 500 ms past the last video sample (research notes).
  - All sink-writer calls (video from the capture thread, audio from the microphone thread, `Finalize`) are serialized by one lock inside `Mp4Writer`. An audio `WriteSample` failure is a **write failure** and goes through 0034's existing write-failure path, with no new UI.
- **`RecordingSession` (0034), extended:**
  - **At every start**, read fresh values and don't cache: `AppSettings.RecordMicrophone`, `MicrophonePermission.Authorization`, and `MicrophonePermission.HasAudioInputDevice`. Pass `OutputFormat: RecordingFormat.Mp4`, which 0046 replaces with the snapshotted format. If `ShouldAttemptMicrophone` is true, call `MicrophoneSource.TryOpen` **off the UI thread, in parallel with the capture setup**. Anything but `Opened` means video only: no audio stream, no alert, no prompt. Log the result with `Debug.WriteLine`. When the source opened, create `Mp4Writer` with its `Format`.
  - When the writer has audio, start the microphone right after `BeginWriting`. Chunks that arrive before T0 is known go into a bounded queue (at most 2 s of audio; the oldest is dropped first). Once the first video frame sets T0, call `AudioTimeline.SetOrigin(T0)`, drain the queue, and from then on run `Place` → `WriteAudio` directly on the microphone thread.
  - **Stop / quit / shutdown save** (all through 0034's save path): call `AudioTimeline.SetEnd(qpcAtStop)`, where `qpcAtStop = MFGetSystemTime()` when Stop is processed. Then `MicrophoneSource.Stop()`, which places and writes any chunks still queued. Only then finalize the writer. A recording with no video frame (T0 never set) follows 0034's behavior, and its queued audio is dropped.
  - **Cancel** (0035): stop and dispose the microphone source, then let 0034/0035 discard the file.
  - **Microphone ends mid-recording** (device unplugged, `onEnded` with any result): stop feeding audio. The video keeps recording, and the file is saved with an audio track that ends where the device was lost. No alert. The audio track length follows the device loss, and the video length is unchanged.
  - Changing the setting during a recording has no effect until the next start.
  - **System audio is never captured.** Snapper never uses a WASAPI loopback endpoint, `AUDCAP_ENDPOINT_ID` of a render device, or Windows.Graphics.Capture audio. Only the capture endpoint above is used.
- **Settings Recording tab** (`SettingsWindow`, 0033, extended):
  - Add a third tab, **Recording**, after Shortcuts. It contains only:
    - a `CheckBox` (or toggle-styled `CheckBox`, matching 0033's General toggles) labeled **"Record microphone"**, bound to `AppSettings.RecordMicrophone` through the decision logic below;
    - a note shown only while `ShowsDeniedNote` is true: secondary text **"Snapper doesn't have access to the microphone."** and a button **"Open Privacy Settings"** that launches `ms-settings:privacy-microphone` (`Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true })`, or 0033's `ms-settings` helper if it has one).
  - Toggle handler (port of `SettingsModel.swift:67-78`): `requestState.UserToggled(v)`. Apply `MicrophoneToggleDecision.UserToggled(v, MicrophonePermission.Authorization)`, which writes the setting and shows or hides the note. If `ShouldRequestAccess && requestState.BeginRequest()`, `await MicrophonePermission.ProbeAsync()`, then on the dispatcher, `if (requestState.FinishRequest())` apply `AccessRequestFinished(MicrophoneAccessMapping.ProbeGrantsAccess(result))`. While the probe runs, the checkbox shows checked (the user's intent) without writing `true`. The decision writes `StoredValue = false` until the probe finishes, so if Settings is closed mid-probe, the setting stays off.
  - Reconcile (port of `SettingsModel.swift:85-88`): when the Settings window opens or is re-activated, and when the Recording tab is selected, apply `ReconcileOnAppear(stored, MicrophonePermission.Authorization)`. Only `CheckAccess` is used here, never the probe. That means a user who blocks the microphone in Windows settings and returns to Snapper sees the toggle off with the note.
  - Add `Recording` to the Settings tab enum used by `AppCoordinator.OpenSettings(tab)` (0025/0033) if it isn't there yet.
  - Leave room for 0045 (MP4 quality picker above the toggle) and 0046 (Format, GIF quality, and the toggle disabled with "GIFs don't include sound."). Don't add those controls here.
- **`windows/src/Snapper.Windows/NativeMethods.txt`**, add if not already listed by 0034: `MFCreateDeviceSource`, `MFCreateAttributes`, `MFCreateSourceReaderFromMediaSource`, `MFCreateMediaType`, `MFCreateMemoryBuffer`, `MFCreateSample`, `MFGetSystemTime`, `IMFSourceReader`, `IMFMediaSource`, `IMFMediaType`, `IMFSample`, `IMFMediaBuffer`, `IMFAttributes`, `IMFSinkWriter`, `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE`, `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID`, `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ROLE`, `MF_SOURCE_READER_CONSTANTS`, `MF_SOURCE_READER_FLAG`, `MFMediaType_Audio`, `MFAudioFormat_PCM`, `MFAudioFormat_AAC`, `MF_MT_MAJOR_TYPE`, `MF_MT_SUBTYPE`, `MF_MT_AUDIO_SAMPLES_PER_SECOND`, `MF_MT_AUDIO_NUM_CHANNELS`, `MF_MT_AUDIO_BITS_PER_SAMPLE`, `MF_MT_AUDIO_BLOCK_ALIGNMENT`, `MF_MT_AUDIO_AVG_BYTES_PER_SECOND`, `MF_MT_AAC_PAYLOAD_TYPE`, `MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION`, `IMMDeviceEnumerator`, `MMDeviceEnumerator`, `EDataFlow`, `ERole`. If CsWin32 can't generate a GUID constant, define it in `windows/src/Snapper.Windows/Interop/MediaFoundationGuids.cs` with a comment naming its SDK header.
- No new NuGet packages. No change to `docs/PRD.md` or `AGENTS.md` beyond what's noted under *Open questions*.

## Acceptance criteria

- [ ] With *Record microphone* **off** (default), a Ctrl+Shift+5 recording's `.mp4` has exactly one stream (H.264 video) and no audio stream. Windows' microphone-in-use indicator never appears.
- [ ] With the setting **on** and microphone access allowed, the `.mp4` has an H.264 video stream and one **AAC** audio stream (48 kHz or 44.1 kHz, mono or stereo) from the default input device, and the audio plays in sync with the video in the Windows Media Player app.
- [ ] Sync holds over a 5-minute recording (clap test: drift ≤ 100 ms at the end) and across a static-screen period of ≥ 10 s with no video frames.
- [ ] System audio is never recorded. Music playing through headphones is absent from the file.
- [ ] Starting a recording never shows a prompt. With the setting on but access blocked, no input device, or a device that fails to open, the recording starts normally and saves as video only, with no alert.
- [ ] Settings has a **Recording** tab with **Record microphone**. Turning it on with access allowed keeps it on. With access blocked, it switches back off and shows "Snapper doesn't have access to the microphone." with **Open Privacy Settings**, which opens Settings → Privacy & security → Microphone.
- [ ] Reopening Settings after blocking access in Windows shows the toggle off with the note. With access allowed, an on toggle stays on.
- [ ] Window recordings (0036, if present), Stop via shortcut or tray, quit/shutdown save, Cancel, and Explorer reveal behave exactly as before, with audio included where applicable.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` are green.

## Edge cases

- **`CheckAccess` reports `UserPromptRequired`/`NotDeclaredByApp`/throws** (expected for unpackaged apps on some builds): the authorization is NotDetermined. The toggle runs the probe, and the recording start attempts to open the device. Only `E_ACCESSDENIED` counts as denied.
- **Access blocked after the toggle was turned on** (Settings → Privacy & security → Microphone → "Let desktop apps access your microphone" off): the next recording is video only, and the next Settings open shows the toggle off with the note.
- **Global "Microphone access" off** (`DeniedBySystem` → Restricted): same as denied.
- **No input device:** `HasAudioInputDevice` is false, so Snapper doesn't attempt and records video only. The toggle can still be turned on (the probe returns `NoDevice`, which grants).
- **Device present but in exclusive use or broken** (`Failed`): video only, no alert.
- **Device unplugged or default input changed mid-recording:** the microphone thread ends, the video continues, and the file has a shorter audio track. No alert. Switching to the new default device mid-recording is out of scope.
- **Audio arrives before the first video frame:** it is queued (≤ 2 s) and then trimmed by `AudioTimeline` to start at T0. **Audio after Stop** is clipped at `qpcAtStop`.
- **Static screen or minimized window** (no video frames): audio continues, and video stream ticks keep the sink from stalling. The video holds the last frame (PRD §13.6).
- **Audio device clock drift vs QPC:** corrected by re-anchoring once the drift exceeds 20 ms (silence insertion or overlap trim).
- **Reader timestamps in a relative domain:** `AudioClockMapper` anchors them at the first sample's receipt time.
- **Mono device:** a mono AAC track (12000 B/s). **More than 2 channels:** downmixed to stereo by the source reader.
- **Device supports neither 48 kHz nor 44.1 kHz via the reader:** `Failed`, so video only.
- **Toggled on, off, and on again while the probe is pending:** `MicrophoneRequestState` makes the last intent win and prevents a second probe.
- **Format GIF (after 0046):** `ShouldAttemptMicrophone` is false. Before 0046 the session always passes `Mp4`.
- **Rapid Stop right after start** (T0 set, very little audio): a valid file with a short or empty audio track. If the sink writer rejects finalizing an audio stream with zero samples, write 1 frame-duration of silence at time 0 before finalizing.

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`):
  - `MicrophoneCapturePolicyTests.cs` (port of `SnapperTests/MicrophoneCapturePolicyTests.swift:1-40`):
    - `FullDecisionTable`: setting {false, true} × authorization (all 4) × device {false, true} × format {Mp4, Gif} (32 cases). The result is true only for setting && device && Mp4 && authorization ∈ {Authorized, NotDetermined}, which is exactly 2 cases.
    - `SettingOffNeverAttempts` (off + Authorized + device + Mp4 → false), `DeniedRecordsVideoOnly` (on + Denied → false), `RestrictedRecordsVideoOnly` (on + Restricted → false), `NotDeterminedAttemptsOnWindows` (on + NotDetermined + device + Mp4 → true), `NoInputDeviceDoesNotAttempt` (on + Authorized + no device → false), `GifNeverAttempts` (on + Authorized + device + Gif → false).
  - `MicrophoneToggleDecisionTests.cs` (port of `SnapperTests/MicrophoneToggleDecisionTests.swift:1-64`):
    - `ToggleDecisionTable` (theory over all 4 statuses): toggling off → `(false,false,false)`. Toggling on → Authorized `(true,false,false)`, NotDetermined `(false,false,true)`, Denied/Restricted `(false,true,false)`.
    - `RequestCompletion`: granted → `(true,false,false)`, not granted → `(false,true,false)`.
    - `ReconcileNeverRequests` (theory): stored false → `(false,false,false)`. Stored true → Authorized `(true,false,false)`, **NotDetermined `(true,false,false)`** (Windows deviation), Denied/Restricted `(false,true,false)`. `ShouldRequestAccess` is false in every case.
    - `PendingRequestHonorsLatestIntentAndDoesNotDuplicateRequests` and `OnOffOnWhilePendingAppliesToLatestOn`: ported verbatim from the Swift tests (lines 33-63).
  - `MicrophoneAccessMappingTests.cs`:
    - `FromCapability`: Allowed → Authorized, DeniedByUser → Denied, DeniedBySystem → Restricted, UserPromptRequired/NotDeclaredByApp/Unavailable → NotDetermined.
    - `ProbeGrantsAccess`: AccessDenied → false, Opened/NoDevice/Failed → true.
    - `ClassifyHResult`: `unchecked((int)0x80070005)` → AccessDenied, `0x80070490` → NoDevice, `MF_E_NO_CAPTURE_DEVICES_AVAILABLE` → NoDevice, `unchecked((int)0x80004005)` (E_FAIL) → Failed.
  - `MicrophoneAudioFormatTests.cs`:
    - `StereoCandidates`: native 2 → [(48000, 2, 16, 24000), (44100, 2, 16, 24000)].
    - `MonoCandidates`: native 1 → [(48000, 1, 16, 12000), (44100, 1, 16, 12000)].
    - `MultichannelDownmixesToStereo`: native 6 → Channels 2.
    - `UnknownChannelCountTreatedAsStereo`: native 0 → Channels 2.
    - `DerivedValues`: 48000/2ch → BlockAlign 4, PcmAverageBytesPerSecond 192000.
  - `AudioClockMapperTests.cs`:
    - `SystemTimeDomainPassesThrough`: first ts = now − 5 ms → Domain SystemTime, ToQpc returns ts unchanged, and so does a later sample.
    - `RelativeDomainAnchorsFirstSampleEndAtReceipt`: ts 0, duration 10 ms, now 5_000_000_000 → returns 5_000_000_000 − 100_000, Domain Relative. Next ts 100_000 → 5_000_000_000.
    - `DomainDecidedOnce`: after Relative is chosen, a later timestamp that happens to be near now still uses the offset.
    - `ToleranceBoundary`: |ts − now| exactly 1 s → SystemTime. 1 s + 1 tick → Relative.
  - `AudioTimelineTests.cs` (48 kHz, tolerance 960 frames):
    - `ThrowsBeforeOrigin`.
    - `ContiguousSamplesPlacedBackToBack`: T0 = 1_000_000_000, two 480-frame samples at T0 and T0 + 100_000 → placements at 0 and 100_000 ticks, FramesWritten 960.
    - `JitterWithinToleranceAbsorbed`: second sample 5 ms late → placed at 100_000 (contiguous), no silence.
    - `GapInsertsSilence`: second sample 50 ms after the first's end → a silence placement of 2400 frames at 100_000, then the sample at 600_000.
    - `OverlapTrimsLeadingFrames`: sample starting 30 ms before the current end → SkipFrames 1440, remaining frames placed at the current end.
    - `PreOriginSampleTrimmed`: sample of 4800 frames starting 50 ms before T0 → SkipFrames 2400, FrameCount 2400 at 0.
    - `EntirelyPreOriginDropped`: a sample ending 30 ms before T0 → empty list.
    - `EndClipsStraddlingSample`: SetEnd(T0 + 1 s), sample of 4800 frames at T0 + 0.95 s (contiguous) → FrameCount 2400.
    - `SampleAfterEndDropped`: sample at or after the end → empty.
    - `LargeQpcValuesDoNotOverflow`: T0 = 9e17 ticks → correct frame positions.
    - `DriftCorrection`: 1000 consecutive 480-frame samples whose QPC spacing is 10.002 ms (0.02 % fast) → total silence inserted ≈ the accumulated drift (± tolerance), and no placement overlaps.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed (both run on macOS; `Snapper.Windows` compiles via `EnableWindowsTargeting`).
- **Manual verification** (Windows 11 machine with a microphone; can't be automated; `ffprobe` from an ffmpeg build, or MediaInfo):
  1. **Setting off (default).** Settings → Recording shows **Record microphone** unchecked. Press Ctrl+Shift+5, drag a rectangle, talk for ~5 s, then click the tray icon to stop. Run `ffprobe -v error -show_entries stream=codec_type,codec_name,sample_rate,channels -of csv=p=0 "<file>.mp4"`. Expected: only `h264,video` and no audio line. No microphone indicator in the taskbar during the recording.
  2. **Turn on, access allowed.** Check Settings → Privacy & security → Microphone: *Microphone access* and *Let desktop apps access your microphone* are on. In Snapper Settings → Recording, check **Record microphone**. Expected: it stays on, no note, no Windows prompt. Close and reopen Settings: still on.
  3. **Recording with audio.** Record ~10 s while speaking and clapping at the start. Expected: the taskbar microphone indicator shows during the recording, not before Ctrl+Shift+5 and not after Stop. `ffprobe` shows `h264,video` and `aac,audio,48000` (or 44100) with 1 or 2 channels. The file plays in the Media Player app with audible voice, and the clap is in sync with the screen (show a stopwatch on screen).
  4. **Long sync and static screen.** Record 5 minutes of a stopwatch window while clapping at 0 s and at about 4:50. Leave the screen static (no changes) for ≥ 10 s in the middle. Expected: both claps are in sync (≤ 100 ms), playback doesn't freeze or stutter around the static period, and the file finalizes normally.
  5. **No system audio.** Use headphones, play music, and record ~10 s while silent. Expected: the audio track has only room noise. Repeat with the setting off: no audio stream.
  6. **Blocked access.** Turn off *Let desktop apps access your microphone*. Reopen Snapper Settings → Recording. Expected: the toggle is off and the note "Snapper doesn't have access to the microphone." with **Open Privacy Settings** shows. Click it: Settings → Privacy & security → Microphone opens. Check the toggle again: it switches back off with the note. Then edit `%APPDATA%\Snapper\settings.json` to set the microphone value to `true` (Snapper not running), relaunch, and record. Expected: no prompt, no alert, saved and revealed in Explorer, and `ffprobe` shows video only. Repeat with the global *Microphone access* off.
  7. **No input device.** Disable all recording devices (Settings → System → Sound → Input → each device → *Don't allow*) with the setting on. Record. Expected: video only, no alert. Re-enable them afterwards.
  8. **Unplug mid-recording** (USB/Bluetooth headset as the default input). Start a recording, unplug the headset after ~3 s, keep recording ~5 s, and stop. Expected: no alert, the file saves and plays, the video covers the full duration, and the audio covers ~3 s.
  9. **Cancel and quit.** With the setting on, start a recording and click Cancel in the pill (0035): no file, and the microphone indicator disappears. Start another, then choose Exit Snapper from the tray menu… (0034's quit path: the recording saves before exit). Expected: the file has audio.
  10. **Window recording** (if 0036 is implemented): Ctrl+Shift+5, Space, click a window, talk, Stop → video of the window plus a synced AAC track. Minimize the window for 5 s mid-recording: audio continues, the video holds the last frame, and sync is intact after restoring.
  11. **Prompt check:** across all steps above, no Windows consent prompt appears at recording start. If one ever appears, stop and report it (it would contradict PRD §7.2).

## Resolved decisions

- "Not determined" access tries the device, and only an explicit denial or `E_ACCESSDENIED` turns the toggle off (PRD §13.9). Losing the microphone mid-recording keeps partial audio with no alert (PRD §13.6).

## Out of scope

- The Settings window shell and its General/Shortcuts tabs: **0033**. Recording start/stop, tray state, MP4 writer setup, quit/shutdown save, Explorer reveal, and the write-failure alert: **0034**. The pill, border, and Cancel: **0035**. Window recording: **0036**.
- The MP4 quality picker and the Medium/Low re-encode with audio passthrough: **0045**.
- The Format/GIF quality pickers, passing the real format into `MicrophoneCaptureInputs`, and disabling the toggle with "GIFs don't include sound.": **0046**. This spec already implements the GIF rule in `ShouldAttemptMicrophone`.
- A microphone device picker, level meter, mute, following default-device changes mid-recording, and separate tracks: not in the PRD.
- System/app audio capture (loopback): PRD §2 non-goal.
- macOS-only items: `NSMicrophoneUsageDescription`, the audio-input entitlement, TCC prompts, and `tccutil`.
