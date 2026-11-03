# 0034 — Record a selected area to an H.264 .mp4 on Windows and reveal it in File Explorer on Stop

> Source: docs/PRD.md §7 items 1, 2 (MP4 video only), 3 (errors bullet, line 103), 4, 5 (lines 93–111); §9 (lines 171–181); §12 (⌘⇧5 / Stop criteria); §13.1 (capture, encoding, excluded windows); §13.3 (tray recording icon and tooltip, lines 275–280); §13.6 (frame timing, Explorer reveal, shutdown block reason, lines 300–306); §13.11
> Depends on: 0026-windows-file-naming-save-folder, 0029-windows-global-hotkeys-app-mode, 0031-windows-screenshot-rectangle-capture (and, through them, 0025, 0027, 0028, 0030). 0032 (window selection mode) is lower-numbered and already done when this is built; this spec handles its `SelectionResult` window case with an interim rule.

## Context

Not yet implemented. After 0025–0033, Ctrl+Shift+5 (and the tray menu's *Record* item) opens the
selection overlay with `CapturePurpose.Record` (0029/0030), and the overlay produces a
`SelectionResult` (rectangle from 0030, window from 0032). `AppCoordinator.StartRecording` (0025)
is a stub, so nothing records.

This spec ports the macOS recording core (`docs/specs/done/0011-recording-core_COMPLETED.md`) to
Windows. The PRD behavior it delivers:

- §7.1: "Recording **starts immediately**, with no countdown."
- §7.2: "Capture is always H.264, native resolution, 30 fps, cursor visible. Source bounds snap to
  the display pixel grid; each output dimension rounds down to an even pixel count for H.264,
  without rescaling rectangle captures." Microphone audio is 0037. Format/quality is 0045/0046.
- §7.3 errors: "Any other failure to start or write the recording shows a short alert with the
  error. On a write failure, preserve and reveal a non-empty partial file; delete an empty file.
  Always show the error alert." There is no permission/onboarding path on Windows (§13.5, §13.10).
- §7.4: Stop saves; Ctrl+Shift+5 again or a tray click does the same; Esc does not cancel;
  Ctrl+Shift+4/6 are ignored; "in rectangle mode the recorded display is disconnected, the
  recording stops and is saved as if Stop had been pressed"; "Quitting while recording stops and
  saves the recording before the app exits."
- §7.5 / §13.6: "After saving, File Explorer opens the save folder with the new file selected."
  Recordings are never copied to the clipboard.
- §9: `Recording 2026-11-02 at 19.18.04.mp4`, named by the time the recording started, with
  ` (2)` collision suffixes.
- §13.1: `Windows.Graphics.Capture`, cursor capture **on** for recordings, `IsBorderRequired =
  false` with fallback; Media Foundation H.264 with the hardware encoder when available.
- §13.3: "While recording or converting, it is a red record dot." "The tooltip is … 'Snapper —
  Recording' while recording." "While recording, either click stops and saves the recording, and
  no menu opens."
- §13.6: "Frames are timestamped with their capture time, at most 30 fps." "Sign-out, restart, or
  shutdown while recording …: Snapper registers a shutdown block reason ('Saving recording…'),
  stops and saves the recording …, then lets the session end."

### macOS reference (port these; don't redesign)

| macOS | Windows (this spec) |
|---|---|
| `Snapper/Recording/RecordingConfigurationBuilder.swift:28-53` (`build`, `evenFloor`) | Core `Recording/RecordingConfiguration` + `RecordingConfigurationBuilder` |
| `Snapper/Recording/RecordingLifecycle.swift:4-96` | Core `Recording/RecordingLifecycle` (full port, minus permission) |
| `Snapper/Recording/RecordingTerminationDecision.swift:1-13` | Core `Recording/RecordingTerminationDecision` |
| `Snapper/Recording/RecordingStartupBuffer.swift:1-19` | **Not ported** (see "Startup ordering" below) |
| `Snapper/Recording/RecordingSession.swift:29-207` | Windows `Recording/RecordingSession`, `Recording/CaptureFrameSource`, `Recording/Mp4Writer` |
| `Snapper/App/AppCoordinator.swift:81-135, 160-193` | Windows `App/AppCoordinator` recording/exit paths |
| `SnapperTests/RecordingConfigurationBuilderTests.swift:14-86` | `RecordingConfigurationBuilderTests.cs` (re-expressed in physical pixels) |
| `SnapperTests/RecordingLifecycleTests.swift:19-187` | `RecordingLifecycleTests.cs` |
| `SnapperTests/RecordingTerminationDecisionTests.swift:5-21` | `RecordingTerminationDecisionTests.cs` |
| `SnapperTests/AppModeMachineTests.swift:48-52, 89-99` (recording rows) | already ported by 0029; verify, add if missing |

macOS facts that **don't** apply on Windows: there is no Y flip (global coordinates are top-left
physical pixels already), no Screen Recording permission, no `.permissionDenied` failure kind, no
`.showOnboarding` effect, no `SCRecordingOutput` (Snapper writes the file itself with an
`IMFSinkWriter`, so it owns frame pacing and finalization), and no app-wide capture filter (only
windows marked `WDA_EXCLUDEFROMCAPTURE` are excluded, per §13.1).

Types this spec relies on (use their actual names and signatures from the earlier specs):
`CaptureKind.RecordingMp4`, `FileNamer`, `SaveFolderPolicy` (0026, Core); `SaveFolder`,
`ExplorerReveal` (0026, Windows); `AppSettings`, `ISettingsStore` (0027); `PixelRect`, `RectD`,
`DisplayInfo`, `DisplayGeometry`, `WindowDisplayRule` (0028, Core); `DisplayInfoProvider`,
`DisplayChangeWatcher` (0028, Windows); `AppModeMachine`, `CapturePurpose` (0029);
`HotkeyManager` (0029); `SelectionResult` (0030); `CaptureExclusion` (0030); `Direct3D11Helper`,
`ScreenshotCapturer`, `ErrorAlerts` (0031); `TrayIconController`, `TrayIconRenderer`,
`TrayIconStyle`, `AppCoordinator` (0025).

## User story

As a Snapper user on Windows 11, I press Ctrl+Shift+5, drag a rectangle (or pick a window), and
recording starts at once. The tray icon becomes a red record dot and its tooltip says "Snapper —
Recording". When I press Ctrl+Shift+5 again or click the tray icon, the recording is saved as
`Recording <start time>.mp4` in my save folder, and File Explorer opens with the file selected.

## Scope & changes

### Pure logic — `windows/src/Snapper.Core/Recording/` (xUnit-tested, no WPF/WinRT/interop)

- **`RecordingConfiguration.cs`** — `public sealed record RecordingConfiguration` and
  `public static class RecordingConfigurationBuilder` (port of
  `RecordingConfigurationBuilder.swift:28-53`).
  - `Build(DisplayInfo display, RectD selection, bool showsCursor = true, int framesPerSecond = 30)`.
    `selection` is in **global physical pixels** (virtual-screen, top-left, y down), possibly
    fractional. If 0030's `SelectionResult` carries something else (for example DIPs of the start
    monitor), the coordinator converts it with `DisplayGeometry` before calling the builder; the
    builder never converts DIPs and never flips Y.
  - Fields: `DisplayId` (0028's `DisplayInfo` identity, e.g. the device name `\\.\DISPLAY2`),
    `SourceRect` (`PixelRect`, **monitor-local** physical pixels, i.e. relative to the monitor's
    top-left, which is the WGC monitor frame's origin), `PixelWidth`, `PixelHeight` (both even,
    ≥ 2, equal to `SourceRect` size: 1 source pixel = 1 output pixel), `FramesPerSecond` (30),
    `FrameIntervalTicks` (`10_000_000.0 / FramesPerSecond` as a `double` of 100-ns ticks),
    `ShowsCursor` (true), `VideoBitrate` (int, bits per second, see below), `Scale` (double, the
    monitor's scale factor from `DisplayInfo`, e.g. 1.5 at 150%; not used for the rectangle math,
    which is already in pixels, but carried for 0035 placement and 0036/0046 sizing),
    `FollowsWindow` (bool, always `false` here) and `ScalesToFit` (bool, always `false` here).
    0036 sets the last two for window recordings (letterboxed fixed output); define them now with
    `init` accessors so 0036 only adds a builder overload.
  - Algorithm (mirrors the Swift, with scale already applied because inputs are pixels):
    1. Clamp `selection` to `display.Bounds` (physical pixels) with `DisplayGeometry`'s clamp.
    2. Make it monitor-local: `localX = sel.X - display.Bounds.X`, `localY = sel.Y - display.Bounds.Y`.
    3. `width = EvenFloor(local.Width)`, `height = EvenFloor(local.Height)`, where
       `EvenFloor(v) = Math.Max(2, (int)Math.Floor(v) / 2 * 2)`.
    4. `x = Clamp(Round(localX), 0, display.Bounds.Width - width)`, same for `y` with heights.
       **`Round` must be `Math.Round(v, MidpointRounding.AwayFromZero)`** to match Swift's
       `.rounded()`; C#'s default banker's rounding gives different results at .5.
  - `VideoBitrate = Math.Max(1_000_000, (int)Math.Round(PixelWidth * PixelHeight * 30 * HighBitsPerPixel))`
    with `public const double HighBitsPerPixel = 0.20`. Rationale: Media Foundation needs an
    explicit average bitrate (`MF_MT_AVG_BITRATE`), whereas macOS lets `SCRecordingOutput` choose.
    0.20 bits/pixel/frame is twice the PRD's Medium factor (0.10, §7.6), so 0045's Medium and Low
    re-encodes are always smaller than High (§12). Recorded in PRD §13.6.
  - Container/codec constants exposed for the glue and asserted in tests:
    `public const string FileExtension = ".mp4"`, `VideoCodec = "H.264"` (informational).
  - Also `public PixelRect? SourceBoxForFrame(int frameWidth, int frameHeight)`: returns
    `SourceRect` when it lies fully inside a frame of that content size, otherwise `null` (the glue
    skips such frames; see Edge cases).

- **`RecordingFrameClock.cs`** — `public sealed class RecordingFrameClock` (new on Windows; macOS
  gets pacing and timing from ScreenCaptureKit). Decides which captured frames are written and
  their sample times, and the hold frame at stop. All values are 100-ns ticks on the QPC clock
  (`Direct3D11CaptureFrame.SystemRelativeTime.Ticks` and the stop timestamp from
  `Stopwatch.GetTimestamp()` converted to 100-ns ticks are on the same clock).
  - `ctor(double frameIntervalTicks, long originTicks)`. `originTicks` is the writer's timeline
    origin **T0** (`Mp4Writer.TimelineOrigin`, the QPC time in 100-ns units taken right after
    `BeginWriting`, before capture starts). All sample times are relative to T0, so 0037's audio
    stream can share the same timeline.
  - `long? Accept(long captureTicks)`: frames with `captureTicks < originTicks` are dropped. The
    first remaining frame is always accepted with sample time `captureTicks - originTicks` and sets
    `lastSlot` to its slot. Later frames: drop (`null`) if `captureTicks <= lastAcceptedTicks`
    (duplicate or out of order). Otherwise
    `slot = (long)Math.Floor((captureTicks - originTicks) / frameIntervalTicks)`; accept iff
    `slot > lastSlot`, returning the **real** sample time `captureTicks - originTicks` (PRD §13.6:
    "timestamped with their capture time, at most 30 fps"). Slot-based pacing keeps 60/120/144 Hz
    sources at 30 fps instead of collapsing to 20 fps as an "at least 33.3 ms since the last frame"
    rule would with jitter.
  - `long? FinalFrameTime(long stopTicks)`: the sample time of the **hold frame** written at stop
    so the video's duration matches the wall-clock recording length even when nothing changed on
    screen (WGC delivers no frames for a static screen, and none while the content is unchanged).
    Returns `stopTicks - originTicks` when at least one frame was accepted and
    `stopTicks - lastAcceptedTicks >= frameIntervalTicks`; otherwise `null` (no frames, or the last
    real frame is already within one interval of stop, or `stopTicks` is earlier than the last
    frame).
  - `long AcceptedCount`, `long? LastSampleTime`.
  - Sample durations: every written sample gets duration `round(frameIntervalTicks)`; the MP4
    muxer uses the timestamps, so gaps simply hold the previous frame.

- **`RecordingLifecycle.cs`** — full port of `RecordingLifecycle.swift:4-96`, as C# records:
  - `public sealed record RecordingFailure(string Message)` with
    `static RecordingFailure Timeout = new("The recording didn't finish saving in time.")` and
    `static RecordingFailure NoFrames = new("The screen didn't send any frames.")`. No `Kind`
    (no permission on Windows).
  - `RecordingPhase`: abstract record with `Idle`, `Starting`, `Recording`, `Stopping`,
    `Finished(string Path)`, `Failed(RecordingFailure)`, `Cancelled` (singletons for the
    parameterless cases so `Assert.Equal` works).
  - `RecordingEvent`: `Start(string Path)`, `CaptureStarted`, `CaptureFailedToStart(RecordingFailure)`,
    `StopRequested`, `CancelRequested`, `OutputFinished`,
    `OutputFailed(RecordingFailure, bool FileHasContent)`, `StreamStopped(RecordingFailure)`,
    `WindowStreamFailed(RecordingFailure)` (ported now so 0036 needs no lifecycle change),
    `StopTimedOut(bool FileHasContent)`.
  - `RecordingEffect`: `StartCapture`, `StopCapture`, `ScheduleStopTimeout`, `Reveal(string Path)`,
    `DeleteFile(string Path)`, `ShowError(RecordingFailure)`, `Ended`. (`showOnboarding` is not
    ported: a start failure always yields `ShowError`.)
  - `IReadOnlyList<RecordingEffect> Handle(RecordingEvent e)` with exactly the Swift transitions,
    idempotence and `resolveFailure` rules (`RecordingLifecycle.swift:40-95`).
  - `public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30)`. **Deliberate
    difference from macOS (5 s):** on Windows the timeout covers Snapper's own `IMFSinkWriter.Finalize`,
    which writes the MP4 index synchronously and can take several seconds for a long recording on
    a slow disk. Revealing before Finalize returns would show an unplayable file. 30 s only guards
    against a hung encoder.
  - `public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5)` (used by the glue to
    fail a start that produced no frame; WGC normally delivers the first frame immediately).
  - `public static bool IsRecordedDisplayGone(string recordedId, IEnumerable<string> currentIds)`
    (use 0028's identity type if it isn't `string`).

- **`RecordingTerminationDecision.cs`** — port of `RecordingTerminationDecision.swift:1-13`
  (`bool Request(bool recordingActive)`, `bool RecordingEnded()`), plus
  `public const string ShutdownBlockReason = "Saving recording…"` (PRD §13.6, with the real
  ellipsis character).

- **Startup ordering (`RecordingStartupBuffer`) is not ported.** On macOS it exists because SCK
  delegate callbacks can race the `async startCapture()` return. On Windows every lifecycle event
  is produced on one of two threads and posted to the WPF dispatcher with
  `Dispatcher.BeginInvoke` at the same priority: the writer thread posts `CaptureStarted` (after
  the first sample is written) **before** any later `OutputFinished`/`OutputFailed`, so dispatcher
  FIFO order already guarantees the macOS ordering. Events from other threads (`Closed`, display
  change) map to `StopRequested`, which the lifecycle defers while `Starting` (`pendingStop`).
  Put a one-line comment in `RecordingSession` stating this.

- **`App/AppModeMachine`** (0029): 0029 ports the full machine including the recording rows. Verify
  that `AppModeMachineTests.cs` contains the ports of `AppModeMachineTests.swift:48-52` and
  `:89-99` (Record shortcut and both tray clicks ⇒ `StopRecording` with mode still `Recording`;
  Screenshot/Annotate shortcuts ignored; `RecordingEnded` ⇒ `Idle`). Add whatever is missing,
  without changing behavior.

- **`App/TrayIconStyle`** (0025): if it doesn't already provide the tooltip text, add
  `Tooltip(bool recording)` ⇒ `"Snapper"` / `"Snapper — Recording"` (em dash) with a test.

### Platform glue — `windows/src/Snapper.Windows/` (manually verified)

- **`Interop/MediaFoundationRuntime.cs`** — `static` helper: `EnsureStarted()` calls
  `MFStartup(MF_VERSION, MFSTARTUP_FULL)` once (thread-safe, lazy); `MFShutdown()` on app exit
  (`App.OnExit`). `MF_VERSION` is `(MF_SDK_VERSION << 16) | MF_API_VERSION` = `0x00020070`.
  Helpers for the `MFSetAttributeSize`/`MFSetAttributeRatio` macros (pack two `uint`s into a
  `ulong`, high word first, then `IMFAttributes.SetUINT64`).

- **`Interop/Direct3D11Helper`** (0031): add an option to create the D3D11 device with
  `D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT` (needed by the Media
  Foundation DXGI device manager), and enable `ID3D10Multithread.SetMultithreadProtected(true)` on
  recording devices, because the frame thread and the writer thread both use the immediate
  context. In addition, **every use of the immediate context** (`CopySubresourceRegion`,
  `CopyResource`, `Map`/`Unmap` in the CPU fallback) is wrapped in one `lock` on a shared
  `object ContextLock` owned by the recording device wrapper, so the frame thread and the writer
  thread never interleave context calls (Media Foundation's own use is covered by the multithread
  protection). Screenshot behavior is unchanged.

- **`Recording/Mp4Writer.cs`** — `sealed class Mp4Writer : IDisposable`, wraps one
  `IMFSinkWriter`. **All Media Foundation calls run on one dedicated writer thread** (a `Thread`
  in the MTA, `IsBackground = false` so a pending finalize isn't killed by a normal exit), fed by a
  bounded queue; nothing here blocks the UI thread.
  - Creation (on the writer thread): `MediaFoundationRuntime.EnsureStarted()`;
    `MFCreateDXGIDeviceManager` + `ResetDevice(device)`; attributes
    `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = 1`, `MF_SINK_WRITER_D3D_MANAGER = manager`,
    `MF_TRANSCODE_CONTAINERTYPE = MFTranscodeContainerType_MPEG4`;
    `MFCreateSinkWriterFromURL(path, null, attributes)` (this creates the file at the final path).
  - Output type: `MFMediaType_Video` / `MFVideoFormat_H264`, `MF_MT_FRAME_SIZE` (W×H),
    `MF_MT_FRAME_RATE` 30/1, `MF_MT_PIXEL_ASPECT_RATIO` 1/1, `MF_MT_INTERLACE_MODE` progressive,
    `MF_MT_AVG_BITRATE = configuration.VideoBitrate`, `MF_MT_MPEG2_PROFILE =
    eAVEncH264VProfile_High`, `MF_MT_YUV_MATRIX = MFVideoTransferMatrix_BT709`.
  - Input type: `MFVideoFormat_ARGB32` for the GPU path, matching WGC's
    `DXGI_FORMAT_B8G8R8A8_UNORM` textures; `MFVideoFormat_RGB32` for CPU memory buffers.
    Both carry the same size/rate/aspect/interlace and `MF_MT_DEFAULT_STRIDE = width × 4`
    (top-down). The allocator's probe texture is validated before capture starts. The sink
    writer inserts RGB→NV12 conversion (the hardware video processor when available).
    `BeginWriting()`.
    **Implementation correction:** RGB32 maps to X8R8G8B8 rather than A8R8G8B8 in the
    [Windows SDK definitions](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/mfapi.h).
    [D3D11 copies require compatible formats](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copysubresourceregion),
    so using RGB32 for allocated GPU textures could produce an invalid copy. This technical
    correction changes no user-facing behavior.
  - **Two-phase setup so 0037 can add audio:** `Create(...)` configures the video stream only and
    does **not** call `BeginWriting`; 0037 calls an `AddAudioStream(...)` method (added by 0037)
    in between; then `BeginWriting()` starts writing and sets `TimelineOrigin` (`long`, QPC time in
    100-ns units, read immediately after `BeginWriting` returns). The session creates the
    `RecordingFrameClock` with that origin and only then starts capture.
  - **One lock serializes every `IMFSinkWriter` call** (`WriteSample`, `SendStreamTick`,
    `Finalize`, release), because 0037's audio thread will write samples concurrently with the
    video writer thread. Video writes still go through the writer thread's queue; the lock is what
    makes a second producer safe.
  - Samples: `MFCreateVideoSampleAllocatorEx` initialized with the input type and the device
    manager (initial 4, maximum 8 samples), with allocator attributes
    `MF_SA_D3D11_BINDFLAGS = D3D11_BIND_RENDER_TARGET` (output textures must be render-target
    bindable for the encoder's video processor) and `MF_SA_D3D11_USAGE = D3D11_USAGE_DEFAULT`. For each accepted frame the frame thread calls
    `TryAllocateSample()`; `MF_E_SAMPLEALLOCATOR_EMPTY` means the encoder is behind, so the frame
    is **dropped** (backpressure, never blocks capture). The allocated sample's
    `IMFDXGIBuffer.GetResource` texture receives the crop via `CopySubresourceRegion` (see
    `CaptureFrameSource`), then `Enqueue(sample, sampleTime)`; the writer thread sets
    `SetSampleTime`/`SetSampleDuration` and calls `WriteSample(streamIndex, sample)`. The writer
    keeps a reference to the **last written** sample's texture for the hold frame.
  - **CPU fallback:** if any step of the D3D-manager path fails (device manager, hardware MFT,
    `SetInputMediaType`, `BeginWriting`, or the allocator), dispose that writer, delete the file it
    created, and recreate the writer once **without** `MF_SINK_WRITER_D3D_MANAGER` and with
    hardware transforms off. In this mode the frame thread copies the crop into a reusable
    `D3D11_USAGE_STAGING` texture, maps it, and copies rows into an `MFCreateMemoryBuffer` sample.
    If the fallback also fails, creation fails (start failure).
  - `RequestFinish(long? holdFrameSampleTime)`: the writer thread drains the queue, then (if
    `holdFrameSampleTime` has a value) allocates one more sample (retrying for up to 1 s while the
    allocator is empty), `CopyResource`s the last frame into it and writes it at that time, then
    `Finalize()`, releases the sink writer (closing the file), and reports `Finished`. Called at
    most once; later calls are no-ops.
  - `Abort()`: stop accepting, release the sink writer **without** `Finalize`, close the file.
  - Callbacks (raised on the writer thread; the session marshals them): `FirstSampleWritten`,
    `Finished`, `Failed(RecordingFailure failure, bool fileHasContent)`. A failed `WriteSample`
    (e.g. disk full) stops further writes, makes a best-effort `Finalize()`, releases the writer,
    and raises `Failed` with the HRESULT message (`Marshal.GetExceptionForHR(hr)?.Message`).
    A failed `Finalize()` during a normal finish also raises `Failed`.
    `fileHasContent = samplesWritten > 0 && new FileInfo(path).Length > 0`.
  - A `ManualResetEventSlim Completed` set when the writer has released the file (used by
    session-end handling).
  - Design the type so 0037 can add an audio stream (keep the video stream index a field; don't
    hard-code index 0 in `WriteSample` calls).

- **`Recording/CaptureFrameSource.cs`** — `sealed class CaptureFrameSource : IDisposable`, the WGC
  side for a **monitor** item (0036 adds a window path).
  - `GraphicsCaptureItem` for the monitor via `IGraphicsCaptureItemInterop.CreateForMonitor`
    (reuse 0031's creation code; if 0031 inlined it in `ScreenshotCapturer`, extract a shared
    internal helper rather than duplicating it).
  - `Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized,
    2, item.Size)`; `GraphicsCaptureSession` with `IsCursorCaptureEnabled = true` and
    `IsBorderRequired = false` (wrapped in try/catch: if Windows refuses, continue with the border,
    §13.1; same rule as 0031).
  - `FrameArrived` (free-threaded, **not** the UI thread): `using var frame = TryGetNextFrame()`;
    `clock.Accept(frame.SystemRelativeTime.Ticks)` (under a lock shared with `Stop`); if accepted,
    `configuration.SourceBoxForFrame(frame.ContentSize)`; if `null`, skip; otherwise allocate a
    writer sample (drop on empty), `CopySubresourceRegion(dest, destSubresource, 0, 0, 0,
    frameTexture, 0, box)` with `box` = `SourceRect` (left, top, right, bottom, front 0, back 1),
    and `Enqueue`. Never touch UI here. Never hold the frame past the handler (the pool has 2
    buffers).
  - `item.Closed` ⇒ raise `SourceClosed` (marshalled by the session).
  - `long? Stop(long stopTicks)`: under the lock, set `stopped` (later `FrameArrived` calls return
    immediately), unsubscribe, dispose the session and pool, and return
    `clock.FinalFrameTime(stopTicks)`.

- **`Recording/RecordingSession.cs`** — `sealed class RecordingSession`, the dispatcher-thread
  facade the coordinator talks to (port of `RecordingSession.swift:29-207`).
  - `ctor(RecordingConfiguration configuration, DisplayInfo display, string outputPath,
    Action onStarted, Action onStopping, Action onEnded)`. `onStarted` (phase reached `Recording`)
    and `onStopping` (phase left `Starting`/`Recording`) are the hooks 0035 uses to show and dismiss
    the pill and border; in this spec the coordinator passes no-ops for them.
  - `Start()` ⇒ `Handle(new Start(outputPath))`; `Stop()` ⇒ `StopRequested`; `Cancel()` ⇒
    `CancelRequested` (no UI caller until 0035; the API and lifecycle path live here).
  - **`Stop()` is idempotent**: repeated calls (shortcut + tray click + display disconnect) are
    ignored by the lifecycle after the first, and calling it after `Ended` is a no-op. 0045 adds a
    converting phase after the writer finishes; `Stop()` must remain a no-op then too (0045 extends
    this; no new `AppMode` is added — the machine stays in `Recording` until `RecordingEnded`, as on
    macOS, `AppModeMachineTests.swift:89-99`).
  - **`Cancel()` discards**: the lifecycle path is `CancelRequested` ⇒ `StopCapture`,
    `ScheduleStopTimeout` ⇒ (`OutputFinished`, `OutputFailed` or `StopTimedOut`) ⇒
    `[DeleteFile, Ended]`: the file is deleted, with **no reveal and no alert**, whatever the
    writer's outcome. Cancel while `Starting` is deferred and wins over a pending stop.
  - The three callbacks `onStarted`, `onStopping`, `onEnded` are the session's public contract for
    0035 (pill/border), 0036 (window tracker) and 0045 (converting state).
  - Effects executor (all on the dispatcher thread):
    - `StartCapture`: subscribe to `DisplayChangeWatcher`; start a `DispatcherTimer` for
      `RecordingLifecycle.StartTimeout`; then on a background task create the D3D device, the
      `Mp4Writer` (`Create`, then `BeginWriting`), the `RecordingFrameClock` with
      `writer.TimelineOrigin`, and the `CaptureFrameSource`, and only then call `StartCapture()`
      (so no frame can predate T0). Any exception ⇒ post
      `CaptureFailedToStart(new RecordingFailure(message))`. `FirstSampleWritten` ⇒ post
      `CaptureStarted` and stop the start timer. Start timer fires while still `Starting` ⇒
      `CaptureFailedToStart(RecordingFailure.NoFrames)`.
    - `StopCapture`: `stopTicks` = now on the QPC clock
      (`Stopwatch.GetTimestamp() * 10_000_000 / Stopwatch.Frequency`, computed with
      `Math.BigMul`/`decimal` or `TimeSpan` arithmetic so it doesn't overflow);
      `hold = frameSource.Stop(stopTicks)`; `writer.RequestFinish(hold)`.
    - `ScheduleStopTimeout`: `DispatcherTimer` for `RecordingLifecycle.StopTimeout` ⇒
      `StopTimedOut(fileHasContent)`.
    - `Reveal(path)`: `ExplorerReveal` (0026).
    - `DeleteFile(path)`: dispose the frame source and `Abort()` the writer first (the file must be
      closed), then `File.Delete`; ignore `FileNotFoundException`/`DirectoryNotFoundException`, log
      other errors (`Debug.WriteLine`/trace; no alert).
    - `ShowError(f)`: `ErrorAlerts` (0031) with title **"Couldn't record the screen"** and
      `f.Message` as the text.
    - `Ended`: stop timers, unsubscribe the display watcher, dispose the frame source, the writer
      and the device, then `onEnded()`.
  - Writer callbacks and `SourceClosed` arrive on other threads and are posted with
    `Dispatcher.BeginInvoke` (default `Normal` priority, never `Invoke`). `Finished` ⇒
    `OutputFinished`; `Failed(f, content)` ⇒ `OutputFailed(f, content)`; `SourceClosed` ⇒ `Stop()`
    (the monitor went away: stop and save, §7.4).
  - Display disconnect: on `DisplayChangeWatcher` notification, read `DisplayInfoProvider`'s current
    displays; if `RecordingLifecycle.IsRecordedDisplayGone(configuration.DisplayId, ids)` ⇒ `Stop()`.
    Also `Stop()` (rectangle mode only) when the recorded monitor is still present but its `Bounds`
    or `Dpi` changed versus the snapshot taken at start (PRD §13.6; add a pure
    `RecordingLifecycle.IsRecordedDisplayChanged(start, current)` with tests: same → false,
    bounds changed → true, DPI changed → true, work area only → false, gone → true).
    Whichever of this and `item.Closed` comes first wins; the lifecycle ignores the second.
  - After every `Handle`, if the phase is neither `Starting` nor `Recording`, call `onStopping()`
    before running the effects (same as `RecordingSession.swift:86-87`, so 0035 dismisses the pill
    before alerts or the Explorer reveal).
  - `bool IsActive` (session exists and hasn't ended) and `WaitForWriter(TimeSpan)` (waits on
    `Mp4Writer.Completed`; used only by `WM_ENDSESSION`).

- **`Recording/ShutdownBlocker.cs`** — `sealed class ShutdownBlocker : IDisposable`, owns a
  top-level helper window for session-end messages (shared later by 0043's "Unsaved annotations").
  - A WPF/`HwndSource` window that is **top-level and shown**, 1×1 px, positioned off-screen with
    `SetWindowPos` (e.g. −32000, −32000), styles `WS_POPUP`, ex-styles `WS_EX_TOOLWINDOW |
    WS_EX_NOACTIVATE | WS_EX_LAYERED` with alpha 0; registered with `CaptureExclusion` (0030) so
    window hit-testing skips it. (A message-only `HWND_MESSAGE` window can't hold a block reason,
    and a hidden window may not be listed on the shutdown screen.) Created at app start by the
    coordinator.
  - `SetReason(string? reason)`: `ShutdownBlockReasonCreate(hwnd, reason)` or
    `ShutdownBlockReasonDestroy(hwnd)` for `null`.
  - Hooks `WM_QUERYENDSESSION`: calls `Func<bool> canEndSession` (from the coordinator); returns
    `TRUE`/`FALSE` accordingly (`handled = true`).
  - Hooks `WM_ENDSESSION`: if `wParam` is `TRUE` (the session really ends), calls
    `Action sessionEnding` (from the coordinator) synchronously before returning.

- **`App/AppCoordinator.cs`** (extend 0025's stub; port of `AppCoordinator.swift:81-135, 160-193`):
  - On a `SelectionResult` with purpose `Record`:
    - Rectangle `(display, rect)` ⇒ `RecordingConfigurationBuilder.Build(display, rect)`.
    - Window (0032) ⇒ **interim, until 0036:** take the window's extended frame bounds (physical
      pixels), pick its display with `WindowDisplayRule` (largest overlap), clamp the frame to that
      display, and record it **as a fixed rectangle** through the same path as a rectangle. It
      doesn't follow the window, and covering windows appear in the video. Mark it with a
      `// 0036:` comment. If no display overlaps (window off-screen), show "Couldn't record the
      screen" and return to idle.
  - The window case is the sibling contract for 0032/0036: the coordinator accepts **any**
    `SelectionResult` for `Record`; nothing is rejected before 0036.
  - Order: the overlay is already gone (0030 dismisses it before delivering the result). Capture
    `startDate = DateTime.Now` at that moment ⇒ resolve the save folder with `SaveFolder`/
    `SaveFolderPolicy` (create if missing, writability check) ⇒ `FileNamer` unique path for
    `CaptureKind.RecordingMp4` with extension `.mp4` and `startDate` ⇒ create the `RecordingSession`
    ⇒ machine enters `Recording` ⇒ `tray.SetRecording(true)` ⇒
    `shutdownBlocker.SetReason(RecordingTerminationDecision.ShutdownBlockReason)` ⇒ `session.Start()`.
  - Save-folder failure ⇒ no recording starts; feed `RecordingEnded` to the machine and show 0031's
    "Couldn't save to <folder>" alert with **Choose Folder…** (full path, §13.5). Choosing a folder
    updates the setting (as 0031 does); there is no automatic retry for recordings (same as macOS),
    the user presses Ctrl+Shift+5 again.
  - **Write directly to the final path** (same justification as 0011: folder errors surface
    before recording; no cross-volume move at stop; the file reserves its name; Cancel and failed
    starts delete it; a crash leaves a partial `.mp4`, which is acceptable).
  - `StopRecording()` ⇒ `session?.Stop()`; `CancelRecording()` ⇒ `session?.Cancel()` (wired to the
    pill by 0035).
  - `onEnded` ⇒ release the session, feed `RecordingEnded`, `tray.SetRecording(false)`,
    `shutdownBlocker.SetReason(null)`, then if `termination.RecordingEnded()` ⇒ finish the pending
    exit (`Application.Current.Shutdown()`).
  - `Exit()` (tray *Exit Snapper*, and any later caller such as 0043's quit flow): if
    `termination.Request(recordingActive: session is not null)` ⇒ `StopRecording()` and return
    (exit happens in `onEnded`); otherwise shut down now. (While recording the tray menu can't
    open, so this is mainly reached by programmatic exits and session end; it must still work.)
  - **The coordinator owns the app-level session-end handling** (`OnQueryEndSession()` /
    `OnEndSession()` methods passed to the single `ShutdownBlocker` it creates at startup). 0043
    and 0045 extend these methods and reuse the same blocker; they must not create another one.
    **The session-end path never calls `Exit()`**, because `Exit()` gains a confirmation prompt in
    0043 and a prompt can't be shown during shutdown. It calls `termination.Request` and
    `StopRecording()` directly, and the pending exit is completed with
    `Application.Current.Shutdown()` in `onEnded`, not through `Exit()`.
  - Session end: `canEndSession` returns `true` when no session is active; otherwise it calls
    `termination.Request(true)`, `StopRecording()`, and returns `false` (Windows then shows
    "Saving recording…" on its shutdown screen; when the save finishes, `onEnded` clears the reason
    and exits the process, which lets the session end, §13.6). `sessionEnding` (`WM_ENDSESSION`,
    the user chose *Shut down anyway* or the session is ending regardless): if a session is still
    active, call `StopRecording()` and block on `session.WaitForWriter(TimeSpan.FromSeconds(5))` so
    the MP4 is finalized before the process is killed; no reveal or alert is guaranteed then.
  - Settings snapshot: read nothing from *Format*, *MP4 quality*, *GIF quality*, or *Record
    microphone* in this spec. **Until 0045/0046 land, every recording is MP4 High, video only,
    regardless of those settings** (a `// 0045/0046:` comment marks the spot). Until 0037, no
    microphone.

- **`App/TrayIconController.cs`** (extend 0025): `SetRecording(bool recording)`. When `true`: the
  recording-dot icon from `TrayIconRenderer` (0025), tooltip `TrayIconStyle.Tooltip(true)`
  ("Snapper — Recording"), and **no menu opens** on left or right click; both clicks raise
  `StopClicked` ⇒ coordinator ⇒ `AppModeMachine` tray-click input ⇒ `StopRecording`. When `false`:
  restore the theme-aware viewfinder icon, the "Snapper" tooltip, and the menu on either click.
  Re-render on theme/DPI changes must keep the current state.

- **Hotkeys:** no `HotkeyManager` change expected. While `Recording`, 0029's machine maps
  Ctrl+Shift+5 to `StopRecording` and ignores Ctrl+Shift+4/6. There is no overlay, so Esc reaches
  the foreground app and does nothing in Snapper.

- **`NativeMethods.txt`** additions (CsWin32): `MFStartup`, `MFShutdown`,
  `MFCreateSinkWriterFromURL`, `MFCreateAttributes`, `MFCreateMediaType`,
  `MFCreateDXGIDeviceManager`, `MFCreateVideoSampleAllocatorEx`, `MFCreateMemoryBuffer`,
  `MFCreateSample`, `IMFSinkWriter`, `IMFDXGIDeviceManager`, `IMFVideoSampleAllocatorEx`,
  `IMFDXGIBuffer`, `IMFMediaBuffer`, `IMFSample`, `IMFAttributes`, `IMFMediaType`,
  `MF_MT_MAJOR_TYPE`, `MF_MT_SUBTYPE`, `MF_MT_FRAME_SIZE`, `MF_MT_FRAME_RATE`,
  `MF_MT_PIXEL_ASPECT_RATIO`, `MF_MT_INTERLACE_MODE`, `MF_MT_AVG_BITRATE`, `MF_MT_MPEG2_PROFILE`,
  `MF_MT_DEFAULT_STRIDE`, `MF_MT_YUV_MATRIX`, `MFMediaType_Video`, `MFVideoFormat_H264`,
  `MFVideoFormat_RGB32`, `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS`, `MF_SINK_WRITER_D3D_MANAGER`,
  `MF_TRANSCODE_CONTAINERTYPE`, `MFTranscodeContainerType_MPEG4`, `MF_E_SAMPLEALLOCATOR_EMPTY`,
  `MF_SA_D3D11_BINDFLAGS`, `MF_SA_D3D11_USAGE`, `D3D11_BIND_FLAG`,
  `eAVEncH264VProfile`, `MFVideoInterlaceMode`, `MFVideoTransferMatrix`, `ID3D10Multithread`,
  `ShutdownBlockReasonCreate`, `ShutdownBlockReasonDestroy`, `WM_QUERYENDSESSION`,
  `WM_ENDSESSION`, `SetLayeredWindowAttributes`. (D3D11/WGC interop such as
  `IGraphicsCaptureItemInterop`, `CopySubresourceRegion`, `CreateDirect3D11DeviceFromDXGIDevice`
  come from 0031.) If CsWin32 can't generate a GUID constant, declare it in
  `Interop/MediaFoundationRuntime.cs` with a comment citing the SDK header.

- **No PRD change in this spec.** PRD §13.6 already records the High bitrate
  (`w × h × 30 × 0.20` bps, minimum 1 Mbps) and the resolution/DPI-change rule (stop and save).

## Acceptance criteria

- [ ] Ctrl+Shift+5, then a rectangle drag: recording starts immediately (no countdown); the tray icon
      becomes a red record dot and its tooltip reads "Snapper — Recording".
- [ ] While recording, Ctrl+Shift+4, Ctrl+Shift+6, and Esc do nothing; recording continues.
- [ ] Pressing Ctrl+Shift+5 again stops the recording, writes `Recording YYYY-MM-DD at HH.MM.SS.mp4`
      (the start time) to the save folder, and File Explorer opens the folder with that file
      selected. The clipboard is unchanged.
- [ ] Left- or right-clicking the tray icon while recording does the same as Ctrl+Shift+5, and no
      menu opens. Afterwards the icon, tooltip, and menu are back to normal.
- [ ] The file is H.264 in an MP4 container, ≤ 30 fps, with the cursor visible. Its pixel size equals
      the selection's physical pixel size rounded down to even numbers (no rescaling) on 100%, 150%
      and 200% monitors.
- [ ] A recording of a static screen has the full wall-clock duration (the last frame is held until
      Stop).
- [ ] No overlay window appears in the video.
- [ ] A second recording started in the same second gets a ` (2)` suffix.
- [ ] Disconnecting the recorded monitor mid-recording stops and saves the file, with an Explorer
      reveal.
- [ ] An unwritable or uncreatable save folder shows "Couldn't save to <folder>" (full path) with
      **Choose Folder…**, and no recording starts.
- [ ] Any other start failure shows "Couldn't record the screen" with the error, deletes the file,
      and returns to idle. A write failure (e.g. disk full) keeps and reveals a non-empty partial
      file (or deletes an empty one) and always shows the alert.
- [ ] Exiting Snapper while recording (programmatic `Exit()`), and signing out/restarting/shutting
      down while recording, stops and saves the recording first; during session end Windows lists
      Snapper with "Saving recording…" until the save finishes.
- [ ] A window selection with Ctrl+Shift+5 records the window's current frame as a fixed rectangle
      on the display it overlaps most (interim until 0036).
- [ ] Encoding never blocks the UI thread (the tray stays responsive during recording and stop).
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` pass.

## Edge cases

- **Mixed DPI:** inputs are physical pixels, so the builder never multiplies by a scale; the
  150%/200% cases in the tests exist to prove fractional pixel selections (from DIP drags) snap and
  even-floor correctly.
- **Negative-origin monitors** (left of or above the primary): `SourceRect` is monitor-local and
  non-negative.
- **Drag across monitors:** 0030 confines the selection to the starting monitor; the builder still
  clamps defensively to the given display.
- **Odd monitor dimensions** lose at most one pixel row/column; the minimum output is 2×2 and stays
  inside the monitor at its right/bottom edge.
- **Midpoint rounding:** `Math.Round(…, MidpointRounding.AwayFromZero)` everywhere the Swift uses
  `.rounded()`.
- **High-refresh monitors** (60/120/144 Hz): slot pacing yields ~30 fps; timestamps stay real.
- **Static screen / nothing changes:** WGC sends no frames; the hold frame at stop gives the right
  duration. Cursor movement does produce frames (cursor capture is on).
- **Encoder behind** (large area, slow GPU): the sample allocator runs empty and frames are dropped;
  timestamps of written frames stay real, so playback speed is correct.
- **Selection larger than the encoder supports** (e.g. a full 5K/8K monitor): if the hardware
  path rejects the size, the CPU fallback is tried; if Media Foundation still refuses, it is a start
  failure with the alert. No downscaling (PRD: native resolution).
- **Recorded monitor's resolution, DPI or arrangement changes without disconnecting:** the PRD
  only covers disconnection; PRD §13.6 decides: in rectangle mode, a change to the recorded
  monitor's bounds or DPI (detected via 0028's `DisplayChangeWatcher` / `HasChanged` for that
  monitor) stops and saves exactly like a disconnect. Frames that arrive before the stop and no
  longer contain `SourceRect` are skipped.
- **Stop pressed while still starting** (fast double Ctrl+Shift+5): deferred by the lifecycle until
  the first frame is written, then applied. A start that never yields a frame fails after 5 s with
  "The screen didn't send any frames."
- **Double stop** (shortcut plus tray click, or `item.Closed` plus display-change): ignored after
  the first.
- **Late writer callbacks** after a terminal phase, or after a start timeout: ignored by the
  lifecycle; the `Ended`/`DeleteFile` executor has already disposed capture and writer.
- **Writer hung during finalize:** after 30 s, `StopTimedOut` reveals a non-empty file with the
  timeout alert, or deletes an empty one; if deletion fails because the file is still open, it's
  logged, not alerted.
- **Secure desktop** (UAC prompt, Ctrl+Alt+Del) or a locked session during recording: WGC delivers
  no new frames; the video holds the last frame. No error.
- **Save-folder setting changed mid-recording:** no effect; the path was fixed at start.
- **Settings or annotation windows** inside the recorded area **do** appear in the video on
  Windows: §13.1 excludes only overlays, the pill, and the border (macOS excludes the whole app).
- **Format set to GIF, or MP4 quality Medium/Low, or Record microphone on** before 0045/0046/0037:
  the recording is still a plain MP4 High, video only, saved as `.mp4`.
- **Yellow capture border:** if `IsBorderRequired = false` throws, recording proceeds with the
  border (it is not part of the captured frames).
- **Forced session end** (*Shut down anyway*): best effort, up to 5 s of synchronous wait for
  `Finalize` in `WM_ENDSESSION`.

## Test plan

- **Unit tests** — `windows/tests/Snapper.Core.Tests/RecordingConfigurationBuilderTests.cs`
  (ports `RecordingConfigurationBuilderTests.swift:14-86`; displays given as physical-pixel bounds
  plus scale; selection in global physical pixels):
  1. 100% monitor `(0,0,1920,1080)`, selection `(100,100,800,600)` ⇒ `SourceRect == (100,100,800,600)`,
     `PixelWidth == 800`, `PixelHeight == 600`, `DisplayId` passed through, `FramesPerSecond == 30`,
     `FrameIntervalTicks == 10_000_000.0/30`, `ShowsCursor`, `FileExtension == ".mp4"`.
  2. Odd sizes at 100%: selection `(10,10,101,51)` ⇒ `100×50`, `SourceRect == (10,10,100,50)`.
  3. 150% monitor at `(1920,0,2880,1620)`, fractional selection `(2000.75,100.5,301.5,151.5)` ⇒
     `SourceRect == (81,101,300,150)` (AwayFromZero: 100.5 ⇒ 101), sizes even.
  4. 150% monitor, full monitor selection ⇒ `SourceRect == (0,0,2880,1620)`, `2880×1620`.
  5. 200% monitor with negative origin `(-3840,-200,3840,2160)`, selection `(-3800,-150,1000,501)` ⇒
     `SourceRect == (40,50,1000,500)`.
  6. 200% full monitor `(0,0,3840,2160)` ⇒ exact `3840×2160`.
  7. Monitor above the primary `(0,-1080,1920,1080)` at 100%, full selection ⇒ `(0,0,1920,1080)`.
  8. Overhang (drag continued onto the next monitor): monitor `(0,0,1920,1080)`, selection
     `(1800,-20,400,200)` ⇒ clamped `SourceRect == (1800,0,120,180)`.
  9. Odd full monitor `(-100,200,101,51)` ⇒ `SourceRect == (0,0,100,50)`.
  10. Minimum at the edge: monitor `(0,0,1920,1080)`, selection `(1919.9,1079.9,0.1,0.1)` ⇒
      `SourceRect == (1918,1078,2,2)`.
  11. Overrides: `showsCursor: false, framesPerSecond: 60` ⇒ `!ShowsCursor`,
      `FrameIntervalTicks == 10_000_000.0/60`.
  12. `Scale` is passed through from `DisplayInfo` (1.0 / 1.5 / 2.0 in cases 1, 3, 5);
      `FollowsWindow == false`, `ScalesToFit == false`.
  13. Bitrate: `1920×1080` ⇒ `VideoBitrate == 12_441_600`; `2×2` ⇒ `1_000_000` (floor);
      `HighBitsPerPixel == 0.20` and is greater than 0.10 (Medium, §7.6).
  14. `SourceBoxForFrame`: frame `1920×1080` with `SourceRect (1800,0,120,180)` ⇒ that rect; frame
      `1280×720` ⇒ `null`.
- **Unit tests** — `RecordingFrameClockTests.cs` (interval `10_000_000/30`, origin `0` unless
  stated):
  1. Origin `1_000_000`, first frame at `1_250_000` ⇒ sample time `250_000`; `AcceptedCount == 1`;
     a frame at `900_000` (before origin) ⇒ dropped.
  2. 60 Hz arrivals (`k × 166_667` for k = 0…59) ⇒ exactly 30 accepted, sample times strictly
     increasing and equal to the real offsets.
  3. 30 Hz arrivals (`k × 333_334`) ⇒ all accepted.
  4. 144 Hz arrivals over 1 s ⇒ 30 accepted.
  5. Irregular: frames at 0, 400_000, 450_000, 700_000 ⇒ accepted 0, 400_000, 700_000; 450_000 dropped
     (same slot).
  6. Duplicate and out-of-order timestamps ⇒ dropped.
  7. Static screen: one frame at `0`, `FinalFrameTime(100_000_000)` ⇒ `100_000_000`; with origin
     `5_000_000` and one frame at `5_000_000`, `FinalFrameTime(105_000_000)` ⇒ `100_000_000`.
  8. Stop within one interval after the last frame ⇒ `FinalFrameTime == null`.
  9. No frames ⇒ `FinalFrameTime == null`; stop earlier than the last frame ⇒ `null`.
- **Unit tests** — `RecordingLifecycleTests.cs` (port every case of
  `RecordingLifecycleTests.swift:19-187` with paths instead of URLs):
  `HappyPath` (also asserts `StopTimeout == 30 s` and `StartTimeout == 5 s`),
  `SpontaneousWriterCompletionStopsTheStream`, `DoubleStopAndCancelAfterStopAreIgnored`,
  `StopWhileStarting`, `CancellationRemovesFile`, `CancellationWinsPendingStop` (both orders),
  `CancelDuringRecordingDeletesWithoutRevealOrAlert` (after `CancelRequested`, each of
  `OutputFinished`, `OutputFailed(_, true)`, `StopTimedOut(true)` yields exactly
  `[DeleteFile, Ended]`, never `Reveal`/`ShowError`),
  `FailedStartShowsError` (replaces the permission/other case: `[DeleteFile, ShowError, Ended]`,
  phase `Failed`), `FailedStartWithPendingStopStillEnds`, `StreamAlreadyStoppedWaitsForWriter`,
  `WindowStreamErrorReportsAfterFinalizing`, `DuplicateWindowClosureSavesAndRevealsOnce`,
  `WindowStreamTimeoutRetainsOriginalFailure` (true/false), `TimeoutPreservesOnlyContent`
  (true/false), `WriterFailureDuringRecording` (true/false), `WriterFailureDuringStop`
  (true/false), `CancellationDiscardsEvenOnFailureOrTimeout` (true/false),
  `TerminalCallbacksAndStopsAreIdempotent`, `IdleIgnoresCallbacksAndStops`, and
  `DisplayDisconnection` (`("\\.\DISPLAY2", ["\\.\DISPLAY1","\\.\DISPLAY2"]) == false`,
  `(…, ["\\.\DISPLAY1"]) == true`, `(…, []) == true`). Same effect lists and phases as the Swift
  assertions.
- **Unit tests** — `RecordingTerminationDecisionTests.cs`: port `IdleQuitsImmediately`,
  `ActiveRecordingDefersUntilExactlyOneReply`, `OrdinaryStopNeverRepliesToTermination`; plus
  `ShutdownBlockReason == "Saving recording…"`.
- **Unit tests** — `AppModeMachineTests.cs` (0029's file): confirm or add the recording rows
  listed above. `TrayIconStyleTests.cs`: `Tooltip(false) == "Snapper"`,
  `Tooltip(true) == "Snapper — Recording"` (if added here).
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test
  windows/Snapper.slnx` succeed (both run on macOS).
- **Manual verification** (Windows 11 machine; can't be automated):
  1. Press Ctrl+Shift+5 and drag a rectangle on a 150% monitor. Recording starts with no
     countdown; the tray icon is a red dot; hovering it shows "Snapper — Recording".
  2. While recording, press Ctrl+Shift+4, Ctrl+Shift+6 and Esc: nothing happens.
  3. Move the mouse inside the area for ~5 s, then press Ctrl+Shift+5. File Explorer opens the save
     folder with `Recording <start time>.mp4` selected; the time matches the end of your drag. The
     clipboard still holds its previous content.
  4. Play the file (Media Player / Films & TV): the cursor is visible, the content matches the area,
     no overlay is visible. File Properties ▸ Details (or `ffprobe -v error -select_streams v:0
     -show_entries stream=codec_name,width,height,avg_frame_rate <file>`) shows H.264, even width
     and height equal to the selection in physical pixels (W × H readout × 1.5, rounded down to even),
     frame rate ≤ 30.
  5. Record a static area (nothing moving, mouse outside) for 10 s: the video lasts ~10 s.
  6. Record a full 200% monitor and a full 100% monitor: the sizes equal the monitors' native
     resolutions (rounded down to even).
  7. Start another recording; left-click the tray icon: it stops and reveals, and no menu opens.
     Repeat with a right-click. Afterwards the icon and tooltip are normal and a click opens the menu.
  8. Two recordings within the same second (stop, start immediately): the second gets ` (2)`.
  9. With two monitors, record on the secondary and unplug it (or disable it in Display settings):
     the recording stops, is saved and revealed, and the tray returns to normal.
  10. In Settings choose a read-only folder (or deny write in its Security tab), press Ctrl+Shift+5
      and drag: "Couldn't save to <full path>" with **Choose Folder…** appears; no recording starts.
  11. Record to a nearly full USB drive until it fills: the alert "Couldn't record the screen"
      appears and the partial file is revealed (or deleted if empty).
  12. Start a recording and sign out (or restart): Windows' shutdown screen lists Snapper with
      "Saving recording…"; after a moment it disappears and the session ends. Sign back in: the MP4
      is in the save folder and plays to the moment you signed out.
  13. Press Ctrl+Shift+5, press Space, click a partly covered window: its frame area is recorded as a
      fixed rectangle, including what covers it (interim; 0036 follows the window).
  14. Set Format to GIF or MP4 quality to Low in Settings (if the Recording tab exists yet): the
      output is still an `.mp4` at full quality (until 0045/0046).
  15. On a machine or VM without a hardware H.264 encoder: recording still works (software path).
  16. During a recording on a 144 Hz monitor, move a window around: playback is smooth at ~30 fps
      and the duration matches real time.

## Out of scope

- Recording pill (elapsed · Stop · Cancel), dashed border, Cancel button UI: **0035** (the
  `Cancel()` API and `onStarted`/`onStopping` hooks exist here).
- Window recordings that follow the window, letterbox to a fixed size, stop when the window closes,
  and keep going while minimized: **0036**.
- Microphone audio (AAC) and the Settings Recording tab: **0037**.
- MP4 Medium/Low re-encode, the converting tray state, and its shutdown handling: **0045**.
- GIF format: **0046**.
- System audio, highlighted clicks: PRD §2 non-goals. Copying recordings to the clipboard: never
  (§7.5).
- Annotation "Unsaved annotations" shutdown block reason: **0043** (reuses `ShutdownBlocker`).

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with
**0 warnings and 0 errors**; `dotnet test windows/Snapper.slnx` passed **747 tests**, including
**62 new tests**, with no skips. The new tests port rectangle configuration (including mixed DPI,
negative origins, clamping and midpoint/even rounding), QPC frame pacing and static hold timing,
all pre-conversion lifecycle transitions, display-change decisions, deferred termination, and
collision-safe final-path reservation. Existing app-mode tests already cover all recording and tray
inputs, repeated stops and the contract for a future conversion phase.

Implementation notes: recording reserves a final file name without replacing existing entries;
WGC monitor/window item creation and borderless policy are shared with screenshots. GPU allocation
uses ARGB32 to match WGC texture format (the correction documented above), with a reusable-staging
CPU fallback. The dedicated MTA writer serializes sink calls and retains its device lease until
native release. Deletion after cancel/failed start waits for actual writer completion even when a
native timeout has already returned the app to idle. AGENTS.md records these ownership rules.
PRD behavior is unchanged.

GUI behavior has **not** been verified. All following checks need a Windows 11 GUI session:

- [ ] not yet run by user: On a 150% monitor, press Ctrl+Shift+5 and select a rectangle; confirm immediate recording, red tray dot, and “Snapper — Recording” tooltip.
- [ ] not yet run by user: During recording, press Ctrl+Shift+4, Ctrl+Shift+6 and Esc; confirm recording continues without another Snapper action.
- [ ] not yet run by user: Move the cursor within the area for about five seconds, then stop with Ctrl+Shift+5; confirm Explorer selects `Recording <start time>.mp4` and the clipboard is unchanged.
- [ ] not yet run by user: Play the MP4; verify cursor, correct content, no selection overlay, H.264, at most 30 fps, and native even pixel dimensions without rescaling.
- [ ] not yet run by user: Record an unchanged area with the cursor outside it for ten seconds; confirm playback lasts approximately ten seconds.
- [ ] not yet run by user: Record full 100% and 200% monitors; confirm native dimensions rounded down to even values.
- [ ] not yet run by user: Stop separate recordings with left and right tray clicks; confirm no menu opens, Explorer reveals each file, then the normal icon/tooltip/menu return.
- [ ] not yet run by user: Start two recordings within one second; confirm the second name has a collision suffix and the first file is preserved.
- [ ] not yet run by user: Record a secondary monitor, then disconnect/disable it; confirm save/reveal and return to idle. Also change the recorded monitor's DPI, arrangement, or resolution; confirm stop/save. A work-area-only change should not stop recording.
- [ ] not yet run by user: Choose an unwritable folder and select a recording area; confirm “Couldn't save to <full path>” with Choose Folder…, no recording starts, and choosing a folder applies to the next attempt.
- [ ] not yet run by user: Fill a nearly full removable drive during recording; confirm the recording error alert and reveal of a non-empty partial file (or deletion of an empty one).
- [ ] not yet run by user: Sign out/restart during recording; confirm Windows lists “Saving recording…” until finalization completes and the resulting MP4 plays after signing back in. Forced shutdown is best effort with a five-second writer wait.
- [ ] not yet run by user: Use Space window mode for a partly covered window; confirm the current interim behavior records its fixed rectangle, including covering content.
- [ ] not yet run by user: With GIF/Low/microphone preferences set through JSON or later Recording settings, confirm this slice still records video-only MP4 High until 0037/0045/0046.
- [ ] not yet run by user: On a machine without a usable hardware H.264 path, confirm the software fallback records correctly; verify a refused borderless request still allows capture.
- [ ] not yet run by user: Record moving content on a 144 Hz monitor; confirm roughly 30 fps playback, correct wall-clock duration, and a responsive tray during recording/finalization.
