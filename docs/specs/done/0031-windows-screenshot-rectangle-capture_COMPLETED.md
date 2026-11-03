# 0031 — Capture a rectangle selection on Windows as a PNG and save it, copy it, or both

> Source: docs/PRD.md §6 (Screenshot), §9 (Files and naming: unwritable folder alert, Choose Folder…, retry once, *Both* still copies), §5 (overlay removed before capture), §12 (Ctrl+Shift+4 criterion, unwritable-folder criterion), §13.1 (Windows.Graphics.Capture, cursor off, `IsBorderRequired = false` with fallback, WIC), §13.2 (term mapping), §13.5 (native pixels = monitor scale, clipboard PNG + DIB, synthesized shutter sound, no permission fallback, full folder path in alerts), §13.8 (default folder), §13.11 (mixed-DPI PNG sizes; overlays never in output)
> Depends on: 0026-windows-file-naming-save-folder, 0027-windows-app-settings, 0030-windows-selection-overlay-rectangle (and transitively 0025, 0028, 0029)

## Context

Not yet implemented: `windows/` is created by 0025. After 0030, Ctrl+Shift+4 opens one overlay per
monitor, a rectangle drag produces a `SelectionResult` (Core, 0030), and `AppCoordinator`
(0025) receives it but does nothing with it. This spec turns that result into a screenshot.

The macOS reference implementation is spec `docs/specs/done/0008-screenshot-rectangle-capture_COMPLETED.md`
and these Swift sources. Port their behavior and test cases. Don't redesign them:

| macOS reference | What to port |
|---|---|
| `Snapper/App/AppCoordinator.swift:207-268` (`takeScreenshot`) | Pipeline order: capture → sound → encode off the UI thread → copy → save, with one folder retry |
| `Snapper/Capture/ScreenshotCapturer.swift:43-62` | Display-capture primitive (cursor off, crop to region, native pixels, own windows excluded) |
| `Snapper/Capture/ScreenshotOutputPlan.swift:5-34` | Destination → (copy, save); save-error classification with bounded inner-error traversal; folder display name |
| `Snapper/Support/ScreenshotFileWriter.swift:3-24` | Prepare folder, name with `FileNamer`, write without overwriting, retry a collision once |
| `Snapper/Support/PNGEncoder.swift:12-24` | PNG with resolution metadata derived from the scale factor |
| `Snapper/Support/Clipboard.swift:3-15` | One clipboard write carrying PNG plus a legacy bitmap format |
| `Snapper/Support/ShutterSound.swift:19-38` | Keep the player alive, restart if already playing; the caller checks the setting |
| `Snapper/Support/ErrorAlerts.swift:5-56` | "Couldn't take screenshot", generic error alert, "Couldn't save to <folder>" with Choose Folder… / Cancel, nearest existing folder as the picker's start |
| `Snapper/Capture/CapturedImage.swift:3-11` | Hand-off type carrying pixels plus scale |
| Tests: `SnapperTests/ScreenshotOutputPlanTests.swift:6-31`, `SnapperTests/ScreenshotFileWriterTests.swift:6-47`, `SnapperTests/PNGEncoderTests.swift:21-50` | Test cases ported below |

**macOS facts that don't apply on Windows (PRD §13.5, §13.10):**
- No Screen Recording permission and no onboarding. `CaptureFailure` (`Snapper/Capture/CaptureFailure.swift:3-11`,
  `SnapperTests/CaptureFailureTests.swift`) is **not ported**: every capture failure shows
  "Couldn't take screenshot" with the error.
- No Y flip. Selections and monitor bounds are already physical pixels in virtual-screen
  coordinates (top-left, y down), so cropping is a translation plus a clamp.
- No system sound file and no `Grab`/`Pop` fallback chain. `ShutterSoundResolver`
  (`SnapperTests/ShutterSoundResolverTests.swift`) is **not ported**. It is replaced by a
  synthesized click (PRD §13.5).
- No tilde abbreviation (`folderDisplayName`). Alerts show the full folder path (PRD §13.5).
- No TIFF on the clipboard. The legacy format is a DIB (PRD §13.5).
- Own-window exclusion is done per window with `WDA_EXCLUDEFROMCAPTURE` (0030's `CaptureExclusion`),
  not with an app-level `SCContentFilter`.

PRD behavior delivered here (with the §13.2 mapping applied):
- §6.2: PNG at native pixel resolution, i.e. the monitor's physical pixels (§13.5), with no cursor.
- §6.3: output follows *Screenshot destination*: *Save to file* / *Copy to clipboard* / *Both*
  (default **Both**).
- §6.4 / §13.5: if *Play shutter sound* is on (default), a short camera click synthesized in code
  plays without blocking. No audio file ships.
- §6.5 / §13.5: any capture failure, including "capture unavailable or denied", shows the alert
  "Couldn't take screenshot" with the error.
- §9: named at capture time (`Screenshot 2026-11-02 at 19.18.04.png`, collisions ` (2)`…); default
  folder `%USERPROFILE%\Pictures\Screenshots` (§13.8), created on first use. If it can't be
  created or written: alert "Couldn't save to <full folder path>" with **Choose Folder…** and
  **Cancel** and the system error details. With *Both*, the image is still on the clipboard.
  Choosing a folder updates the setting and retries the pending screenshot **once**. Other save
  failures use the title "Couldn't save screenshot".
- §5: the overlay is removed before capture (0030's contract), and overlay windows are excluded
  from capture anyway (`WDA_EXCLUDEFROMCAPTURE`, 0030).

Types from earlier specs used here. Refer to them by these names and adapt to the exact signatures
they landed with:
- 0026: Core `Support/CaptureKind`, `Support/FileNamer` (name for a `DateTime`, collision suffix
  from an "exists" predicate), `Support/SaveFolderPolicy` (folder creation and writability check,
  its typed save-folder exception carrying the system message, and the "Couldn't save to <folder>"
  message text); Windows `Support/SaveFolder` (known-folder default).
- 0027: Core `Settings/AppSettings` (`ScreenshotDestination`, `PlayShutterSound`, save folder
  path), `Settings/ISettingsStore`, `Settings/ScreenshotDestination`.
- 0028: Core `Geometry/PixelRect`, `PixelSize`, `DisplayInfo` (physical-pixel `Bounds`, scale
  factor), `DisplayGeometry`; Windows `Capture/DisplayInfoProvider`.
- 0029: Core `App/CapturePurpose`, `App/AppModeMachine`.
- 0030: Core `Capture/SelectionResult`, `Capture/SelectionSession`; Windows
  `Capture/SelectionOverlayController` (hides every overlay before it reports a result),
  `Support/CaptureExclusion`, `Support/ForegroundRestorer`.
  **Assumption:** a rectangle result carries the `DisplayInfo` where the drag started and the
  selection as a `PixelRect` in global physical pixels, already confined to that monitor and
  pixel-aligned. If 0030 landed with a DIP rect instead, convert it once with `DisplayGeometry`
  (0028) before calling `CaptureCrop`. Nothing else changes.

## User story

As a Snapper user on Windows, I press Ctrl+Shift+4, drag a rectangle on any monitor, hear a short
camera click, and get a PNG of exactly that area at the monitor's native pixel size in
`Pictures\Screenshots` and on my clipboard (or only one of them, per my setting), with no trace of
Snapper's overlay and no cursor in the image.

## Scope & changes

- **Reuse 0028:** `CaptureCrop` builds on 0028's `DisplayGeometry.CaptureRect` and `ToMonitorLocal`, and 0030's `RectSelection(DisplayInfo, PixelRect)`. It only adds what those don't provide, such as frame-size clamping and row copying. It never repeats their pixel math.

### Pure logic — `windows/src/Snapper.Core` (xUnit-tested, no Windows/WPF/WinRT/interop types)

- **`Capture/CapturedImage.cs`** — `public sealed class CapturedImage` with `int Width`,
  `int Height`, `byte[] Bgra` (tightly packed, stride `Width * 4`, top-down rows, BGRA order,
  alpha forced to 255), and `double ScaleFactor` (the source monitor's scale, e.g. 1.5 at 150%).
  The constructor validates `Bgra.Length == Width * Height * 4` and `Width, Height > 0`
  (`ArgumentException` otherwise). `PixelSize Size` convenience property. This is the hand-off
  type for 0032 (window capture) and 0040 (annotation). Port of `Snapper/Capture/CapturedImage.swift:3-11`.

- **`Capture/CaptureCrop.cs`** — `public static class CaptureCrop`:
  - `PixelRect? MonitorLocal(PixelRect selection, PixelRect monitorBounds, PixelSize frameSize)`:
    translate the global selection by `-monitorBounds.Left/-Top`, intersect with
    `(0, 0, frameSize.Width, frameSize.Height)`, return `null` if the result is empty. Scale is
    deliberately not a parameter: everything here is physical pixels. Defensive clamping only;
    0030 already confines the selection to its monitor.
  - `void CopyRows(ReadOnlySpan<byte> source, int sourceRowPitch, int width, int height, Span<byte> destination)`:
    copies `height` rows of `width * 4` bytes from a pitched buffer (D3D11 `RowPitch` is often
    larger than `width * 4`) into a tight buffer, and sets every 4th byte (alpha) to 255. Throws
    `ArgumentException` if `sourceRowPitch < width * 4`, if `source` is shorter than
    `(height - 1) * pitch + width * 4`, or if `destination` is shorter than `width * height * 4`.

- **`Capture/ScreenshotCaptureException.cs`** — `public sealed class ScreenshotCaptureException : Exception`
  with `ScreenshotCaptureErrorKind Kind` and the message per kind:
  `NotSupported` → "Screen capture isn't available on this PC.";
  `DisplayUnavailable` → "The display is no longer available.";
  `TimedOut` → "The screen didn't respond in time.";
  `WindowUnavailable` → "The window is no longer available." (reserved for 0032).
  Ports `ScreenshotCaptureError` (`Snapper/Capture/ScreenshotCapturer.swift:5-14`).

- **`Capture/ScreenshotOutputPlan.cs`** — port of `Snapper/Capture/ScreenshotOutputPlan.swift:5-34`:
  - `ScreenshotOutputPlan(ScreenshotDestination destination)` → `bool CopiesToClipboard`,
    `bool SavesToFile` (`File` → save only; `Clipboard` → copy only; `Both` → both).
  - `static SaveFailureAlert ClassifySaveFailure(Exception error)` with
    `enum SaveFailureAlert { SaveFolder, Generic }`. Walk `error` and its `InnerException` chain,
    at most 16 levels (cycle guard as in Swift line 17). `SaveFolder` when a level is: 0026's typed
    save-folder exception; `UnauthorizedAccessException`; `DirectoryNotFoundException`;
    `DriveNotFoundException`; or an `IOException` whose `HResult` is one of
    `0x80070005` (access denied), `0x80070013` (write protected), `0x80070003` (path not found),
    `0x80070015` (device not ready, e.g. a removed drive), `0x80070035` (network path not found).
    Everything else is `Generic`, for example disk full (`0x80070070`, `0x80070027`). Match by
    type and `HResult` only, never by message text, so tests are deterministic on macOS too.
  - `static double PngDpi(double scaleFactor)` → `96 * scaleFactor` (96 at 100%, 144 at 150%,
    192 at 200%). This is the Windows port of the macOS `72 × pixelsPerPoint` rule
    (`Snapper/Support/PNGEncoder.swift:18-19`): DPI-aware apps (Word, PowerPoint, WPF) then show
    the PNG at its on-screen DIP size. Pixel data is untouched.
  - `static string NearestExistingFolder(string folder, Func<string, bool> directoryExists)` →
    the folder itself if it exists, otherwise its nearest existing ancestor
    (`Path.GetDirectoryName` repeatedly), otherwise the root of the path, otherwise `folder`.
    Port of the loop in `Snapper/Support/ErrorAlerts.swift:48-53`.
  - Do **not** port `folderDisplayName`: alerts use the full path (§13.5).

- **`Capture/ScreenshotOutputRunner.cs`** — the output step of `takeScreenshot`
  (`Snapper/App/AppCoordinator.swift:236-267`), extracted so its order and retry rules are unit-tested.
  `public interface IScreenshotOutputEnvironment` with:
  `bool CopyToClipboard()`; `string Save(string folder)` (writes the pending PNG, returns the path,
  throws on failure); `string? ShowSaveFolderFailure(string folder, Exception error)` (returns the
  chosen folder, already stored in settings, or `null` on Cancel); `void ShowError(string title, string message)`.
  `public static class ScreenshotOutputRunner { ScreenshotOutputResult Run(ScreenshotOutputPlan plan, string saveFolder, IScreenshotOutputEnvironment env) }`
  with `ScreenshotOutputResult { bool Copied; string? SavedPath }`. Rules:
  1. **Copy first, then save.** If `plan.CopiesToClipboard`, call `CopyToClipboard()`. On `false`,
     `ShowError("Couldn't copy to clipboard", "The clipboard couldn't accept the screenshot.")` and continue.
  2. If `!plan.SavesToFile`, stop. `Save` is never called, so a clipboard-only capture never
     creates or touches the save folder.
  3. `Save(saveFolder)`. On an exception, classify it. `Generic` →
     `ShowError("Couldn't save screenshot", error.Message)` and stop. `SaveFolder` →
     `ShowSaveFolderFailure(folder, error)`. `null` → stop. A new folder on the **first** failure →
     `Save(newFolder)` once. A failure of that retry is classified again and shows its alert
     (the Choose Folder… choice then only updates the setting for future captures, as on macOS,
     `AppCoordinator.swift:249`), with no further save attempt.

- No new code for file naming, folder defaults, or settings. Those are 0026/0027.

- **`Support/ScreenshotFileWriter.cs`** (Core; uses `System.IO` only, which is portable and
  testable with temp folders) — port of `Snapper/Support/ScreenshotFileWriter.swift:3-24`:
  `public static string Save(byte[] png, string folder, DateTime captureDate)`. Prepares the
  folder with 0026's `SaveFolderPolicy` (creates it if missing, checks it is a writable directory,
  throws 0026's typed exception with the system message). Picks the name with
  `FileNamer` + `CaptureKind.Screenshot` + `captureDate`, using an "exists" predicate that treats
  any existing file, directory, or link at that path as taken. Writes with
  `new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)`. If that throws an
  `IOException` **and the path now exists** (a race with another writer), ask `FileNamer` again
  and retry once. Use the existence check, not an `HResult`, because the errno-based `HResult`
  differs on macOS test runs. Any other exception propagates unchanged.

- **`Support/ShutterSoundSynthesizer.cs`** — `public static class ShutterSoundSynthesizer` with
  `byte[] CreateWav()`, returning a complete RIFF/WAVE file. It must be deterministic: no
  `Random`, no time, no globals.
  - Format: PCM, 1 channel, **44,100 Hz**, 16-bit signed little-endian. Duration **120 ms** →
    exactly `SampleCount = 5292` samples. File length `44 + 5292 * 2 = 10,628` bytes.
  - Header (44 bytes, little-endian): `"RIFF"`, `10,620`, `"WAVE"`, `"fmt "`, `16`, format `1`,
    channels `1`, sample rate `44100`, byte rate `88200`, block align `2`, bits `16`, `"data"`,
    `10,584`.
  - Signal: two transients, a shutter "click" at **0 ms** and a "clack" at **45 ms**. Each is
    `noise(n) * 0.6 + sin(2π f t) * 0.4`, multiplied by an envelope that rises linearly over
    **1 ms** and then decays as `exp(-t / τ)`. First transient: `f = 2,000 Hz`, `τ = 6 ms`,
    amplitude 1.0. Second: `f = 1,400 Hz`, `τ = 8 ms`, amplitude 0.7. `t` is measured from each
    transient's start. Noise is a 32-bit xorshift (`x ^= x << 13; x ^= x >> 17; x ^= x << 5`) with
    the fixed seed `0x5EED1234`, mapped to `[-1, 1)`. Sum the transients, apply a final linear
    fade-out over the last **5 ms**, then normalize so the absolute peak is exactly
    `PeakAmplitude = 16384` (half of full scale, so it's never jarring). Round to the nearest
    integer and clamp to `[-16384, 16384]`.
  - Expose `SampleRate`, `DurationMs`, `SampleCount`, `PeakAmplitude`, and
    `SecondTransientMs = 45` as `public const` for the tests.
  - Determinism is guaranteed within a process and architecture. Don't write a golden-hash test,
    because `Math.Exp`/`Math.Sin` can differ in the last bit between x64 and Arm64.

- **`Support/DibEncoder.cs`** — `public static byte[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)`
  → a packed DIB for `CF_DIB`, with no `BITMAPFILEHEADER`: a 40-byte `BITMAPINFOHEADER` (`biSize 40`,
  `biWidth = width`, `biHeight = +height` (bottom-up), `biPlanes 1`, `biBitCount 24`,
  `biCompression 0` (BI_RGB), `biSizeImage = stride * height`, pels-per-meter `0`, `biClrUsed 0`,
  `biClrImportant 0`), followed by the rows **bottom row first**. Each row holds `width` BGR
  triplets and is zero-padded to `stride = (width * 3 + 3) & ~3`. 24-bit avoids apps
  misinterpreting a 32-bit DIB's alpha channel. Windows synthesizes `CF_DIBV5` and `CF_BITMAP`
  from `CF_DIB`.

### Platform glue — `windows/src/Snapper.Windows` (manually verified)

- **`Interop/Direct3D11Helper.cs`** — a shared, lazily created D3D11 device for screenshots now
  and for recordings later (0034/0036). It is a `sealed class` with one process-wide instance
  owned by `AppCoordinator`, and it implements `IDisposable`.
  - `D3D11CreateDevice` with `D3D_DRIVER_TYPE_HARDWARE` and `D3D11_CREATE_DEVICE_BGRA_SUPPORT`,
    falling back to `D3D_DRIVER_TYPE_WARP` if hardware creation fails. Enable
    `ID3D11Multithread.SetMultithreadProtected(true)`, because `FrameArrived` runs on a worker
    thread and later specs share the immediate context.
  - `IDirect3DDevice WinRtDevice`: wrap the device via `IDXGIDevice` +
    `CreateDirect3D11DeviceFromDXGIDevice`, then project the returned `IInspectable*` with
    `WinRT.MarshalInterface<IDirect3DDevice>.FromAbi`. Release the raw pointer afterwards.
  - `ID3D11Texture2D GetTexture(IDirect3DSurface surface)`: QueryInterface the surface's native
    object (`((WinRT.IWinRTObject)surface).NativeObject`) for `IDirect3DDxgiInterfaceAccess` and
    call `GetInterface(IID_ID3D11Texture2D)`.
  - `ID3D11Texture2D CreateStagingTexture(int width, int height)`: `DXGI_FORMAT_B8G8R8A8_UNORM`,
    `D3D11_USAGE_STAGING`, `D3D11_CPU_ACCESS_READ`, 1 mip, 1 array slice, no bind flags.
  - `void Reset()`: drop the device so the next use recreates it, after
    `DXGI_ERROR_DEVICE_REMOVED`/`DXGI_ERROR_DEVICE_RESET` (driver update, GPU switch).
  - COM lifetimes: wrap in `using`/`Marshal.ReleaseComObject` per AGENTS.md. No `!` operator
    except for documented invariants.

- **`Capture/ScreenshotCapturer.cs`** — `sealed class ScreenshotCapturer(Direct3D11Helper d3d)`:
  - `Task<CapturedImage> CaptureMonitorRegionAsync(DisplayInfo display, PixelRect selection)`.
    It is called on the dispatcher thread and returns a `CapturedImage`. Steps:
    1. `if (!GraphicsCaptureSession.IsSupported())` → throw `ScreenshotCaptureException(NotSupported)`.
    2. Resolve the `HMONITOR` at capture time: `MonitorFromPoint(center of display.Bounds, MONITOR_DEFAULTTONULL)`,
       then `GetMonitorInfo`. If the handle is null or `rcMonitor` ≠ `display.Bounds`, throw
       `DisplayUnavailable` (the monitor was unplugged or rearranged after the overlay closed).
    3. Create the item: get the activation factory for `"Windows.Graphics.Capture.GraphicsCaptureItem"`
       as `IGraphicsCaptureItemInterop` (`WinRT.ActivationFactory.Get(…, IID)`), call
       `CreateForMonitor(hmonitor, IID_IGraphicsCaptureItem)`, then
       `WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr)` and release `ptr`.
    4. Borderless (PRD §13.1): call `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)`
       **once per process**, inside `try/catch`, and cache the result. Then, if
       `ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired")`,
       set `session.IsBorderRequired = false` inside `try/catch`. Any refusal or exception is
       logged once and capture proceeds with the border. The border is never an error.
    5. `Direct3D11CaptureFramePool.CreateFreeThreaded(d3d.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size)`,
       `session = pool.CreateCaptureSession(item)`, `session.IsCursorCaptureEnabled = false`.
    6. Subscribe to `FrameArrived` and call `StartCapture()`. On the **first** frame, on the
       worker thread: `TryGetNextFrame()`; `crop = CaptureCrop.MonitorLocal(selection, display.Bounds, frame.ContentSize)`;
       `null` → `DisplayUnavailable`. Get the frame texture via `d3d.GetTexture(frame.Surface)`,
       create a staging texture of the crop size, call `CopySubresourceRegion` with a `D3D11_BOX`
       of the crop, `Map(READ)`, `CaptureCrop.CopyRows(mapped, RowPitch, w, h, buffer)`, then
       `Unmap`. Complete a `TaskCompletionSource<CapturedImage>`
       (`RunContinuationsAsynchronously`) with `new CapturedImage(w, h, buffer, display.ScaleFactor)`.
       Ignore later frames.
    7. Timeout: if no frame arrives within **3 s**, throw `TimedOut`.
    8. `finally`: dispose the frame, the session, and the pool, and unsubscribe. The `await`
       resumes on the dispatcher (the caller's sync context).
    9. On `DXGI_ERROR_DEVICE_REMOVED`/`RESET` (COMException `HResult`), call `d3d.Reset()` and
       retry the whole capture **once**.
  - Structure the item-independent part (steps 4–9) as a private
    `CaptureItemAsync(GraphicsCaptureItem item, Func<PixelSize, PixelRect?> crop, double scale)`
    so that 0032 adds `CreateForWindow` without duplicating the frame pipeline.
  - Our overlays are excluded twice: 0030 hides them before reporting the result, and they carry
    `WDA_EXCLUDEFROMCAPTURE`. Don't add delays.

- **`Support/PngEncoder.cs`** — `static byte[] Encode(CapturedImage image)`:
  `BitmapSource.Create(w, h, dpi, dpi, PixelFormats.Bgr32, null, image.Bgra, w * 4)` with
  `dpi = ScreenshotOutputPlan.PngDpi(image.ScaleFactor)`, then `Freeze()`. Encode with WPF's
  `PngBitmapEncoder` (it wraps WIC, PRD §13.1) into a `MemoryStream`. `Bgr32` ignores alpha, so the
  PNG is opaque 24-bit RGB. Thread-safe when called from `Task.Run` (the source is created and
  frozen on the same thread). Wrap any exception in an `InvalidOperationException` with the
  message "The screenshot couldn't be encoded as PNG." and the original as the inner exception.
  0043 (annotation export) reuses this.

- **`Support/Clipboard.cs`** — `static class Clipboard` (fully qualify WPF's
  `System.Windows.Clipboard` inside it). `bool Copy(byte[] png, byte[] dib)` must be called on
  the dispatcher thread, which is STA. Build one `System.Windows.DataObject` with
  `SetData("PNG", new MemoryStream(png))` (the registered `PNG` format that Office, browsers,
  Paint, and chat apps read) and `SetData(DataFormats.Dib, new MemoryStream(dib))`, then call
  `System.Windows.Clipboard.SetDataObject(obj, copy: true)`. That call already retries while
  another process holds the clipboard. Return `false` (and log the error) on
  `COMException`/`ExternalException`. The PNG is encoded once and shared with the file save, and
  the DIB comes from `DibEncoder`. 0043 reuses this.

- **`Support/ShutterSound.cs`** — `sealed class ShutterSound : IDisposable`: lazily builds
  `ShutterSoundSynthesizer.CreateWav()` once, keeps a `MemoryStream` and a
  `System.Media.SoundPlayer` as fields (they must outlive asynchronous playback), and calls
  `Load()` once. `Play()` calls `SoundPlayer.Play()`, which is asynchronous (`PlaySound` with
  `SND_ASYNC`) and never blocks the UI thread. Calling it while a click plays restarts the click,
  like `Snapper/Support/ShutterSound.swift:36-37`. Exceptions (no audio device) are caught and
  logged once. The caller checks `AppSettings.PlayShutterSound`; `ShutterSound` has no settings
  dependency.

- **`Support/ErrorAlerts.cs`** — `static class ErrorAlerts`, using the WinForms
  `System.Windows.Forms.TaskDialog` (fully qualified, because the global WinForms usings are
  removed). Every page has caption "Snapper", `Icon = TaskDialogIcon.Warning`, and
  `StartupLocation = CenterScreen`. Because the tray app has no window and the overlay's process
  received the last input, bring the dialog to the front: in the page's `Created` handler, call
  `SetForegroundWindow` on `page.BoundDialog.Handle`.
  - `ShowCaptureFailure(Exception e)` → heading "Couldn't take screenshot", text `e.Message`, button OK.
  - `ShowError(string title, string message)` → same shape with a custom heading. It is used for
    "Couldn't copy to clipboard" and "Couldn't save screenshot", and reused by 0034 for recording
    errors.
  - `string? ShowSaveFolderFailure(string folder, Exception error, AppSettings settings, ISettingsStore store)` →
    heading from 0026's "Couldn't save to <folder>" text with the **full** path (§13.5), text
    `error.Message`, buttons **"Choose Folder…"** (default, first) and **Cancel**.
    Choose Folder… opens `Microsoft.Win32.OpenFolderDialog` (`Title = "Choose Save Folder"`,
    `Multiselect = false`, `InitialDirectory = ScreenshotOutputPlan.NearestExistingFolder(folder, Directory.Exists)`).
    On OK, it updates the save folder in `AppSettings`, persists it via `ISettingsStore`
    (0027; change notification fires), and returns the chosen path. Otherwise it returns `null`.
    0033's Settings folder picker may reuse the dialog setup.
  - Visual styles: `TaskDialog` needs Common Controls v6. If 0025's `app.manifest` doesn't
    declare the `Microsoft.Windows.Common-Controls` 6.0 dependency, add it there in this spec
    (preferred), or call `System.Windows.Forms.Application.EnableVisualStyles()` once at startup
    before any dialog.

- **`App/AppCoordinator.cs`** (modify 0025's stub) — wire `.Screenshot`:
  1. On a rectangle `SelectionResult` with purpose `Screenshot` (the overlay has already hidden
     itself; `AppModeMachine` goes back to idle exactly as 0029/0030 define; no new modes):
     `captureDate = DateTime.Now` taken immediately before capturing (§9: capture time).
  2. `await capturer.CaptureMonitorRegionAsync(display, rect)`. Any exception → log, call
     `ErrorAlerts.ShowCaptureFailure(e)`, stop. No sound and no output on failure.
  3. Success → if `settings.PlayShutterSound`, `shutterSound.Play()`.
  4. `await Task.Run(() => (PngEncoder.Encode(image), DibEncoder.FromBgra(image.Bgra, image.Width, image.Height)))`
     (a 4K/5K frame takes noticeable time; the UI stays responsive). Failure → `ShowCaptureFailure`, stop.
  5. Back on the dispatcher, `ScreenshotOutputRunner.Run(new ScreenshotOutputPlan(settings.ScreenshotDestination), settings.SaveFolder, env)`.
     `env` is a small private adapter: `CopyToClipboard` → `Clipboard.Copy(png, dib)`;
     `Save(folder)` → `ScreenshotFileWriter.Save(png, folder, captureDate)` (same bytes and date
     across the retry); `ShowSaveFolderFailure` → `ErrorAlerts.ShowSaveFolderFailure`;
     `ShowError` → `ErrorAlerts.ShowError`.
  6. Log successes and failures (saved path, clipboard result) with the logging approach 0025
     established, or `System.Diagnostics.Debug.WriteLine` if it established none.
  7. A second Ctrl+Shift+4 during the short capture may open a new overlay. Captures are
     independent, as on macOS.
  8. **Ctrl+Shift+6 until 0040:** a rectangle result with purpose `Annotate` captures nothing,
     saves nothing, copies nothing, and plays no sound. It logs "Annotate isn't available yet"
     and restores focus to the remembered foreground window with 0030's `ForegroundRestorer`
     (0030 skips the restore for Annotate selections, and no annotation window will take focus
     yet). 0040 replaces this branch with capture → annotation window.
  9. Ctrl+Shift+5 behavior is unchanged from 0030 (0034 wires recording). Window results don't
     exist until 0032.

- **`NativeMethods.txt`** — add (keep the existing 0025–0030 entries): `D3D11CreateDevice`,
  `ID3D11Device`, `ID3D11DeviceContext`, `ID3D11Texture2D`, `ID3D11Multithread`, `IDXGIDevice`,
  `D3D11_TEXTURE2D_DESC`, `D3D11_BOX`, `D3D11_MAPPED_SUBRESOURCE`, `D3D11_SDK_VERSION`,
  `DXGI_ERROR_DEVICE_REMOVED`, `DXGI_ERROR_DEVICE_RESET`, `CreateDirect3D11DeviceFromDXGIDevice`,
  `IDirect3DDxgiInterfaceAccess`, `IGraphicsCaptureItemInterop`, `MonitorFromPoint`,
  `GetMonitorInfo` (if 0028 didn't add it), `SetForegroundWindow`.
  If CsWin32 can't produce a usable projection of `IGraphicsCaptureItemInterop` or
  `IDirect3DDxgiInterfaceAccess` alongside C#/WinRT objects, declare only that interface in
  `Interop/` as a minimal `[GeneratedComInterface]` with its documented IID and a comment
  explaining why. Don't add NuGet packages. The WinRT projections (`Windows.Graphics.Capture`,
  `Windows.Graphics.DirectX.Direct3D11`, `Windows.Foundation.Metadata.ApiInformation`) come from
  the TFM `net10.0-windows10.0.22621.0`.

- **PRD:** no change. All decisions are in docs/PRD.md §6, §9, §13.1, §13.5, §13.8. This spec
  also makes one detail decision that ports the macOS rule: PNG DPI metadata is `96 × scale factor`.

## Acceptance criteria

- [ ] Ctrl+Shift+4 + rectangle drag on a 150% monitor produces a PNG whose pixel size equals the
      dragged area in physical pixels (1.5× the DIP size shown in the readout, ±1 px), saved to
      `%USERPROFILE%\Pictures\Screenshots` and copied to the clipboard with the default *Both*
      destination (§12, §13.5).
- [ ] On a 100% monitor the PNG pixel size equals the DIP size. On a 200% monitor it is 2×.
- [ ] The PNG's DPI metadata is 96 × the monitor's scale factor (96 / 144 / 192).
- [ ] With monitors at different scales (e.g. 100% and 150%), the capture comes from the monitor
      where the drag started, matches the dragged area, and has that monitor's native pixel size,
      including a monitor left of or above the primary (negative virtual-screen origin) (§13.11).
- [ ] The image contains no dimming, selection rectangle, W×H readout, capture-excluded Snapper overlay, or
      cursor.
- [ ] No yellow capture border appears during capture on Windows 11 builds that allow borderless
      capture. Where Windows refuses, capture still succeeds.
- [ ] *Save to file* only saves. *Copy to clipboard* only copies and never creates the save
      folder. *Both* does both.
- [ ] A pasted image works in Paint (DIB) and in Word/PowerPoint or a browser-based app (PNG).
- [ ] The file name is `Screenshot YYYY-MM-DD at HH.mm.ss.png` using the capture time, with
      ` (2)`, ` (3)` … on collision.
- [ ] A short camera click plays on success when *Play shutter sound* is on, without delaying the
      output. It doesn't play when the setting is off or the capture fails.
- [ ] A missing `Pictures\Screenshots` folder is created on first save (§12).
- [ ] An unwritable save folder shows "Couldn't save to <full path>" with the system error,
      **Choose Folder…** and **Cancel**. With *Both*, the image is on the clipboard anyway.
      Choosing a folder updates the setting and saves the pending screenshot there once.
- [ ] A disk-full or other non-folder save error shows "Couldn't save screenshot".
- [ ] Any capture failure shows "Couldn't take screenshot" with the error. There is no onboarding.
- [ ] Ctrl+Shift+6 + drag produces no file, clipboard change, or sound, returns focus to the
      previous app, and doesn't crash.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed.

## Edge cases

- **Mixed DPI:** the crop never uses a scale factor. Only `PngDpi` and `CapturedImage.ScaleFactor`
  use the drag monitor's own scale, never the primary monitor's.
- **Negative-origin monitors** (left of or above the primary): `MonitorLocal` subtracts the
  negative origin, so the local crop is never negative.
- **Selection touching the monitor edge / full-monitor selection:** the crop is exactly
  `(0, 0, frameW, frameH)`.
- **Selection spilling onto a neighbor** (can't happen after 0030, but defend): clamped to the
  starting monitor.
- **Monitor unplugged or rearranged after the overlay closed:** the `HMONITOR`/bounds check →
  "The display is no longer available."
- **Frame `ContentSize` smaller than expected** (resolution changed between selection and
  capture): clamped crop. An empty crop → `DisplayUnavailable`.
- **No frame** (monitor asleep, GPU hang): 3 s timeout → "The screen didn't respond in time."
- **GPU device removed** (driver update, eGPU unplug): `Reset()` and one retry, then the alert.
- **WGC unsupported** (e.g. some VMs or Server Core): `NotSupported` alert.
- **Rotated (portrait) monitor:** WGC delivers the frame in desktop orientation. The crop math
  is unchanged (manual check).
- **HDR monitor:** the `B8G8R8A8UIntNormalized` frame is Windows' SDR conversion. Colors may
  differ slightly from the HDR view. Accepted for v1.
- **DRM-protected or `WDA_MONITOR` windows** of other apps appear black. That's expected
  system behavior.
- **Save folder is a file, is on a removed drive, is read-only, or is a network path that's
  gone:** `SaveFolder` alert. **Disk full:** generic alert.
- **User cancels Choose Folder…, or Cancel in the alert:** nothing more happens. The image survives
  only on the clipboard (*Both*/*Clipboard*).
- **Chosen folder also fails:** the same alert shows again. Choosing another folder only changes
  the setting, with no second retry of this screenshot.
- **Two captures in the same second:** collision suffix. `FileMode.CreateNew` guards the race,
  with one rename retry.
- **Clipboard held by another process:** `SetDataObject` retries. On final failure,
  "Couldn't copy to clipboard", and saving still proceeds.
- **No audio output device:** the sound is silently skipped (logged once). The screenshot is
  unaffected.
- **Very large captures** (e.g. 7680×4320): copy, encode, and DIB build happen on the worker
  thread or in `Task.Run`, so the dispatcher stays responsive.
- **Hotkey pressed while an alert is open:** the modal TaskDialog loop still pumps messages. A
  new overlay may open, as on macOS. Not specially handled.

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`, xUnit):
  - `CaptureCropTests.cs`
    - Primary 100% monitor `(0,0,1920,1080)`, frame 1920×1080, selection `(100,200,300,150)` → `(100,200,300,150)`.
    - 150% monitor right of primary `(1920,0,2880,1620)`, frame 2880×1620, selection
      `(2000,50,300,150)` → `(80,50,300,150)` (scale plays no role).
    - 200% monitor left of and above primary `(-3840,-720,3840,2160)`, frame 3840×2160, selection
      `(-3800,-700,400,200)` → `(40,20,400,200)`.
    - Monitor above primary `(0,-1440,2560,1440)`, selection `(10,-10,100,10)` → `(10,1430,100,10)`.
    - Full-monitor selection on the negative-origin 200% monitor → `(0,0,3840,2160)`.
    - Drag across monitors (defensive): selection `(1800,100,300,200)` against the primary
      `(0,0,1920,1080)` → `(1800,100,120,200)`.
    - Selection entirely on another monitor → `null`. A zero-size selection → `null`.
    - Frame smaller than the monitor (1280×720 for a 1920×1080 monitor), selection
      `(1200,700,200,100)` → `(1200,700,80,20)`.
    - `CopyRows`: width 5, height 3, pitch 32, source filled with a row/column pattern → the
      destination is 60 bytes, each row equals the first 20 bytes of the matching source row,
      and every alpha byte is 255. Pitch 16 for width 5 → `ArgumentException`. A destination too
      short → `ArgumentException`.
  - `CapturedImageTests.cs` — the valid constructor exposes `Size`. A buffer length mismatch →
    `ArgumentException`. Width 0 → `ArgumentException`.
  - `ScreenshotOutputPlanTests.cs` (port of `SnapperTests/ScreenshotOutputPlanTests.swift:6-31`)
    - Table: `File` → (save true, copy false); `Clipboard` → (false, true); `Both` → (true, true).
    - `ClassifySaveFailure` → `SaveFolder` for: 0026's save-folder exception;
      `UnauthorizedAccessException`; `DirectoryNotFoundException`; `DriveNotFoundException`;
      `IOException` with each `HResult` 0x80070005, 0x80070013, 0x80070003, 0x80070015,
      0x80070035; an `IOException("x", inner: new UnauthorizedAccessException())` (inner
      traversal); a 3-level chain whose innermost is write-protected.
    - `ClassifySaveFailure` → `Generic` for: `IOException` 0x80070070 (disk full), 0x80070027,
      `new Exception("x")`, `InvalidOperationException`.
    - `PngDpi(1.0) == 96`, `PngDpi(1.5) == 144`, `PngDpi(2.0) == 192`, `PngDpi(1.25) == 120`.
    - `NearestExistingFolder` with a fake predicate: an existing folder returns itself; `C:\a\b\c`
      where only `C:\a` exists → `C:\a`; nothing exists → `C:\`. Build the paths with
      `Path.Combine` and a fake root, so the test also passes on macOS, where `\` isn't a separator.
  - `ScreenshotOutputRunnerTests.cs` (fake `IScreenshotOutputEnvironment` that records calls in order)
    - `Both`, everything succeeds → calls are `[Copy, Save(folder)]` in that order. Result copied plus saved path.
    - `File` → `[Save]` only. `Clipboard` → `[Copy]` only, and `Save` is never called.
    - `Both`, copy returns false → `ShowError("Couldn't copy to clipboard", …)`, then `Save` is still called.
    - `Both`, save throws `UnauthorizedAccessException`, the alert returns `null` → the copy
      happened, one alert, no second save.
    - Save throws the folder error, the alert returns `D:\New` → the second `Save("D:\New")`
      succeeds, and the result path is from the retry.
    - The retry also fails with a folder error → a second `ShowSaveFolderFailure`, and no third
      `Save` even if it returns a folder.
    - Save throws `IOException` 0x80070070 → `ShowError("Couldn't save screenshot", message)`,
      with no folder alert and no retry.
  - `ScreenshotFileWriterTests.cs` (port of `SnapperTests/ScreenshotFileWriterTests.swift:6-47`; temp folders)
    - Missing nested folder is created. Two saves with the same date produce `FileNamer`'s name
      and the same name + ` (2)`. Both contents are preserved byte for byte.
    - A folder path whose parent is a file → throws, and `ClassifySaveFailure` == `SaveFolder`,
      with a non-empty message.
    - A folder path that is itself a file → throws, classified `SaveFolder`.
    - An existing file with the would-be name is never overwritten.
  - `ShutterSoundSynthesizerTests.cs`
    - Length == 10,628. Header fields exactly as specified (RIFF/WAVE/fmt/data tags, sizes
      10,620 and 10,584, PCM 1, mono, 44,100, 88,200, block align 2, 16 bits).
    - Two calls return identical bytes.
    - Max |sample| == 16,384 (normalized peak, never above).
    - First sample == 0 (attack starts at 0). Last sample == 0 (fade-out).
    - Not silent: RMS over the first 10 ms > 2,000.
    - Second transient present: max |sample| in 45–55 ms ≥ 0.3 × peak, and it is greater than the
      max in 35–44 ms (the click has decayed before the clack).
  - `DibEncoderTests.cs`
    - 3×2 image with distinct BGRA pixels → length `40 + 12 * 2 = 64`. Header fields: 40, 3, +2,
      1, 24, 0, `biSizeImage` 24. The first data row equals the **bottom** source row as BGR
      triplets, then 3 zero padding bytes. The second data row is the top source row. Alpha is dropped.
    - 4×1 image → stride 12, no padding, length 52.
    - Buffer length mismatch → `ArgumentException`.
  - `ScreenshotCaptureExceptionTests.cs` — each kind's `Message` equals the exact string above.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed (both run on macOS).
- **Manual verification** (Windows 11, real GUI session; can't be automated). Until 0033 adds
  Settings, change settings by exiting Snapper, editing `%APPDATA%\Snapper\settings.json` with
  0027's keys (`screenshotDestination`: `file`/`clipboard`/`both`, `playShutterSound`, save folder
  path), and relaunching.
  1. Defaults (*Both*, sound on), one 150% monitor: press Ctrl+Shift+4 and drag about 300×200 DIPs
     (per the readout). Expect a click sound and a new `Screenshot <date> at <time>.png` in
     `%USERPROFILE%\Pictures\Screenshots`. File Properties → Details shows about 450×300 px and
     144 DPI. Paste into Paint: same image.
  2. Open the PNG. There is no dimming, selection border, W×H label, or mouse cursor, and the
     area matches the selection.
  3. Repeat on a 100% monitor (pixel size = DIP size, 96 DPI) and a 200% monitor (2×, 192 DPI).
  4. In Settings → System → Display, place a monitor at a different scale **left of** (or above)
     the primary. Drag on it. The image shows that monitor's content at the selected spot, at its
     native pixel size.
  5. Watch the monitor edges during capture. No yellow border flashes (Windows 11 22H2 or
     later). If one appears on an older build, the capture still succeeds.
  6. Destination *Save to file*: copy some text first, then capture. The file is saved and the
     clipboard still holds the text.
  7. Destination *Copy to clipboard*: rename `Pictures\Screenshots` aside, then capture. The
     clipboard has the image (paste into Word and Paint), and the folder was **not** recreated.
     Restore the folder.
  8. `playShutterSound: false` → no sound. `true` → sound. Capture several times quickly: each
     click plays without delay and doesn't stack.
  9. *Both*: rename `Pictures\Screenshots` aside, capture. The folder is recreated with the file
     in it.
  10. Make the folder read-only for your user (Properties → Security → Deny Write), then capture
      with *Both*. The alert "Couldn't save to C:\Users\<you>\Pictures\Screenshots" shows the
      system message, and paste shows the image. Choose Folder… → pick `Desktop`: the screenshot
      is saved there, and the next capture goes to `Desktop`. Repeat and click **Cancel**: nothing
      else happens. Remove the Deny entry and reset the folder.
  11. Set the save folder to a USB drive, unplug it, capture → the "Couldn't save to" alert.
  12. Capture twice within one second (use a tiny drag twice quickly) → the second file gets ` (2)`.
  13. Disable the GPU driver briefly, or capture right after a display driver update if
      practical. Otherwise, verify that a capture with a monitor asleep (if possible) shows
      "Couldn't take screenshot" after about 3 s, without a hang.
  14. Rotate a monitor to portrait and capture on it → correct orientation and area.
  15. Press Ctrl+Shift+6, drag → no file, no clipboard change, no sound, and focus returns to the
      previous app (typing goes there).
  16. Press Ctrl+Shift+5, drag → behavior unchanged from 0030.

## Out of scope

- Window mode, window candidates, and `CreateForWindow` capture cropped to the extended frame
  bounds — 0032 (it reuses `CaptureItemAsync`, `CaptureCrop`, and `CapturedImage`).
- Settings UI for destination, sound, and folder — 0033 (this spec only reads and writes `AppSettings`).
- Recording, its D3D/Media Foundation pipeline, and Explorer reveal — 0034–0037 (they reuse
  `Direct3D11Helper` and `ErrorAlerts`).
- Opening captures in the annotation window (Ctrl+Shift+6) — 0040. Annotation Save/Copy — 0043
  (reuses `PngEncoder`, `Clipboard`, `DibEncoder`).
- The selection overlay itself (dimming, readout, Esc, display-change cancel, capture exclusion
  of overlays) — 0030.
- HDR tone mapping, JPEG output, floating thumbnails, window shadows — PRD §2 non-goals or v1
  limitations.

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**. `dotnet test windows/Snapper.slnx` passed **522 tests**, including **60 new cases**, with **0 failures and 0 skips**. Logs: `/tmp/snapper-0031-build.log` and `/tmp/snapper-0031-test.log`.

Implemented Core capture crops/pitched-row copies, image validation, output decisions and bounded folder retry, save-error classification, collision-safe file writes, DIB packing, and deterministic shutter WAV synthesis. Windows glue uses WGC, CsWin32 D3D11 interfaces, WIC-backed PNG encoding, PNG/DIB clipboard output, native alerts, and sound playback. Checked HRESULT-returning P/Invokes explicitly; generated COM methods without `PreserveSig` throw on failed HRESULTs. No new packages were added.

Internal API adaptations: the shared D3D11 helper provides disposable device-generation leases (`Acquire`), with the projected WinRT device, native device/context, texture access and staging creation on the lease. This prevents an independent capture's reset from disposing another capture's resources. `ErrorAlerts.ShowSaveFolderFailure` accepts `AppSettings` without a redundant store argument because `AppSettings` already owns persistence. TaskDialog startup location is supplied to `ShowDialog`, matching the actual framework API. AGENTS.md now records the tested Core helpers and device-lease ownership rule. PRD behavior is unchanged. The acceptance wording about other Snapper windows was clarified to capture-excluded overlays: PRD §13.6 explicitly permits ordinary Settings and annotation windows to appear in capture.

Actual WGC/D3D11 capture, WIC PNG metadata, native clipboard/audio/dialog behavior, and all GUI paths require Windows 11 and **have not been verified here**. Until 0033 provides Settings, exit Snapper, edit `%APPDATA%\Snapper\settings.json`, and relaunch to change destination, folder or shutter settings.

- [ ] not yet run by user: At defaults on a 150% monitor, capture about 300×200 DIPs; confirm shutter sound, named PNG in Pictures\Screenshots, about 450×300 pixels at 144 DPI, and successful paste into Paint.
- [ ] not yet run by user: Open the PNG and confirm the selected area contains no dimming, selection outline/readout, mouse cursor, or excluded Snapper overlay.
- [ ] not yet run by user: Repeat on 100% and 200% monitors; confirm native pixel dimensions and 96/192 DPI metadata.
- [ ] not yet run by user: Capture on a differently scaled monitor left of or above the primary; confirm correct content and native pixel size.
- [ ] not yet run by user: Watch monitor edges during capture; confirm no yellow border where Windows permits borderless capture, and successful capture if Windows retains it.
- [ ] not yet run by user: With file-only destination, capture after copying text; confirm the file saves and clipboard text remains.
- [ ] not yet run by user: With clipboard-only destination, move Pictures\Screenshots aside, capture and paste into Paint and Word/browser; confirm the folder is not recreated, then restore it.
- [ ] not yet run by user: Toggle shutter sound off/on and capture repeatedly; confirm silence when disabled and asynchronous restarting clicks when enabled.
- [ ] not yet run by user: With Both, remove the save folder and capture; confirm it is recreated and contains the PNG.
- [ ] not yet run by user: Deny write access to the save folder and capture with Both; confirm full-path/system-detail alert and clipboard image. Choose Desktop and confirm one save retry plus persisted folder. Repeat with Cancel; restore permissions afterward.
- [ ] not yet run by user: Set a removable-drive save folder, unplug it and capture; confirm the save-folder alert.
- [ ] not yet run by user: Capture twice in one second; confirm the second filename has a collision suffix and the first file is preserved.
- [ ] not yet run by user: Where practical, test a driver reset or sleeping monitor; confirm capture recovers or shows the capture error after about three seconds without a hang.
- [ ] not yet run by user: Capture on a portrait monitor; confirm correct orientation and area.
- [ ] not yet run by user: Press Ctrl+Shift+6 and select; confirm no file, clipboard change or sound, and typing returns to the previous app (annotation is not available until 0040).
- [ ] not yet run by user: Press Ctrl+Shift+5 and select; confirm the unchanged 0030 placeholder behavior until 0034.
