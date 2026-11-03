# 0045 — Add the Windows MP4 quality setting with a post-Stop re-encode step

> Source: docs/PRD.md §7 item 2 (capture format), item 3 (write-failure partial file), item 6 ("Format and quality": MP4 quality, conversion state, failure fallback), §10 Recording (Quality picker, MP4 only here), §12 ("MP4 Medium and Low produce smaller files than High…"), §13.1 (Media Foundation for the re-encode), §13.3 (tray while converting), §13.6 (shutdown block while converting). Windows port of the macOS spec `docs/specs/todo/0023-recording-export-mp4-quality_TODO.md`.
> Depends on: 0037-windows-microphone-audio (and through it 0025–0036: tray 0025, file naming/save folder 0026, settings 0027, app mode 0029, error alerts 0031, Settings window 0033, recording core 0034, pill/border 0035, window recording 0036). 0046-windows-recording-gif-format builds on this spec.

## Context

When this spec starts, every Windows recording (0034, 0036, 0037) is written by `Recording/Mp4Writer` (an `IMFSinkWriter`, H.264 + optional AAC) **directly to the final path in the save folder**:

- `App/AppCoordinator.StartRecording` (0034) reserves the final path with the 0026 unique-path helper for `CaptureKind` recording at the start time, and hands it to `Recording/RecordingSession`.
- `Recording/RecordingSession` (0034) drives the pure Core `Recording/RecordingLifecycle` (a port of `Snapper/Recording/RecordingLifecycle.swift:28-96`). On a normal finish the lifecycle emits `Reveal(path), Ended` (Swift `:71-77`); on a write failure with content it reveals the partial file (Swift `:88-95`). `Ended` releases the WGC session, the sink writer and the microphone source, removes the shutdown block reason (`Recording/ShutdownBlocker`, 0034), returns `AppModeMachine` (0029) to idle and lets a deferred exit proceed (`Recording/RecordingTerminationDecision`, port of `Snapper/Recording/RecordingTerminationDecision.swift:1-13`).
- `Settings/AppSettings` (0027) already stores `RecordingFormat` (default `Mp4`), `Mp4Quality` (default `High`) and `GifQuality` (default `Medium`), but nothing reads the qualities and the Settings window's Recording tab (0037) only has the *Record microphone* toggle.

PRD §7 item 6 defines the behavior this spec delivers (MP4 part):

- *MP4 quality* — **High** (default): the capture file as recorded, no re-encode. **Medium** / **Low**: after Stop, re-encode the video as H.264 at the same pixel size and timing, average bitrate = width × height × 30 × **0.10** (Medium) or × **0.05** (Low) bits/s. Microphone audio is passed through unchanged.
- When a re-encode is needed, Snapper records to a temporary MP4 and converts it after Stop. While converting: the pill and border are already gone, the tray icon stays in its recording state (red dot, tooltip "Snapper — Recording", §13.3), tray clicks and Ctrl+Shift+5 do nothing, Ctrl+Shift+4/6 stay ignored, and quitting waits for the conversion. Cancel deletes the temp file without converting.
- If the conversion fails: the original recording is saved to the save folder as `Recording … .mp4`, revealed in File Explorer, and an alert shows **"Couldn't compress the recording"** with the error. A partial recording kept after a write failure (item 3) is saved the same way, as an MP4, without converting.
- §13.6: on sign-out/restart/shutdown while converting, the "Saving recording…" shutdown block reason stays registered until the conversion has finished.

### macOS reference implementation (port this, don't redesign it)

The macOS spec 0023 is implemented before this batch. Port its decision logic and tests:

- `Snapper/Recording/RecordingOutputPlan.swift` (`RecordingOutputPlan`, `RecordingTranscode`, `MP4Bitrate`; spec `docs/specs/todo/0023-recording-export-mp4-quality_TODO.md:31-46`) and `SnapperTests/RecordingOutputPlanTests.swift` (spec `:115-119`).
- The conversion stage of `Snapper/Recording/RecordingLifecycle.swift` (spec `:47-58`) and its new cases in `SnapperTests/RecordingLifecycleTests.swift` (spec `:120-130`). Existing cases to keep passing are at `SnapperTests/RecordingLifecycleTests.swift:19-190`.
- `RecordingFailure.title` / `.compression` (spec `:58`).
- `Snapper/Recording/MP4Transcoder.swift` (spec `:65-70`) and the `RecordingSession` `.convert` / `.saveOriginal` handling (spec `:71-77`) — re-implemented here with Media Foundation instead of AVFoundation.

Differences from macOS:
- Windows `Settings` (0027) has separate `Mp4Quality` and `GifQuality` enums instead of one shared `RecordingQuality`; use them.
- `CaptureKind` (0026) needs no change in this spec: with the MP4 format the final file and the fallback original are both `.mp4`. 0046 handles `.gif` naming.
- No `showOnboarding` / permission path (there is no Screen Recording permission on Windows).
- The tray, not a menu bar item; "Finder reveals" → File Explorer via `Support/ExplorerReveal` (0026).
- The temp file lives in `%TEMP%\Snapper\` instead of `$TMPDIR`.
- The Exit menu item can't be reached while recording or converting (a tray click stops instead of opening the menu, §13.3), so the "quit waits" paths on Windows are session end (§13.6) and any programmatic exit 0034 already routes through `RecordingTerminationDecision`.

## User story

As a Snapper user on Windows, I can choose High, Medium, or Low MP4 quality in Settings → Recording, and Medium/Low recordings come out noticeably smaller at the same resolution and length, with my microphone audio intact.

## Scope & changes

### Pure logic — `Snapper.Core` (xUnit-tested)

- **New `windows/src/Snapper.Core/Recording/RecordingExportPlan.cs`** (port of `RecordingOutputPlan.swift`):
  ```csharp
  public abstract record RecordingTranscode
  {
      public sealed record Mp4(int AverageBitRate) : RecordingTranscode;
      // 0046 adds: Gif(GifEncodingSettings, int Width, int Height)
  }
  public sealed record RecordingExportPlan(RecordingFormat Format, RecordingTranscode? Transcode)
  {
      public bool Converts => Transcode is not null;
      public static RecordingExportPlan Make(RecordingFormat format, Mp4Quality mp4Quality, GifQuality gifQuality,
                                             int pixelWidth, int pixelHeight, double scaleFactor);
  }
  public static class Mp4Bitrate
  {
      public const int FramesPerSecond = 30;
      public static double? BitsPerPixelPerFrame(Mp4Quality quality);   // High: null, Medium: 0.10, Low: 0.05
      public static int? AverageBitRate(int pixelWidth, int pixelHeight, Mp4Quality quality);
  }
  ```
  - `AverageBitRate` = `(int)Math.Round((double)w * h * 30 * bpp, MidpointRounding.AwayFromZero)`, `null` for High. Compute in `double` (no `int` overflow for 8K). Throws `ArgumentOutOfRangeException` for non-positive sizes.
  - `Make(Mp4, High, …)` → `Transcode == null` (record straight to the final path, 0034 behavior). `Make(Mp4, Medium|Low, w, h, …)` → `new Mp4(AverageBitRate(w, h, q))`. `gifQuality` and `scaleFactor` don't affect an MP4 plan (`scaleFactor` is in the signature now so callers don't change in 0046).
  - `Make(Gif, …)` in this spec: the UI can't select GIF yet, but `settings.json` may contain `"gif"`. Return the MP4 plan for `mp4Quality` with `Format = Mp4` and a `// 0046` comment; 0046 replaces it.
- **New `windows/src/Snapper.Core/Recording/RecordingTempFiles.cs`**: `static (string Capture, string Output) For(string tempRoot, Guid id)` → `<tempRoot>\Snapper\Snapper-<id:N>.mp4` and `<tempRoot>\Snapper\Snapper-<id:N>-out.mp4`; `static string Directory(string tempRoot)` → `<tempRoot>\Snapper`. Pure path math (`Path.Combine`), no I/O. The Windows side passes `Path.GetTempPath()`.
- **New `windows/src/Snapper.Core/Recording/TranscodeProgress.cs`**: pure bookkeeping for the read/write loop so its end conditions are testable without Media Foundation:
  ```csharp
  public enum TranscodeStream { Video, Audio }
  public enum TranscodeStep { WriteSample, SendStreamTick, Continue, Finish }
  public sealed class TranscodeProgress
  {
      public TranscodeProgress(bool hasAudio);
      public TranscodeStep OnRead(TranscodeStream stream, bool endOfStream, bool streamTick, bool hasSample);
      public int VideoFramesWritten { get; }
      public string? FailureMessage { get; }   // non-null after Finish when no video frame was written
  }
  ```
  Rules: a read with a sample → `WriteSample` (counts video frames); a stream tick (gap) → `SendStreamTick`; end-of-stream marks that stream done; once video (and audio, if present) are done → `Finish`; otherwise `Continue`. Further reads after `Finish` → `Finish`. On `Finish` with `VideoFramesWritten == 0`, `FailureMessage` = "The recording has no video frames." (the transcoder throws it → conversion failure → original saved).
- **`windows/src/Snapper.Core/Recording/RecordingLifecycle.cs`** (from 0034) gains the conversion stage exactly as macOS 0023 specifies, keeping today's behavior bit-for-bit when nothing converts:
  - `RecordingEvent.Start(string path, bool converts)`; existing call sites pass `converts: false`. `path` is the capture file (the temp file when converting).
  - New phase `Converting`. New events `ConversionFinished(string finalPath)` and `ConversionFailed(RecordingFailure failure, string? savedOriginal)`. New effects `ReleaseCapture` (everything `Ended` does today *except* ending the app-level recording: stop the stop-timeout timer, release WGC/sink writer/microphone, unsubscribe the display watcher and window tracker, close the pill and border if still open), `Convert(string path)`, `SaveOriginal(string path)` (move a capture file into the save folder as `.mp4`, then reveal it).
  - When `converts == true`:
    - Normal finish (the `(Recording, OutputFinished)` and `(Stopping, OutputFinished)` branches without cancel or stream failure) → phase `Converting`, effects `[StopCapture?, ReleaseCapture, Convert(path)]` (`StopCapture` exactly where today's branch has it, i.e. only for the spontaneous `(Recording, OutputFinished)` case). No `Reveal`, no `Ended`.
    - `(Converting, ConversionFinished(final))` → `Finished(final)`, `[DeleteFile(path), Reveal(final), Ended]`.
    - `(Converting, ConversionFailed(f, saved))` → `saved != null`: `Finished(saved)`, `[Reveal(saved), ShowError(f), Ended]`; else `Failed(f)`, `[DeleteFile(path), ShowError(f), Ended]`.
    - The failure resolver with `fileHasContent == true` → `[SaveOriginal(path), ShowError(failure), Ended]` instead of `Reveal(path)`. Cancel paths still emit `DeleteFile(path)`.
    - `StopRequested`, `CancelRequested`, `StopTimedOut`, display/window/stream/output events during `Converting` → `[]`, phase unchanged.
  - The terminal-phase guard stays: `Finished`/`Failed`/`Cancelled` ignore everything.
  - `bool IsConverting => Phase is Converting` (used by the session for logging/diagnostics only; routing relies on the ignored events).
- **`RecordingFailure`** (0034, Core): add `string Title` — default whatever 0034 shows today for recording failures (macOS: "Couldn't record the screen"; keep 0034's string) — and `static RecordingFailure Compression(string message)` with Title **"Couldn't compress the recording"**. 0046 adds the GIF title.
- **New `windows/src/Snapper.Core/Settings/RecordingSettingsPresentation.cs`** (keeps the XAML dumb; 0046 extends it with Format and the microphone note): `IReadOnlyList<(Mp4Quality Value, string Title)> Mp4QualityOptions` → High / Medium / Low; `const string Mp4QualityCaption = "Medium and Low re-encode after you stop, for smaller files."`. If 0027 already provides a `Title` for `Mp4Quality`, reuse it.

### Platform glue — `Snapper.Windows` (manually verified)

- **New `windows/src/Snapper.Windows/Recording/Mp4Transcoder.cs`**: `static Task TranscodeAsync(string input, string output, int averageBitRate, CancellationToken token)`, run with `Task.Run` (thread-pool = MTA; never on the dispatcher). Media Foundation through CsWin32; `MFStartup` is already done app-wide by 0034 (if 0034 starts MF per session instead, call `MFStartup`/`MFShutdown` around the transcode). All COM objects released in `finally`.
  - **Reader:** `MFCreateSourceReaderFromURL(input, attrs)` with `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = TRUE` and `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = TRUE`. Select only the first video and (if present) first audio stream. Video: `SetCurrentMediaType` to `MFMediaType_Video` / `MFVideoFormat_NV12` (decoded). Audio: leave the native type (compressed AAC) so samples come out undecoded. Read the source frame size from the video's current type (`MF_MT_FRAME_SIZE`).
  - **Writer:** `MFCreateSinkWriterFromURL(output, null, attrs)` with `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = TRUE`, `MF_SINK_WRITER_DISABLE_THROTTLING = TRUE` (offline transcode, run faster than real time), and `MF_TRANSCODE_CONTAINERTYPE = MFTranscodeContainerType_MPEG4`.
    - Video output type: `MFMediaType_Video`, `MFVideoFormat_H264`, `MF_MT_FRAME_SIZE` = the source size (unchanged), `MF_MT_FRAME_RATE` = 30/1 (nominal; samples keep their own times), `MF_MT_PIXEL_ASPECT_RATIO` 1:1, `MF_MT_INTERLACE_MODE` = `MFVideoInterlace_Progressive`, `MF_MT_AVG_BITRATE` = `averageBitRate`, `MF_MT_MPEG2_PROFILE` = `eAVEncH264VProfile_High`. Input type = the reader's NV12 type. Pass encoder parameters as the `SetInputMediaType` attributes: `CODECAPI_AVEncCommonRateControlMode = eAVEncCommonRateControlMode_UnconstrainedVBR`, `CODECAPI_AVEncCommonMeanBitRate = averageBitRate`.
    - Audio (if present): `AddStream` with a copy of the reader's **native** audio type (`MFCreateMediaType` + `CopyAllItems`) and **no** `SetInputMediaType` → passthrough, no re-encode.
  - **Loop:** synchronous `ReadSample(MF_SOURCE_READER_ANY_STREAM, …)`; feed each result to `TranscodeProgress.OnRead` and act on its step: `WriteSample` → `WriteSample(writerStream, sample)` **with the source sample time and duration unchanged** (WGC output is variable frame rate; don't retime); `SendStreamTick` → `SendStreamTick(writerStream, timestamp)`; `Finish` → `Finalize()`. Check `token` between samples; on cancellation or any HRESULT failure, throw (the partial `-out` file is deleted by the caller). If `TranscodeProgress.FailureMessage` is set, throw `InvalidOperationException(FailureMessage)` instead of finalizing.
- **`Recording/RecordingSession.cs`** (0034):
  - Constructor takes the `RecordingExportPlan` inputs (format and both qualities, snapshotted from `AppSettings` once at start like the microphone inputs from 0037) and `finalPath` (the reserved save-folder path), plus the start time for re-resolving names.
  - The plan is built with `RecordingExportPlan.Make` once the output pixel size is known: rectangle → `RecordingConfiguration` output size (0034, even dims) and the monitor scale; window → the fixed output size from 0036's `LetterboxFit`/window config and the window's monitor scale. When `plan.Transcode == null`, capture path = `finalPath` (unchanged 0034 behavior). Otherwise create `RecordingTempFiles.Directory(Path.GetTempPath())` and capture to `RecordingTempFiles.For(...).Capture`.
  - `Start` sends `Start(capturePath, plan.Converts)`.
  - Split today's `Ended` handling into `ReleaseCapture` (all teardown except ending the app recording state) and `Ended` (runs the `ReleaseCapture` work if not done yet, then removes the shutdown block reason and invokes the session's `onEnded` callback, which 0034 uses to return `AppModeMachine` to idle and resolve `RecordingTerminationDecision`).
  - `Convert(path)`: run `Mp4Transcoder.TranscodeAsync(path, tempOutput, bitrate, token)` on the thread pool. On success, `File.Move(tempOutput, finalPath)`; if `finalPath` exists by then, re-resolve with the 0026 unique-path helper for the start time (` (2)` suffix). The move can cross volumes (`%TEMP%` on C:, save folder elsewhere); `File.Move` copies then deletes — keep it off the dispatcher. Then marshal to the dispatcher and send `ConversionFinished(final)`. On failure: delete `tempOutput` if present, try the save-original move (below) and send `ConversionFailed(RecordingFailure.Compression(ex.Message), movedPathOrNull)` on the dispatcher.
  - `SaveOriginal(path)`: off the dispatcher, move the capture file to a unique `.mp4` path in the save folder (0026 helper; create the folder if missing per `SaveFolderPolicy`), then on the dispatcher reveal it with `ExplorerReveal`. If the move fails, reveal nothing, keep the alert, and `Debug.WriteLine` the error. When it's used from the conversion-failure path, return the moved path instead of revealing (the lifecycle's `Reveal(saved)` does that).
  - `ShowError(f)`: use `f.Title` in `Support/ErrorAlerts` instead of a hard-coded title.
  - The `CancellationToken` is cancelled only by process teardown (`App.Exit` after the termination decision has given up, if 0034 has such a path); conversion itself has no timeout and no user cancel.
- **`App/AppCoordinator.cs`** (0034): build the session with the settings snapshot; no change to the pill/stop/cancel wiring. The pill and border are dismissed on Stop by 0035, `onEnded` now fires only after conversion, so:
  - `AppModeMachine` stays in its recording mode → Ctrl+Shift+4/6 ignored, Ctrl+Shift+5 → `StopRecording` → `StopRequested` → ignored by the lifecycle.
  - The tray (0025/0034) stays in its recording look (red dot, "Snapper — Recording") and a tray click routes to `StopRecording` (ignored) without opening the menu. Verify nothing else happens (no second reveal, no menu).
  - `ShutdownBlocker` (0034) keeps the "Saving recording…" reason until `Ended`; `RecordingTerminationDecision` replies only on `Ended`, i.e. after the conversion.
- **Settings window, Recording tab** (`Settings/SettingsWindow`, tab added by 0037): above the *Record microphone* toggle add a **"MP4 quality"** group of three radio buttons (High / Medium / Low, from `RecordingSettingsPresentation.Mp4QualityOptions`, styled like General's Screenshot destination choice from 0033) bound to `AppSettings.Mp4Quality`, with `Mp4QualityCaption` below in secondary text. Changes save immediately like the other settings and don't affect a recording in progress. 0046 replaces this group with Format + Quality.
- **`NativeMethods.txt`**: add whatever 0034/0037 didn't already list: `MFCreateSourceReaderFromURL`, `MFCreateSinkWriterFromURL`, `MFCreateAttributes`, `MFCreateMediaType`, `IMFSourceReader`, `IMFSinkWriter`, `IMFMediaType`, `IMFSample`, `IMFAttributes`, `MF_SOURCE_READER_FLAG`, `MF_SOURCE_READER_CONSTANTS` (`MF_SOURCE_READER_ANY_STREAM`, `MF_SOURCE_READER_FIRST_VIDEO_STREAM`, `MF_SOURCE_READER_FIRST_AUDIO_STREAM`, `MF_SOURCE_READER_ALL_STREAMS`), `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS`, `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING`, `MF_SINK_WRITER_DISABLE_THROTTLING`, `MF_TRANSCODE_CONTAINERTYPE`, `MFTranscodeContainerType_MPEG4`, `MF_MT_MAJOR_TYPE`, `MF_MT_SUBTYPE`, `MF_MT_FRAME_SIZE`, `MF_MT_FRAME_RATE`, `MF_MT_PIXEL_ASPECT_RATIO`, `MF_MT_INTERLACE_MODE`, `MF_MT_AVG_BITRATE`, `MF_MT_MPEG2_PROFILE`, `MFMediaType_Video`, `MFMediaType_Audio`, `MFVideoFormat_H264`, `MFVideoFormat_NV12`, `MFVideoInterlaceMode`, `eAVEncH264VProfile`, `CODECAPI_AVEncCommonRateControlMode`, `CODECAPI_AVEncCommonMeanBitRate`, `eAVEncCommonRateControlMode`. If CsWin32 doesn't emit a GUID constant, define it in `Interop/MediaFoundationGuids.cs` with a comment citing the SDK header.

### Docs

- No PRD or AGENTS.md change: PRD §7 item 6, §10, §13.3 and §13.6 already describe this behavior.

## Acceptance criteria

- [ ] Settings → Recording shows **MP4 quality** with High / Medium / Low and the caption; default High; the choice persists in `%APPDATA%\Snapper\settings.json` across restarts.
- [ ] With High, recording behaves exactly as before (written directly to the save folder, no conversion, same Explorer reveal, nothing in `%TEMP%\Snapper`).
- [ ] With Medium or Low, Stop produces `Recording <start>.mp4` in the save folder with the same pixel dimensions and duration as a High recording of the same area, smaller than High; Low is smaller than Medium.
- [ ] Microphone audio, when recorded, survives the re-encode in sync and is not re-encoded.
- [ ] While converting: pill and border are gone, the tray shows the red dot and "Snapper — Recording", clicking the tray icon or pressing Ctrl+Shift+5 does nothing (no menu, no second file), Ctrl+Shift+4/6 are ignored; when done, File Explorer selects the file and the tray icon returns to normal.
- [ ] Signing out or restarting during a conversion shows Snapper with "Saving recording…" on the Windows shutdown screen until the conversion finishes and the file is saved.
- [ ] Cancel with Medium/Low leaves no file in the save folder or in `%TEMP%\Snapper`.
- [ ] A conversion failure saves the original MP4 in the save folder as `Recording … .mp4`, reveals it, and shows "Couldn't compress the recording" with the error.
- [ ] A write failure mid-recording with content (Medium/Low) saves the partial MP4 to the save folder unconverted, reveals it, and shows the recording error.
- [ ] No temp files remain after success, failure, or cancel.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed.

## Edge cases

- **Window recordings** (0036): the bitrate uses the fixed output pixel size (letterboxed frame), not the window's current extended frame bounds.
- **Mixed DPI:** the bitrate depends only on output pixels. A 1000×500 DIP rectangle on a 150% monitor records 1500×750 px and gets `1500·750·30·0.10 = 3_375_000` bits/s at Medium; the same DIP size on a 100% monitor gets `1_500_000`. A recording on a negative-origin monitor is unaffected (no geometry here).
- **Very short or static recordings:** WGC delivers frames only on change; a recording can have one frame or (if the writer got none) zero. One frame must transcode cleanly; zero video frames → `TranscodeProgress.FailureMessage` → conversion failure → original saved (the original may itself be unplayable; it is still saved per PRD, or deleted by 0034's empty-file rule if it has no content).
- **Variable frame rate / minimized window:** keep source sample times and durations; held-last-frame gaps (§13.6) stay gaps. Stream ticks are forwarded.
- **No hardware encoder** (some VMs, Arm64 without driver): the software H.264 MFT is used; slower but correct. A hardware encoder that rejects `CODECAPI_AVEncCommonMeanBitRate` via attributes still gets `MF_MT_AVG_BITRATE`.
- **Name collision** created between start and conversion end → re-resolved with ` (2)`.
- **Save folder deleted** during conversion → recreated by the 0026 policy; **unwritable** → final move fails → treated as conversion failure; the save-original move also fails → `Failed`, alert shown, temp capture deleted. No crash, no temp files left.
- **Temp directory missing or unwritable** at start: create `%TEMP%\Snapper`; if that fails, fall back to recording directly to the final path without conversion (as High) and `Debug.WriteLine` the reason — losing compression is better than losing the recording.
- **Cross-volume move** (`%TEMP%` on C:, save folder on D: or a network share): `File.Move` copies; a failure mid-copy is a move failure as above.
- **Stop timeout** (`RecordingLifecycle.StopTimeout`, 0034) applies only to sink-writer finalization of the capture, not to the conversion.
- **Settings changed** while recording or converting: no effect on the current recording (snapshot at start).
- **Display change** during conversion: capture is already released; must not trigger a second stop.
- **Process killed** during conversion (Task Manager, forced shutdown): the temp capture stays in `%TEMP%\Snapper`. Cleaning up stale temp files at launch is out of scope (see Open questions).
- **`settings.json` contains `"gif"`** before 0046: recording proceeds as MP4 with the MP4 quality; named `.mp4`.

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`, xUnit):
  - `RecordingExportPlanTests.cs` (port of `SnapperTests/RecordingOutputPlanTests.swift`):
    - `Mp4Bitrate.AverageBitRate(2880, 1800, High) == null`; `Medium == 15_552_000`; `Low == 7_776_000`; `(1920, 1080, Medium) == 6_220_800`; `(1500, 750, Medium) == 3_375_000` (150% monitor case); `(7680, 4320, Medium) == 99_532_800` (no overflow).
    - `BitsPerPixelPerFrame`: High `null`, Medium `0.10`, Low `0.05`; `FramesPerSecond == 30`.
    - `AverageBitRate(0, 100, Medium)` throws `ArgumentOutOfRangeException`.
    - `Make(Mp4, High, …)` → `Transcode == null`, `Converts == false`, `Format == Mp4`.
    - `Make(Mp4, Medium, 1000, 500, …)` → `Transcode == new Mp4(1_500_000)`; `Low` → `new Mp4(750_000)`.
    - `gifQuality` (all three values) and `scaleFactor` (1.0, 1.5, 2.0) don't change an MP4 plan.
    - `Make(Gif, Low, …)` in this spec → `Format == Mp4`, `Transcode == new Mp4(...)` for Low (placeholder; 0046 rewrites this test).
  - `RecordingLifecycleTests.cs` (extend 0034's port): all existing cases pass with `converts: false`. New cases with `converts: true` (port of 0023's list):
    - stop → `OutputFinished` → phase `Converting`; effects contain `ReleaseCapture` and `Convert(tmp)`, **not** `Reveal` or `Ended`.
    - spontaneous `OutputFinished` while recording → `[StopCapture, ReleaseCapture, Convert(tmp)]`.
    - `Converting` + `ConversionFinished(final)` → `Finished(final)`, `[DeleteFile(tmp), Reveal(final), Ended]`.
    - `Converting` + `ConversionFailed(f, saved)` → `Finished(saved)`, `[Reveal(saved), ShowError(f), Ended]`.
    - `Converting` + `ConversionFailed(f, null)` → `Failed(f)`, `[DeleteFile(tmp), ShowError(f), Ended]`.
    - `StopRequested`, `CancelRequested`, `StopTimedOut(true)`, `StreamStopped`, `WindowStreamFailed`, `OutputFinished` during `Converting` → `[]`, phase still `Converting`, `IsConverting == true`.
    - cancel → `OutputFinished` → `Cancelled`, `[DeleteFile(tmp), Ended]`, no `Convert`.
    - `OutputFailed(f, content: true)` while recording → `[StopCapture, SaveOriginal(tmp), ShowError(f), Ended]`; `content: false` → `[StopCapture, DeleteFile(tmp), ShowError(f), Ended]`.
    - `StopTimedOut(content: true)` while stopping → `[SaveOriginal(tmp), ShowError(timeout), Ended]`.
    - window stream failure then `OutputFinished` → `[SaveOriginal(tmp), ShowError(f), Ended]` (today's path with `SaveOriginal` instead of `Reveal`).
    - after `Ended`, `ConversionFinished` again → `[]` (terminal guard).
  - `RecordingFailureTests.cs` (or inside the lifecycle tests): the default `Title` equals 0034's recording-failure title; `RecordingFailure.Compression("x").Title == "Couldn't compress the recording"` and `.Message == "x"`.
  - `RecordingTempFilesTests.cs`: `For(@"C:\Users\a\AppData\Local\Temp\", id)` → `…\Temp\Snapper\Snapper-<id:N>.mp4` and `…-out.mp4`; both under `Directory(root)`; two different GUIDs give different paths; a root without a trailing separator gives the same result. (Assert with `Path.Combine` so the test also passes on macOS/Linux runners.)
  - `TranscodeProgressTests.cs`:
    - video-only: sample → `WriteSample`; tick → `SendStreamTick`; EOS → `Finish`; `VideoFramesWritten == 1`, `FailureMessage == null`.
    - with audio: video EOS → `Continue`, audio sample → `WriteSample`, audio EOS → `Finish`; audio samples don't count as video frames.
    - zero video frames then EOS → `Finish`, `FailureMessage == "The recording has no video frames."`.
    - a read after `Finish` → `Finish`.
    - EOS read that also carries a sample → `WriteSample` counted, and the stream is marked done (next relevant check finishes).
  - `RecordingSettingsPresentationTests.cs`: `Mp4QualityOptions` is High/Medium/Low in that order with titles "High", "Medium", "Low"; `Mp4QualityCaption` equals the PRD-derived string.
  - `RecordingTerminationDecisionTests.cs`: no change needed (decision is unchanged; deferral to after conversion comes from the lifecycle emitting `Ended` later — covered above).
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed (both run on macOS).
- **Manual verification** (Windows 11, x64 and if available Arm64; can't be automated):
  1. Settings → Recording: *MP4 quality* shows High selected with the caption. Ctrl+Shift+5, select a busy area (e.g. a scrolling web page) ~10 s, Stop → Explorer selects the file immediately as before; note size. `%TEMP%\Snapper` is empty or absent.
  2. Set Medium, repeat the same recording → after a short pause Explorer selects a smaller `.mp4`. File Properties → Details shows the same frame width/height and length as step 1.
  3. Set Low, repeat → smaller than Medium.
  4. On a 150% monitor (and on a 100% one if available) record the same DIP-size area with Medium → the output has the monitor's native pixel size; both play correctly.
  5. Turn on *Record microphone*, Low, talk while recording → the audio plays in sync in Media Player; Details shows the same audio bitrate as a High recording (passthrough).
  6. Low, record ~60 s of a full 4K/high-DPI monitor, Stop, then immediately press Ctrl+Shift+4, Ctrl+Shift+5, Ctrl+Shift+6 and left- and right-click the tray icon → nothing happens, no menu opens; the tray icon is the red dot with tooltip "Snapper — Recording" until the file is revealed, then returns to the viewfinder icon.
  7. Low, start a long recording (~2 min), Stop, then immediately sign out → the Windows screen lists Snapper with "Saving recording…"; after it finishes, sign-out continues; on sign-in the file is in the save folder.
  8. Low, record, press Cancel on the pill → no new file in the save folder; `%TEMP%\Snapper` has no `Snapper-*.mp4`.
  9. Window recording (Space to window mode) with Medium → output dimensions match a High recording of the same window.
  10. Failure path: Medium, start a recording, then make the save folder read-only (Properties → Security → deny Write for your user) before Stop → after conversion the alert "Couldn't compress the recording" appears (or the recording error if the original can't be saved either); restore permissions. Alternatively set a save folder on a USB stick and remove it before Stop.
  11. Restart Snapper → the MP4 quality choice is kept.

## Resolved decisions

- **Forced session end during conversion (PRD §13.6), in scope:** on `WM_ENDSESSION` with `wParam == TRUE` while converting, cancel the transcode, delete the partial output, and move the original temp MP4 into the save folder as `Recording … .mp4` (the recording's start time, collision rule from 0026). Add a pure decision with tests: converting + forced end → `CancelAndSaveOriginal`; not converting → existing behavior.
- **Leftover temp recordings at launch (PRD §13.6), in scope:** at startup, scan `%TEMP%\Snapper`. Non-empty `Snapper-<id>.mp4` capture files (not `-out` outputs) move into the save folder as `Recording <file creation time>.mp4` (FileNamer and the collision rule); empty files and `-out` files are deleted. The plan comes from a pure `RecordingTempFiles.RecoveryPlan(entries, now)` with tests (non-empty capture → move with its name, empty → delete, `-out` → delete, an unrelated file name → ignored). The glue does the I/O, ignores per-file errors, and shows no UI. Manual check: kill Snapper mid-recording in Task Manager, relaunch, and the recording appears in the save folder.

## Out of scope

- GIF format, Format/GIF quality pickers, microphone gating for GIF, GIF naming, and the WIC GIF encoder — **0046-windows-recording-gif-format** (which reuses `RecordingExportPlan`, the `Converting` lifecycle stage, `RecordingTempFiles`, and the save-original fallback from this spec).
- Changing capture resolution, frame rate, or codec (HEVC); progress UI for the conversion; a cancel button for an in-progress conversion.
- Packaging and the full §13.11 acceptance pass — **0047-windows-packaging-release**.
