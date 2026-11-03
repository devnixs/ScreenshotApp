# 0036 — Record a single window on Windows, with a fixed letterboxed output and a pill and border that follow it

> Source: docs/PRD.md §7 item 1 (window mode), §7 item 2 (window output size: fixed, even, min 2 × 2, letterboxed on resize/scale change), §7 item 3 (pill and border follow the window, polled about 4×/s; minimized/hidden/off-screen behavior), §7 item 4 (window closes → stop and save; follow the window after a display disconnect), §12 ("Window recordings follow the window…"), §13.4 (window frames are `DWMWA_EXTENDED_FRAME_BOUNDS`, captures cropped to them), §13.6 (scale = monitor scale factor; extended frame bounds already physical pixels; minimized → no new frames, video holds the last frame)
> Depends on: 0032-windows-window-selection-mode, 0035-windows-recording-overlay (and transitively 0028, 0031, 0034)

## Context

This is the Windows port of macOS spec `docs/specs/done/0013-window-recording_COMPLETED.md`. Read that spec and the Swift sources it produced. They are the reference implementation. Port their behavior and test cases, adapted per PRD §13:

| macOS reference | What it does | Windows counterpart in this spec |
|---|---|---|
| `Snapper/Recording/WindowTracking.swift:4-22` (`WindowPollResult`, `WindowInfoParser`) | Turns a `CGWindowListCopyWindowInfo` result into `unavailable` / `gone` / `present(bounds, isOnscreen)` | Core `WindowPollResult` + `WindowPollClassifier` (classifies a Win32 probe, see below) |
| `Snapper/Recording/WindowTracking.swift:24-62` (`WindowTrackingState`, `WindowTrackingEvent`, `WindowTrackingReducer`) | Pure state machine: `unchanged`/`moved`/`hidden`/`shown`/`closed`, closed emitted once | Core `WindowTrackingReducer` (same transitions, **no Y flip and no point rounding**: frames are integer physical pixels) |
| `Snapper/Recording/WindowFrameTracker.swift:21-43` | 0.25 s main-run-loop `Timer`, poll → parse → reduce → callback, stops itself after `.closed` | Windows `WindowFrameTracker` with a 250 ms `DispatcherTimer` |
| `Snapper/Recording/RecordingConfigurationBuilder.swift:29-36` (`evenFloor`, `buildWindow`) | Output = round(points × scale), even-floored, min 2 | Core `RecordingConfiguration` window variant: even-floor of the starting extended-frame **pixel** size (no multiplication; §13.6) |
| `SCStreamConfiguration.scalesToFit` / `preservesAspectRatio` (`RecordingConfigurationBuilder.swift:72-82`) | ScreenCaptureKit letterboxes a resized window into the fixed output | Core `LetterboxFit` (pure math) + Windows `LetterboxCompositor` (Direct2D on the capture D3D11 device) — WGC does not scale for us |
| `SCContentFilter(desktopIndependentWindow:)` (`RecordingSession.swift:164-172`) | Captures only that window, follows it, ignores covering windows | `GraphicsCaptureItem` created with `IGraphicsCaptureItemInterop.CreateForWindow(hwnd)` — follows the window natively |
| `RecordingSession.swift:73-75` (stream stopped: poll once; gone → Stop, else `windowStreamFailed`) | Close detected by the stream | `GraphicsCaptureItem.Closed` → probe once on the dispatcher; gone → Stop, else `WindowStreamFailed` |
| `RecordingSession.swift:197-205` (display-disconnect stop only for `.rectangle`) | Window recordings ignore display disconnects | Same: 0034's recorded-monitor-gone stop is skipped for window sources |
| `Snapper/App/AppCoordinator.swift:92-100, 118-123, 137-150` | Builds the window source, starts the tracker after the stream starts, routes tracker events to the overlay | Windows `AppCoordinator` recording start + `HandleWindowTracking` |
| `Snapper/Recording/RecordingOverlayController.swift:66-97` (`setBorderVisible`, `update(recordedRect:on:)`, `refreshDisplay` with `followsWindow`) | Border hide/show, pill re-placement on the window's current display, hidden pill kept reachable | 0035's `RecordingOverlayController` (extended here only if 0035 didn't port these, see Scope) |

Tests to port: `SnapperTests/WindowTrackingReducerTests.swift:8-71`, `SnapperTests/WindowInfoParserTests.swift:7-28` (re-expressed as classifier tests), `SnapperTests/RecordingConfigurationBuilderTests.swift:88-110` (`windowNativeSizeAndFixedFrameFlags`), `SnapperTests/RecordingLifecycleTests.swift:96-121` (window stream failure / duplicate closure), `SnapperTests/RecordingBorderGeometryTests.swift:16-20` and `SnapperTests/RecordingPillPlacementTests.swift:68-77` (only if 0035 didn't port them).

What lower-numbered Windows specs provide (all not yet implemented at the time of writing; names come from the batch ownership table):

- 0028: Core `Geometry/` primitives (`PixelPoint`, `PixelSize`, `PixelRect`), `DisplayInfo`, `DisplayGeometry`, and `WindowDisplayRule` (display with the largest overlap, `null` if none). Windows `Capture/DisplayInfoProvider` and `Capture/DisplayChangeWatcher`.
- 0031: Windows `Interop/Direct3D11Helper` (D3D11 device created with `D3D11_CREATE_DEVICE_BGRA_SUPPORT`, WinRT `IDirect3DDevice` wrapping, `IGraphicsCaptureItemInterop`, `IDirect3DDxgiInterfaceAccess`) and Core `Capture/CaptureCrop`.
- 0032: window selection — `SelectionResult` window case carrying the `HWND` and its extended frame bounds (`PixelRect`), the window candidate provider (`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, `DWMWA_CLOAKED`), and the WGC `CreateForWindow` screenshot that is cropped from the WGC window frame to the extended frame bounds.
- 0034: `RecordingSession` (WGC capture + `Mp4Writer`/`IMFSinkWriter`, 30 fps cap, cursor on, `IsBorderRequired = false`, frames timestamped by capture time with the last frame held), Core `RecordingConfiguration` (rectangle variant with even-floor rounding), `RecordingLifecycle`, `RecordingTerminationDecision`, `ShutdownBlocker`, Explorer reveal and error alerts. **Interim behavior before this spec:** 0034 records a window selection as a rectangle of the window's extended frame on its largest-overlap monitor. That recording doesn't follow the window and includes anything covering it. This spec replaces that path.
- 0035: `RecordingPillWindow`, `RecordingBorderWindow`, `RecordingOverlayController`, Core `RecordingPillPlacement` (with `ReachableFrame`), `RecordingBorderGeometry` (with the `followsWindow` unclipped variant), all positioned in physical pixels with `SetWindowPos` and excluded from capture with `WDA_EXCLUDEFROMCAPTURE`.

macOS facts that **don't apply** on Windows: there is no Y flip (the macOS reducer's `DisplayGeometry.appKitRect(fromCG:)` call and the "sub-point jitter" rounding disappear; frames are integer pixels); there is no `pointPixelScale` multiplication (the extended frame is already physical pixels); there is no Screen Recording permission or onboarding fallback; there are no Spaces (the equivalent is a cloaked window on another virtual desktop); and there is no `ignoreShadowsSingleWindow` flag — the shadow and invisible resize borders are removed by cropping to the extended frame bounds, as in 0032.

## User story

As a Snapper user on Windows 11, I press Ctrl+Shift+5, press Space, and click a window. Snapper records only that window, even when I move it, drag it to a monitor with a different scale, resize it, or cover it with another window. The output keeps the window's starting pixel size; if the window's size or aspect changes, its content is scaled to fit with black bars. The pill and the dashed border move with the window. If I minimize it, recording continues, the border hides, and the pill stays where it was. If I close the window, the recording is saved and File Explorer shows it, just as if I had pressed Stop.

## Scope & changes

### Pure logic — `Snapper.Core` (xUnit-tested)

- **Window output size** (`src/Snapper.Core/Recording/RecordingConfiguration.cs`, extend 0034's type; port of `RecordingConfigurationBuilder.buildWindow`, `RecordingConfigurationBuilder.swift:29-36`).
  - Add `public static RecordingConfiguration ForWindow(PixelRect startingExtendedFrame, double monitorScale)` (or an equivalently named factory next to 0034's rectangle factory — follow 0034's naming).
  - `OutputWidth = Max(2, EvenFloor(startingExtendedFrame.Width))`, `OutputHeight = Max(2, EvenFloor(startingExtendedFrame.Height))`. Reuse 0034's even-floor helper so both paths agree. The frame origin is irrelevant.
  - The configuration records `Source = Window`, `FollowsWindow = true`, `ScalesToFit = true`, `PreservesAspectRatio = true`, `CursorVisible = true`, `FrameRate = 30`, and `Scale = monitorScale` (the starting monitor's scale factor from `WindowDisplayRule`, so the later GIF spec 0046 can compute "1×" sizes as pixel size ÷ scale, PRD §7 item 6). If 0034's type has no `Scale`/source fields, add them here.
  - If 0034 defines a maximum encoder size cap, apply the same cap to window recordings, scaled down with the aspect ratio preserved and both dimensions even. If it defines none, add none (macOS has none either: `RecordingConfigurationBuilderTests.swift:95` keeps 10240 × 5760).
  - **Decision (from macOS 0013, PRD §7 item 2):** the output size is computed once, from the extended frame queried when the capture starts, and never changes. An MP4 H.264 track can't change dimensions mid-file.

- **`LetterboxFit`** (new `src/Snapper.Core/Recording/LetterboxFit.cs`). Pure fit of a source size into the fixed output, replacing ScreenCaptureKit's `scalesToFit` + `preservesAspectRatio`.
  - `public readonly record struct LetterboxPlacement(PixelRect Destination, bool IsIdentity);`
  - `public static LetterboxPlacement? Fit(PixelSize source, PixelSize output)`:
    - `source.Width <= 0 || source.Height <= 0` (or a non-positive output) → `null`; the caller drops the frame.
    - `source == output` → `Destination = (0, 0, output)`, `IsIdentity = true` (native pixels, no resampling).
    - Otherwise `scale = Min(output.Width / (double)source.Width, output.Height / (double)source.Height)` — it scales **up or down**, matching macOS `scalesToFit`. `dw = Clamp(Round(source.Width × scale, AwayFromZero), 1, output.Width)`, `dh` likewise. `x = (output.Width − dw) / 2`, `y = (output.Height − dh) / 2` (integer division). `IsIdentity = false`.
  - The area outside `Destination` is black (letterbox/pillarbox bars).

- **`WindowFrameCrop`** (new `src/Snapper.Core/Recording/WindowFrameCrop.cs`) — the part of a WGC window frame that is the extended frame (no shadow, no invisible resize borders), as 0032 does for window screenshots. **If 0032 already has a pure helper that computes exactly this (for example in `Capture/CaptureCrop`), reuse it and add the test cases below to its test file instead of creating this type.**
  - `public static PixelRect SourceRect(PixelSize contentSize, PixelRect windowRect, PixelRect extendedFrame)`, all in physical pixels. `contentSize` is the WGC frame's `ContentSize`; `windowRect` is `GetWindowRect`; `extendedFrame` is `DWMWA_EXTENDED_FRAME_BOUNDS`.
    - If `contentSize` equals the extended frame size → `(0, 0, contentSize)` (WGC already excluded the margins).
    - Otherwise: `left = extendedFrame.X − windowRect.X`, `top = extendedFrame.Y − windowRect.Y`, each clamped to `[0, content − 1]`; `width = Min(extendedFrame.Width, contentSize.Width − left)`, `height` likewise.
    - If the result has a non-positive width or height → the full content `(0, 0, contentSize)`.

- **Window polling model** (new `src/Snapper.Core/Recording/WindowTracking.cs`; port of `WindowTracking.swift`).
  - `public readonly record struct WindowProbe(bool Exists, uint ThreadId, bool IsVisible, bool IsMinimized, bool IsCloaked, PixelRect? ExtendedFrame);` — a raw snapshot that the Windows tracker fills from Win32 calls. `ExtendedFrame == null` means `DwmGetWindowAttribute` failed.
  - `public abstract record WindowPollResult` with `Unavailable`, `Gone`, and `Present(PixelRect Frame, bool IsOnscreen)` (or an equivalent closed hierarchy / enum + payload; keep value equality for tests).
  - `public static class WindowPollClassifier`, `static WindowPollResult Classify(WindowProbe probe, uint expectedThreadId, IReadOnlyList<DisplayInfo> displays)` (replaces `WindowInfoParser`, `WindowTracking.swift:10-22`):
    - `!Exists` → `Gone`. `ThreadId != expectedThreadId` → `Gone` (the `HWND` value was reused by another window after the recorded one was destroyed).
    - `ExtendedFrame == null` (transient DWM failure) → `Unavailable`. This never counts as closed.
    - Otherwise `Present(frame, isOnscreen)`, where `isOnscreen = IsVisible && !IsMinimized && !IsCloaked && frame intersects at least one display's bounds with a positive area`. A minimized window's frame (for example `(-32000, -32000, 160, 28)`) is therefore off-screen. A cloaked window (another virtual desktop, a suspended app) is off-screen, not closed.
  - `public readonly record struct WindowTrackingState(PixelRect Frame, bool IsVisible = true, bool IsClosed = false);`
  - `WindowTrackingEvent`: `Unchanged`, `Moved(PixelRect)`, `Hidden`, `Shown(PixelRect)`, `Closed`.
  - `public static class WindowTrackingReducer`, `static (WindowTrackingState, WindowTrackingEvent) Reduce(WindowTrackingState state, WindowPollResult poll)` with exactly the macOS transitions (`WindowTracking.swift:44-61`):
    - Closed state → always `Unchanged` (Closed is emitted once).
    - `Unavailable` → `Unchanged`, state unchanged.
    - `Gone` → `Closed`, `IsClosed = true`.
    - `Present(frame, onscreen)`: the state's frame becomes `frame` and `IsVisible = onscreen`. Not on-screen: `Hidden` if it was visible, else `Unchanged`. On-screen while hidden: `Shown(frame)`. Visible and the frame differs: `Moved(frame)`. Otherwise `Unchanged`.
    - No coordinate conversion and no rounding: frames are integer physical pixels (PRD §13.4).
  - **Pill display for a moving window:** use 0028's `WindowDisplayRule` (largest overlap). No overlap → the caller keeps the last display. Don't duplicate the rule.

- **Lifecycle** (`src/Snapper.Core/Recording/RecordingLifecycle.cs`, 0034). If 0034's port of `RecordingLifecycle` doesn't already have the `WindowStreamFailed(failure)` event (macOS `RecordingLifecycle.swift:19, 64-67, 74-75`), add it here exactly as on macOS: in `Recording` it records the failure, enters `Stopping`, and returns `[ScheduleStopTimeout]`; when output finishes, it returns `[Reveal(url), ShowError(failure), Ended]`; a later `StopRequested` or second failure is ignored. A duplicate stop (tracker and `Closed` both reporting) produces one reveal.

- **Overlay geometry** (0035's `RecordingBorderGeometry` and `RecordingPillPlacement`). If 0035 didn't port them, add here: `RecordingBorderGeometry` with `followsWindow: true` returns the outset window frame **without** clipping to the monitor (macOS `RecordingBorderGeometry.swift`, test `RecordingBorderGeometryTests.swift:16-20`), and `RecordingPillPlacement.ReachableFrame(frame, workArea)` (macOS `RecordingPillPlacement.swift:12-16`, test `RecordingPillPlacementTests.swift:68-77`).

### Platform glue — `Snapper.Windows` (manually verified)

- **Window capture source in `RecordingSession`** (`src/Snapper.Windows/Recording/RecordingSession.cs`, extend 0034). When the selection is a window:
  1. On the dispatcher, check `IsWindow(hwnd)`, read `GetWindowThreadProcessId(hwnd)` (the expected thread ID), and query `DWMWA_EXTENDED_FRAME_BOUNDS`. If the window is gone or the query fails, show 0034's start-failure alert and return to idle, leaving no file (PRD §7 item 3: "Any other failure to start … shows a short alert").
  2. Build the output configuration with `RecordingConfiguration.ForWindow(extendedFrame, scale)`, where `scale` is the scale factor of `WindowDisplayRule`'s display for that frame.
  3. Create the item with `IGraphicsCaptureItemInterop.CreateForWindow(hwnd)` (0031's helper). The item follows the window across monitors and isn't affected by covering windows. Snapper's pill and border are not part of it by construction (they also keep `WDA_EXCLUDEFROMCAPTURE`; add a code comment saying so).
  4. Create the frame pool with `Direct3D11CaptureFramePool.CreateFreeThreaded(device, B8G8R8A8UIntNormalized, 2, item.Size)`, a session with `IsCursorCaptureEnabled = true` and `IsBorderRequired = false` (ignore a refusal, PRD §13.1), and the same 30 fps cap and capture-time timestamps as 0034.
  5. Remove 0034's interim "window as a rectangle" path for recordings. 0034's rectangle path is unchanged.
- **Per-frame processing for window sources** (on the WGC worker thread, inside `FrameArrived`):
  1. Read `frame.ContentSize`. If either dimension is 0, drop the frame.
  2. Call `GetWindowRect(hwnd)` and `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` (both are safe from any thread). If either fails, reuse the last successful pair. Compute the crop with `WindowFrameCrop.SourceRect(contentSize clamped to the surface size, windowRect, extendedFrame)`.
  3. Compute `LetterboxFit.Fit(crop.Size, outputSize)`. `null` → drop the frame.
  4. Compose into 0034's output-size BGRA texture with the new `LetterboxCompositor` and hand it to `Mp4Writer` exactly as 0034 does for rectangle frames.
  5. If `frame.ContentSize` differs from the pool's current size, call `framePool.Recreate(device, B8G8R8A8UIntNormalized, 2, contentSize)` after the frame is disposed, so later frames aren't clipped to the old size. Never recreate with a zero size.
  - While the window is minimized WGC delivers no frames; 0034's writer holds the last frame (PRD §13.6). Nothing extra is needed here.
- **`LetterboxCompositor`** (new `src/Snapper.Windows/Recording/LetterboxCompositor.cs`, `IDisposable`):
  - **Fast path:** `placement.IsIdentity` → `ID3D11DeviceContext.CopySubresourceRegion` of the crop box into the output texture at (0, 0). This is the native-resolution path for an unresized window and must be bit-exact.
  - **Scaled path:** Direct2D on the same D3D11 device. Create once per session: an `ID2D1Factory1` (`D2D1_FACTORY_TYPE_MULTI_THREADED`), an `ID2D1Device` from the `IDXGIDevice` of 0031's D3D11 device, and an `ID2D1DeviceContext`. The output texture's `IDXGISurface` becomes the target bitmap (`D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW`, `DXGI_FORMAT_B8G8R8A8_UNORM`, `D2D1_ALPHA_MODE_IGNORE`); if 0034 created that texture without `D3D11_BIND_RENDER_TARGET`, add the flag. Per frame, wrap the frame surface's `IDXGISurface` with `CreateBitmapFromDxgiSurface` (`D2D1_ALPHA_MODE_IGNORE`, so transparent window pixels encode as opaque), then `BeginDraw`, `Clear(opaque black)`, `DrawBitmap(source, destination, 1, D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC, crop)`, `EndDraw`. Release the per-frame bitmap.
  - All use of the D3D11 immediate context and the D2D context is serialized with the lock (or `ID3D10Multithread` protection) that 0034 uses for the immediate context. A `D2DERR_RECREATE_TARGET` or device-lost error is a write failure handled by 0034's write-failure path (PRD §7 item 3).
- **`WindowFrameTracker`** (new `src/Snapper.Windows/Recording/WindowFrameTracker.cs`, `IDisposable`, dispatcher thread only; port of `WindowFrameTracker.swift`):
  - `WindowFrameTracker(HWND hwnd, uint expectedThreadId, PixelRect initialFrame, Action<WindowTrackingEvent> onEvent)`, `Start()`, `Stop()` (idempotent; `Dispose` calls `Stop`).
  - A `DispatcherTimer` with a 250 ms interval at `DispatcherPriority.Normal`. `Start()` also polls once immediately.
  - Each tick builds a `WindowProbe` from `IsWindow`, `GetWindowThreadProcessId`, `IsWindowVisible`, `IsIconic`, `DwmGetWindowAttribute(DWMWA_CLOAKED)`, and `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, classifies it with the current displays from 0028's `DisplayInfoProvider`, reduces, and forwards every event except `Unchanged`. After `Closed`, it stops its timer.
  - `static WindowPollResult PollOnce(HWND, uint expectedThreadId)` for the one-off check below.
- **`GraphicsCaptureItem.Closed`** (raised on an arbitrary thread): marshal to the dispatcher, then `PollOnce`. `Gone` → the same path as Stop (save and reveal, no alert). Anything else (the window still exists but capture ended) → `RecordingLifecycle.WindowStreamFailed`, which finalizes, reveals a non-empty file, and shows 0034's error alert. Both this and the tracker can report a close; the lifecycle makes it one save and one reveal.
- **Wiring in `AppCoordinator`** (`src/Snapper.Windows/App/AppCoordinator.cs`, extend 0034/0035; port of `AppCoordinator.swift:118-150`):
  - After the capture has started (0034's started callback), create the overlay with `followsWindow: true` and the window's frame, then create and start the `WindowFrameTracker`.
  - `Moved(frame)` / `Shown(frame)`: pick the display with `WindowDisplayRule`; if none, hide the border and keep the pill. Otherwise call the overlay's update with the new frame and that display (the pill re-places with `RecordingPillPlacement` against that monitor's work area), and show the border.
  - `Hidden`: hide only the border; the pill stays at its last position, so Stop and Cancel stay reachable.
  - `Closed`: run exactly the Stop path.
  - The tracker is stopped and disposed on every exit path: pill Stop, Ctrl+Shift+5, tray click, Cancel, window closed, Exit Snapper, shutdown (0034's `ShutdownBlocker`), and errors.
- **Overlay controller** (`src/Snapper.Windows/Recording/RecordingOverlayController.cs`, 0035). Use 0035's API. If it lacks any of the following, add them here: `SetBorderVisible(bool)`; `Update(PixelRect recordedRect, DisplayInfo display)`; an unclipped border for `followsWindow` (a single border window may span monitors with different DPIs: it is positioned in physical pixels with `SetWindowPos` and must not resize itself on `WM_DPICHANGED`); and, on a display change while following a window, refreshing the display with `WindowDisplayRule` (falling back to the primary display if the last one is gone) and, while the border is hidden, clamping the pill with `RecordingPillPlacement.ReachableFrame` to that display's work area (port of `RecordingOverlayController.swift:83-97`).
- **Display disconnects:** 0034 stops and saves a rectangle recording when its monitor disappears. For window sources, skip that check (macOS `RecordingSession.swift:197-205`). Windows moves the window to a remaining monitor, and WGC and the tracker follow it (PRD §7 item 4).
- **`NativeMethods.txt`:** add whichever of these aren't already present: `IsWindow`, `IsWindowVisible`, `IsIconic`, `GetWindowRect`, `GetWindowThreadProcessId`, `DwmGetWindowAttribute`, `DWMWINDOWATTRIBUTE`, `D2D1CreateFactory`, `ID2D1Factory1`, `ID2D1Device`, `ID2D1DeviceContext`, `ID2D1Bitmap1`, `D2D1_BITMAP_PROPERTIES1`, `D2D1_FACTORY_TYPE`, `D2D1_INTERPOLATION_MODE`, `IDXGIDevice`, `IDXGISurface`, `ID3D10Multithread`. No new NuGet packages.
- No PRD or AGENTS.md change.

## Acceptance criteria

- [ ] Ctrl+Shift+5 → Space → click a window produces an `.mp4` that contains only that window, without its shadow or invisible resize borders, with the cursor visible over it, at up to 30 fps.
- [ ] The output's pixel size equals the window's starting extended-frame size, rounded down to even numbers (minimum 2 × 2). For example, an 801 × 601 px window gives 800 × 600, and the same window on a 150% monitor (1202 × 902 px) gives 1202 × 902.
- [ ] Moving the window during the recording doesn't move the content in the video, and windows that cover it don't appear in it.
- [ ] Resizing the window, maximizing it, or moving it to a monitor with a different scale doesn't fail or restart the recording. The output size stays fixed, and the content is scaled to fit with its aspect ratio preserved and black bars where needed. An unresized window is recorded at native pixels with no resampling.
- [ ] Neither the pill nor the dashed border appears in the video.
- [ ] The pill and the border follow the window within about 0.25 s, including across monitors with different scales. The pill uses 0035's below/above/inside rule against the window's current monitor (largest overlap).
- [ ] Minimizing the window, moving it to another virtual desktop, or moving it entirely off-screen keeps recording: the border hides, the pill stays at its last position (kept reachable if its monitor disappears), and the border returns with the window. While minimized, the video holds the last frame.
- [ ] Closing the recorded window, or quitting its app, stops and saves the recording as if Stop had been pressed: one file is saved, File Explorer shows it selected, the tray icon returns to the viewfinder. Exactly one save and one reveal happen.
- [ ] Stop (pill, Ctrl+Shift+5, tray click), Cancel, and sign-out/shutdown behave as in rectangle mode, and the tracker stops in every case.
- [ ] Disconnecting the window's monitor keeps the window recording going on the remaining monitor; a rectangle recording on a disconnected monitor still stops and saves (0034).
- [ ] Rectangle recordings behave exactly as before.

## Edge cases

- **Mixed DPI:** a window dragged from a 100% to a 150% or 200% monitor grows in pixels (per-monitor-aware apps rescale); the content is scaled down into the fixed output. The reverse is scaled up. No restart, no error.
- **Negative-origin monitors** (left of or above the primary): frames are plain virtual-screen pixels with negative coordinates; the classifier and `WindowDisplayRule` handle them (tested).
- **A window spanning two monitors:** the border covers the whole window without clipping; the pill goes to the monitor with the larger overlap.
- **Maximized windows:** `GetWindowRect` extends past the monitor by the resize-border width; `WindowFrameCrop` crops to the extended frame, so no off-screen margin is recorded. Maximizing from a smaller start size letterboxes.
- **The window closes between the click and the start:** the start-failure alert appears and no file is left behind.
- **`HWND` reuse after close:** the thread-ID check classifies a different window with the same handle as `Gone`.
- **Transient `DwmGetWindowAttribute` failure:** `Unavailable` → `Unchanged`; never a close. In the frame handler, the last known crop is reused.
- **Minimized frame coordinates** (`-32000, -32000`) or a cloaked window: off-screen → `Hidden`, never a close.
- **The window hides itself** (for example an app minimizing to the tray, `IsWindowVisible == false`) but still exists: `Hidden`, recording continues.
- **`ContentSize` of 0 or a source with a zero dimension:** the frame is dropped; the pool is never recreated with a zero size.
- **Tracker and `Closed` both detect the close:** one save and one reveal (lifecycle test).
- **Elevated or protected windows:** if WGC refuses to create the item, it's a start failure with the alert. Windows that apply `WDA_EXCLUDEFROMCAPTURE` themselves record as black; no special handling.
- **A very large window:** 0034's encoder cap applies if it has one, with the aspect ratio preserved.

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`):
  - `WindowTrackingReducerTests.cs` (port of `WindowTrackingReducerTests.swift:8-71`; initial state `Frame = (100, 100, 800, 600)`):
    - The same frame, on-screen → `Unchanged`, state unchanged.
    - A new frame on the primary monitor `(300, 120, 800, 600)` → `Moved((300, 120, 800, 600))`, and the state's frame is updated.
    - Negative-origin monitor: `(-1820, 80, 800, 600)` → `Moved` with the same rect (no flip). A monitor above the primary: `(100, -1000, 400, 300)` → `Moved` with the same rect.
    - `Unavailable` → `Unchanged`, state unchanged.
    - `Gone` → `Closed`, `IsClosed == true`. Afterwards `Gone`, `Unavailable`, and `Present(..., true)` all → `Unchanged`, state unchanged.
    - Off-screen while visible → `Hidden`, `IsVisible == false`, `IsClosed == false`. Off-screen again → `Unchanged`. On-screen with a new frame `(200, 100, 400, 300)` → `Shown((200, 100, 400, 300))`, `IsVisible == true`.
  - `WindowPollClassifierTests.cs` (replaces `WindowInfoParserTests.swift:7-28`). Displays: primary `(0, 0, 1920, 1080)` at 100%, left `(-2880, 0, 2880, 1620)` at 150%, right `(1920, -200, 3840, 2160)` at 200%:
    - `Exists == false` → `Gone`. A different `ThreadId` → `Gone`.
    - `ExtendedFrame == null` → `Unavailable`.
    - Visible window on the primary → `Present(frame, true)`.
    - On the 150% negative-origin monitor `(-1500, 200, 1200, 900)` → `Present(frame, true)`. On the 200% monitor with a negative y `(2500, -150, 1600, 1200)` → `Present(frame, true)`.
    - Spanning primary and the left monitor `(-300, 100, 1000, 600)` → on-screen.
    - `IsMinimized` with frame `(-32000, -32000, 160, 28)` → `Present(frame, false)`. `IsCloaked` → off-screen. `IsVisible == false` → off-screen.
    - A frame that touches no monitor (`(10000, 10000, 100, 100)`) or, with only the primary in the display list, only shares its right edge (`(1920, 500, 100, 100)`) → off-screen.
    - Pill display: `WindowDisplayRule` on `(-300, 100, 1000, 600)` picks the primary (700 px vs 300 px overlap); on `(-700, 100, 1000, 600)` picks the left monitor; on `(10000, 10000, 100, 100)` returns `null` (port of `WindowTrackingReducerTests.swift:60-70`; put these in 0028's `WindowDisplayRuleTests.cs` if that's where the rule's tests live, without duplicating existing cases).
  - `LetterboxFitTests.cs`:
    - 800 × 600 into 800 × 600 → `(0, 0, 800, 600)`, `IsIdentity == true`.
    - 1600 × 600 into 800 × 600 → `(0, 150, 800, 300)`, not identity (wider window → letterbox).
    - 400 × 600 into 800 × 600 → `(200, 0, 400, 600)` (narrower → pillarbox).
    - 1200 × 900 (moved to 150%) into 800 × 600 → `(0, 0, 800, 600)`, not identity. 1600 × 1200 (200%) into 800 × 600 → `(0, 0, 800, 600)`.
    - 400 × 300 (moved to a lower scale / shrunk) into 800 × 600 → `(0, 0, 800, 600)` (scaled up).
    - 1201 × 900 into 1600 × 1200 → `(0, 0, 1600, 1199)` (rounding never exceeds the output).
    - 1 × 1000 into 800 × 600 → `(399, 0, 1, 600)` (minimum 1 px).
    - 0 × 600 and 800 × 0 sources → `null`.
  - `WindowFrameCropTests.cs` (or the 0032 crop test file, see Scope):
    - Content equals the extended frame size (800 × 600) → `(0, 0, 800, 600)`.
    - Window rect `(93, 100, 814, 607)`, extended frame `(100, 100, 800, 600)`, content 814 × 607 → `(7, 0, 800, 600)`.
    - Maximized on a 150% negative-origin monitor: window rect `(-2891, -11, 2902, 1642)`, extended frame `(-2880, 0, 2880, 1620)`, content 2902 × 1642 → `(11, 11, 2880, 1620)`.
    - Content smaller than expected (window rect 814 × 607, content 700 × 500) → `(7, 0, 693, 500)` (clamped to the content).
    - Insets that leave nothing (left ≥ content width) → the full content rect.
  - `RecordingConfigurationTests.cs` (extend 0034's file; port of `RecordingConfigurationBuilderTests.swift:88-110`):
    - Extended frame 800 × 600 (100%) → 800 × 600; 1200 × 900 (150%) → 1200 × 900; 1600 × 1200 (200%) → 1600 × 1200.
    - 801 × 601 → 800 × 600. 1 × 1 → 2 × 2. Origin `(-1500, 200)` doesn't change the result.
    - 10240 × 5760 → 10240 × 5760 (or 0034's cap, aspect preserved within 1 px and both even, if 0034 defines one).
    - The window configuration has `FollowsWindow`, `ScalesToFit`, `PreservesAspectRatio`, and `CursorVisible` all true, `FrameRate == 30`, and `Scale` equal to the passed monitor scale (1.0, 1.5, 2.0).
  - `RecordingLifecycleTests.cs` (only if this spec adds `WindowStreamFailed`; port of `RecordingLifecycleTests.swift:96-121`): stream failure → `[ScheduleStopTimeout]`, then stop and a second failure → `[]`, output finished → `[Reveal, ShowError, Ended]`; duplicate stop → one `[Reveal, Ended]`; timeout after a stream failure → reveal or delete by content, then `ShowError`, `Ended`.
  - `RecordingBorderGeometryTests.cs` / `RecordingPillPlacementTests.cs` (only if 0035 didn't port them): the unclipped `followsWindow` border spanning `(-700, 100, 1000, 600)` across a `(-1000, 0, 1000, 900)` monitor → `(-703, 97, 1006, 606)` (use 0035's outset); `ReachableFrame` keeps a pill on its monitor and clamps it into a remaining monitor's work area (port of `RecordingPillPlacementTests.swift:68-77`, in pixels).
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` both succeed.
- **Manual verification** (Windows 11 machine; can't be automated):
  1. Open a Notepad window. Press Ctrl+Shift+5, press Space, click Notepad. **Expected:** recording starts immediately, the tray icon turns into the red record dot, and the dashed border and pill surround the window.
  2. Drag Notepad around for a few seconds. **Expected:** the border and pill follow within about 0.25 s; the pill stays below the window and flips above or inside near screen edges.
  3. Drag another window over Notepad, type in Notepad, then click Stop. **Expected:** File Explorer opens with `Recording … .mp4` selected. In Media Player the video shows only Notepad (no shadow, no transparent or black resize-border margins, no desktop, no covering window), stays framed while it moved, shows the cursor over it, and has no pill or border. In File Explorer → Properties → Details, the frame size equals Notepad's starting size in physical pixels, even.
  4. With monitors at 100% and 150% (Settings → System → Display → Scale): record a window and drag it to the other monitor and back. **Expected:** the pill and border move to that monitor and match the window there; the recording continues; the video stays framed, scaled down (or up) to fit with black bars only where the aspect ratio changed.
  5. If a monitor is placed left of or above the primary (negative coordinates), repeat step 2 on it. **Expected:** the border and pill line up with the window.
  6. Record a window and resize it larger, smaller, and to a different aspect ratio, then maximize and restore it. **Expected:** the recording continues; border and pill track it; the video keeps its starting size with the content letterboxed or pillarboxed; the maximized frames show no off-screen margins.
  7. Record a window and minimize it, wait 3 s, restore it from the taskbar. **Expected:** recording continues; the border hides while minimized and returns on restore; the pill stays usable; the video holds the last frame during the minimized time.
  8. Record a window, then move it to another virtual desktop (Win+Tab → drag to a new desktop) and back. **Expected:** the border hides while it's away, the pill stays reachable, recording continues.
  9. Record a window and close it (Alt+F4). **Expected:** within about 0.25 s the pill and border disappear, the tray icon returns to the viewfinder, and File Explorer shows exactly one new `.mp4` that plays up to the close.
  10. Record a Notepad window and end its process in Task Manager. **Expected:** same as step 9.
  11. Record a window and press Ctrl+Shift+5. **Expected:** saved and revealed. Repeat with a tray click. **Expected:** saved and revealed, no menu opens. Repeat with the pill's Cancel. **Expected:** no file is saved and the overlay disappears.
  12. With two monitors: record a window on the secondary, then disconnect it (or turn it off/disable it in Display settings). **Expected:** the recording continues as Windows moves the window to the remaining monitor; the pill is reachable and the border follows. Repeat with a rectangle recording on that monitor. **Expected:** it stops and saves.
  13. Sign out while recording a window. **Expected:** "Saving recording…" appears in the shutdown screen briefly and the file is saved (0034's behavior holds for window sources).
  14. Regression: a rectangle recording (Ctrl+Shift+5, drag) still works as in 0034/0035.

## Out of scope

- Window selection, highlighting, the Space toggle, candidates, and window screenshots: 0032.
- The recording session lifecycle, tray state, MP4 writing, timestamps, file naming, saving, Explorer reveal, shutdown blocking, write-failure handling, and rectangle recordings: 0034 (plus 0026).
- Pill and border UI, elapsed time, placement, and Cancel: 0035. This spec only drives updates and border visibility (and fills gaps listed above).
- Microphone audio: 0037. MP4 quality re-encode: 0045. GIF: 0046. Window recordings inherit them, with the fixed output size and `Scale` from this spec.
- Changing the output size mid-recording or restarting the capture on resize (impossible within one H.264 track).
- Recording several windows or a whole app: not in the PRD.
