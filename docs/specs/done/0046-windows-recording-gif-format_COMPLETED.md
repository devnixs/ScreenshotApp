# 0046 — Record to GIF on Windows, with a GIF quality setting

> Source: docs/PRD.md §7 item 2 (format; GIFs never record the microphone), item 6 ("Format and quality": GIF quality, conversion, "Couldn't create the GIF" fallback), §9 (`Recording … .gif` names), §10 Recording (Format picker, per-format Quality, disabled microphone toggle with "GIFs don't include sound."), §12 ("With the GIF format, ⌘⇧5 saves a looping `.gif` at the chosen quality, with no overlay pill or border in it."), §13.1 (WIC for GIF), §13.2 (points → DIPs, Retina → monitor scale factor), §13.6 (recording on Windows), §13.9 (the GIF rule from §10 applies unchanged). Windows port of the macOS spec `docs/specs/todo/0024-recording-gif-format_TODO.md`.
> Depends on: 0045-windows-recording-mp4-quality (record-to-temp → convert pipeline, `RecordingExportPlan`, `Converting` lifecycle stage, `RecordingTempFiles`, save-original fallback, `RecordingFailure.Title`, `RecordingSettingsPresentation`), and through it 0026 (`CaptureKind`, `FileNamer`), 0027 (`RecordingFormat`, `GifQuality`), 0034 (recording core), 0036 (`RecordingConfiguration.Scale`), 0037 (`MicrophoneCapturePolicy`, Recording tab).

## Context

When this spec starts (after 0045):

- `Snapper.Core/Settings/AppSettings` (0027) stores `RecordingFormat` (default `Mp4`) and `GifQuality` (default `Medium`), but the Settings window only shows the **"MP4 quality"** radio group that 0045 added to the Recording tab (0037).
- `Snapper.Core/Recording/RecordingExportPlan.Make(format, mp4Quality, gifQuality, pixelWidth, pixelHeight, scaleFactor)` (0045) returns an MP4 plan with `Format = Mp4` even when `format == Gif` (placeholder marked `// 0046`). `RecordingTranscode` has only `Mp4(int AverageBitRate)`.
- `RecordingLifecycle` (0034 + 0045) has the `Converting` stage with `Convert(path)`, `ConversionFinished`, `ConversionFailed(failure, savedOriginal)` and `SaveOriginal(path)`. `Recording/RecordingSession` (Windows) runs `Recording/Mp4Transcoder` for `Convert` and saves the original MP4 on failure with `RecordingFailure.Compression(...)` ("Couldn't compress the recording").
- `Snapper.Core/Recording/MicrophoneCapturePolicy.ShouldAttemptMicrophone` (0037) already returns `false` for `OutputFormat == RecordingFormat.Gif`, but `RecordingSession` always passes `RecordingFormat.Mp4` ("0046 replaces this with the snapshotted format", 0037 spec line 109).
- `Snapper.Core/Support/CaptureKind` (0026) already has `Screenshot`, `RecordingMp4`, `RecordingGif` with prefixes and extensions (`.gif` naming and collisions are tested by 0026), but nothing maps `RecordingFormat` to `CaptureKind`. 0034/0045 reserve recording paths as MP4.
- `RecordingConfiguration` (0034, extended by 0036) carries `Scale`, the starting monitor's scale factor (0036 spec line 46 for windows; the rectangle path uses the selection's monitor).

PRD §7 item 6, GIF quality (with §13.2 applied: "1×" means pixel size ÷ the monitor's scale factor, i.e. DIPs):

- **High**: 15 fps at native pixels. **Medium** (default): 10 fps at 1×. **Low**: 8 fps at 1×, then scaled down so the longest side is at most 640 px. Keep the aspect ratio, round to whole pixels (minimum 1 × 1), never upscale. The GIF loops forever, with frame delays taken from the real frame timing.
- A conversion failure saves the original as `Recording … .mp4`, reveals it in File Explorer, and shows **"Couldn't create the GIF"** with the error.
- §7 item 2: GIF recordings never capture the microphone. §10/§13.9: with GIF, the *Record microphone* toggle is disabled, keeping its value, with the note **"GIFs don't include sound."**
- §9: `Recording 2026-11-02 at 19.18.04.gif`.

Capture is unchanged: WGC → `Mp4Writer` writes H.264 at native pixels, ≤ 30 fps, cursor on, into a temp MP4 (0045). The GIF is made from it after Stop.

### macOS reference implementation (port this, don't redesign it)

The macOS spec 0024 is implemented before this batch. Port its decision logic and tests:

- `Snapper/Recording/GIFEncodingSettings.swift` (`forQuality`, `outputPixelSize`; spec `docs/specs/todo/0024-recording-gif-format_TODO.md:31-41`) and `SnapperTests/GIFEncodingSettingsTests.swift` (spec `:106-112`).
- `Snapper/Recording/GIFFrameSampler.swift` (spec `:42-52`) and `SnapperTests/GIFFrameSamplerTests.swift` (spec `:113-120`).
- The `.gif` case of `RecordingOutputPlan` (spec `:53`) and its test (spec `:121`).
- `MicrophoneCapturePolicy` format input (spec `:54`) — already ported by 0037.
- `RecordingFailure.gifConversion` (spec `:55`, test `:124`).
- `Snapper/Settings/RecordingSettingsPresentation.swift` (spec `:56-59`, test `:123`).
- `Snapper/Recording/GIFEncoder.swift` (spec `:63-68`) and the `RecordingSession` `.convert` switch (spec `:69-71`) — re-implemented here with Media Foundation + WIC instead of AVFoundation + ImageIO.

Differences from macOS (deliberate, all Windows-specific):

- **Timestamps are integer 100-ns ticks** (Media Foundation `hns`, same unit as `TimeSpan.Ticks`), not `Double` seconds. Slot math is done in integers with a 1 ms tolerance so rounded MF timestamps (333 333, 666 667, …) hit their slots exactly; see the sampler rules.
- **Rounding is half away from zero** everywhere (`MidpointRounding.AwayFromZero`), matching Swift's `.rounded()`. C#'s default `Math.Round` is banker's rounding and would give different sizes (e.g. 500.5 → 500) — don't use it.
- **Delay rounding carries its error forward** (target-end minus emitted-so-far), so a clamped 2 cs delay is paid back by the next frame and the total always equals the rounded real duration. macOS rounds each frame's start/end independently and clamps afterwards; the results are identical whenever no clamp fires.
- **Delays above 65 535 cs** (the GIF 16-bit limit, ~10.9 min of a static screen) are split into repeated copies of the same frame. macOS doesn't need this (ImageIO handles it).
- **Separate `GifQuality` enum** (0027) instead of the shared `RecordingQuality`.
- **The macOS sampler test "31 uniform timestamps (0…1 s) → 10 frames, sum 100"** is inconsistent: a frame at exactly t = 1.0 s hits slot 10 and is selected (11 frames, sum 110). Port it with **30** timestamps (t = k/30 s, k = 0…29), which gives the intended 10 frames and sum 100.
- No headless encoder test: `Snapper.Core.Tests` is a cross-platform net10.0 project with no Windows APIs, so `GifEncoder` is verified manually. Everything decidable without WIC/MF (sizes, sampling, delays, frame cursor, crop, loop-extension bytes) is in Core and tested.

## User story

As a Snapper user on Windows, I can switch the recording format to GIF in Settings → Recording and pick High, Medium, or Low, and Ctrl+Shift+5 then saves a looping `.gif` that I can drop straight into a chat or a pull request.

## Scope & changes

### Pure logic — `Snapper.Core` (xUnit-tested)

- **New `windows/src/Snapper.Core/Recording/GifConversionPlan.cs`** (port of `GIFEncodingSettings.swift` + the size part of the plan):
  ```csharp
  public readonly record struct GifEncodingSettings(int FramesPerSecond, bool UsesDipSize, int? MaxLongestSide)
  {
      public static GifEncodingSettings ForQuality(GifQuality quality);   // High (15,false,null), Medium (10,true,null), Low (8,true,640)
      public (int Width, int Height) OutputPixelSize(int sourceWidth, int sourceHeight, double scaleFactor);
  }
  public sealed record GifConversionPlan(GifEncodingSettings Settings, int Width, int Height)
  {
      public static GifConversionPlan Make(GifQuality quality, int pixelWidth, int pixelHeight, double scaleFactor);
  }
  ```
  - `OutputPixelSize` (same steps as macOS `0024:41`): start from the source pixels. If `UsesDipSize && scaleFactor > 1`, divide both by `scaleFactor` and round. Then if `MaxLongestSide` is set and `max(w, h) > MaxLongestSide`, multiply both by `MaxLongestSide / (double)max(w, h)` and round again. Every rounding is `Math.Round(x, MidpointRounding.AwayFromZero)`. Finally clamp each side to `1..source`.
  - `scaleFactor ≤ 1` (or NaN/≤ 0) never changes the size. Never upscales. Non-positive `sourceWidth`/`sourceHeight` → `ArgumentOutOfRangeException`.
  - `GifConversionPlan.Make` = `ForQuality(quality)` + `OutputPixelSize(pixelWidth, pixelHeight, scaleFactor)`.
- **New `windows/src/Snapper.Core/Recording/GifFrameSampler.cs`** (port of `GIFFrameSampler.swift`, adapted to ticks):
  ```csharp
  public readonly record struct GifFrame(int SourceIndex, long SourceTimestamp, int DelayCentiseconds);
  public static class GifFrameSampler
  {
      public const long TicksPerSecond = 10_000_000;
      public const long SlotToleranceTicks = 10_000;      // 1 ms
      public const int MinDelayCentiseconds = 2;
      public const int MaxDelayCentiseconds = 65_535;
      public static IReadOnlyList<GifFrame> Plan(IReadOnlyList<long> timestamps, long durationTicks, int framesPerSecond);
  }
  ```
  - `timestamps`: source presentation times in 100-ns ticks. The input may be unsorted (compressed samples can arrive in decode order) and contain duplicates: sort ascending and drop duplicates first; `SourceIndex` is the index in the **sorted, de-duplicated** list. `durationTicks`: the MP4's `MF_PD_DURATION` (≤ 0 means unknown → treat as 0, so the last-frame rule below applies). `framesPerSecond` must be > 0 (`ArgumentOutOfRangeException`).
  - **Selection** (absolute slots, as macOS): the first timestamp is always selected. After selecting a frame at `t`, the next slot index is `n = floor((t + SlotToleranceTicks) * fps / TicksPerSecond) + 1` and the slot time is `n * TicksPerSecond / fps` (compare without division: a frame at `u` is selected when `(u + SlotToleranceTicks) * fps >= n * TicksPerSecond`). Use `long`/`Int128`-safe math (`t * fps` fits in `long` for any realistic duration; document the bound).
  - **Delays**: times are relative to the first selected frame `t0`. Frame `k` ends at the next selected frame's time; the last frame ends at `max(durationTicks, tLast + TicksPerSecond / fps)` (both absolute, then made relative). Convert with **carried rounding**: `targetEndCs_k = RoundCs(end_k − t0)` where `RoundCs(ticks) = (long)Math.Round(ticks / 100_000.0, MidpointRounding.AwayFromZero)`; `delay_k = max(MinDelayCentiseconds, targetEndCs_k − emittedCs)`; `emittedCs += delay_k`. So the sum of all delays equals `RoundCs(totalSpan)` unless clamps can't be paid back by later frames.
  - **Long delays**: a delay above `MaxDelayCentiseconds` becomes several `GifFrame`s with the same `SourceIndex`/`SourceTimestamp`: as many 65 535 cs frames as needed, then the remainder. If the remainder would be 1, the last full piece becomes 65 534 and the remainder 2, so every piece is in `2..65 535` and the sum is unchanged.
  - Empty `timestamps` → empty list (the encoder treats it as a failure).
- **New `windows/src/Snapper.Core/Recording/GifFrameCursor.cs`** (pure; pairs decoded samples with the plan so `GifEncoder` stays simple):
  ```csharp
  public sealed class GifFrameCursor
  {
      public GifFrameCursor(IReadOnlyList<GifFrame> plan);
      public IReadOnlyList<GifFrame> Take(long decodedTimestamp);   // plan frames to encode with this decoded image
      public IReadOnlyList<GifFrame> Remaining();                    // after end of stream: frames still owed
      public bool IsComplete { get; }
  }
  ```
  `Take(t)` returns, in order, every not-yet-emitted plan frame whose `SourceTimestamp <= t + GifFrameSampler.SlotToleranceTicks` (usually 0 or 1 frames; several for split long delays). Decoded timestamps that go backwards are ignored (return empty). `Remaining()` returns the frames not yet emitted (the encoder writes them with the last decoded image; if there is none, the conversion fails).
- **New `windows/src/Snapper.Core/Recording/GifFrameCrop.cs`** (pure): `static PixelRect CropFor(int decodedWidth, int decodedHeight, PixelRect? displayAperture, int expectedWidth, int expectedHeight)`. Media Foundation may decode H.264 into a padded frame (e.g. 1920×1088 for 1920×1080, or 16-aligned sizes for odd window sizes). Rule: use `displayAperture` (from `MF_MT_MINIMUM_DISPLAY_APERTURE`) when present and inside the decoded frame; else `(0, 0, min(expectedWidth, decodedWidth), min(expectedHeight, decodedHeight))`. Never returns an empty rect (min 1×1). `PixelRect` is 0028's type.
- **New `windows/src/Snapper.Core/Recording/GifMetadata.cs`** (pure constants, so the encoder's bytes are tested):
  - `static ReadOnlySpan<byte> NetscapeApplicationId` = ASCII `"NETSCAPE2.0"` (11 bytes).
  - `static byte[] LoopForeverData()` = `{ 3, 1, 0, 0, 0 }` (sub-block length 3, sub-block id 1, loop count 0 little-endian = forever, terminator).
  - `const ushort DisposalDoNotDispose = 1`.
- **`windows/src/Snapper.Core/Recording/RecordingExportPlan.cs`** (0045): add `public sealed record Gif(GifConversionPlan Plan) : RecordingTranscode;`. `Make(Gif, …)` returns `new RecordingExportPlan(RecordingFormat.Gif, new Gif(GifConversionPlan.Make(gifQuality, pixelWidth, pixelHeight, scaleFactor)))`; `mp4Quality` is ignored for GIF. Remove the 0045 placeholder and its `// 0046` comment. A GIF plan always `Converts`. Add `CaptureKind FinalCaptureKind => Format.ToCaptureKind()`.
- **`windows/src/Snapper.Core/Settings/RecordingFormat.cs`** (0027): add the mapping extension `public static CaptureKind ToCaptureKind(this RecordingFormat format)` → `Mp4 → CaptureKind.RecordingMp4`, `Gif → CaptureKind.RecordingGif` (unknown value → `ArgumentOutOfRangeException`). 0026 assigned this mapping to "0027 or the first recording spec that needs it"; neither added it, so it lands here. Replace any hard-coded MP4 recording kind in `AppCoordinator.StartRecording`/`RecordingSession` (0034 names it `CaptureKind.Recording`; 0026's enum value is `RecordingMp4`) with `plan.FinalCaptureKind`. The save-original fallback keeps using `CaptureKind.RecordingMp4` explicitly.
- **`windows/src/Snapper.Core/Recording/RecordingTempFiles.cs`** (0045): add `static string OutputFor(string tempRoot, Guid id, RecordingFormat format)` → `<tempRoot>\Snapper\Snapper-<id:N>-out.mp4` or `…-out.gif`. The capture file stays `.mp4`.
- **`RecordingFailure`** (0034/0045, Core): add `static RecordingFailure GifConversion(string message)` with `Title` **"Couldn't create the GIF"**.
- **`windows/src/Snapper.Core/Settings/RecordingSettingsPresentation.cs`** (0045, extended; port of the macOS presentation):
  - `IReadOnlyList<(RecordingFormat Value, string Title)> FormatOptions` → MP4, GIF.
  - `static IReadOnlyList<string> QualityTitles` → "High", "Medium", "Low" (index 0/1/2).
  - `static int QualityIndex(RecordingFormat format, Mp4Quality mp4, GifQuality gif)` → the selected format's own level.
  - `static (Mp4Quality Mp4, GifQuality Gif) WithQualityIndex(RecordingFormat format, int index, Mp4Quality mp4, GifQuality gif)` → changes only the selected format's level (index outside 0..2 → `ArgumentOutOfRangeException`).
  - `static bool MicrophoneToggleEnabled(RecordingFormat format)` → `Mp4` true, `Gif` false.
  - `static string? MicrophoneNote(RecordingFormat format)` → `Gif` "GIFs don't include sound.", `Mp4` null.
  - `static string QualityCaption(RecordingFormat format)` → MP4: "Medium and Low re-encode after you stop, for smaller files." (0045's `Mp4QualityCaption`, keep the constant); GIF: "High: 15 fps, full size. Medium: 10 fps, 1×. Low: 8 fps, up to 640 px."
  - Keep 0045's `Mp4QualityOptions` if anything still uses it; otherwise remove it with its test.

### Platform glue — `Snapper.Windows` (manually verified)

- **New `windows/src/Snapper.Windows/Recording/GifEncoder.cs`**: `static Task EncodeAsync(string input, string output, GifConversionPlan plan, CancellationToken token)`, run with `Task.Run` (thread pool = MTA; never on the dispatcher, no UI). MF is started app-wide by 0034 (if not, `MFStartup`/`MFShutdown` around the call, as 0045 does). Every COM object is released in `finally`; per-frame WIC objects are released at the end of each frame so memory stays bounded for long recordings.
  - **Pass 1 — timestamps (no decoding):** `MFCreateSourceReaderFromURL(input, null)`; deselect all streams, select `MF_SOURCE_READER_FIRST_VIDEO_STREAM`, don't set a media type (compressed samples, nothing is decoded). `ReadSample` until end of stream, collecting each sample's time (`IMFSample.GetSampleTime`). Read the duration with `GetPresentationAttribute(MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION)` (VT_UI8; missing → 0). Run `GifFrameSampler.Plan(times, duration, plan.Settings.FramesPerSecond)`. Empty → throw `InvalidOperationException("The recording has no video frames.")`.
  - **Pass 2 — decode:** a new reader with attributes `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = TRUE` (and `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = TRUE`); select only the first video stream; `SetCurrentMediaType` to `MFMediaType_Video` / `MFVideoFormat_RGB32` (BGRX). Read back the current type: `MF_MT_FRAME_SIZE`, optional `MF_MT_MINIMUM_DISPLAY_APERTURE` → `GifFrameCrop.CropFor(..., expected = the temp MP4's recorded size)`.
  - For each decoded sample: `cursor.Take(sampleTime)`. If it returns frames, convert the sample to a WIC bitmap once: `ConvertToContiguousBuffer` → lock with `IMF2DBuffer.Lock2D` when available (scanline-0 pointer + pitch, **pitch may be negative** for bottom-up RGB), else `IMFMediaBuffer.Lock` with `MF_MT_DEFAULT_STRIDE` (also possibly negative). Copy the crop rect row by row into a managed top-down BGRX buffer, then `IWICImagingFactory.CreateBitmapFromMemory(w, h, GUID_WICPixelFormat32bppBGR, stride, buffer)`. Keep it as "last decoded image" (release the previous one). Write one GIF frame per returned `GifFrame`. After end of stream, write `cursor.Remaining()` with the last decoded image (none → throw "The recording has no video frames.").
  - **Per GIF frame:** if the crop size ≠ `plan.Width × plan.Height`, `IWICBitmapScaler.Initialize(source, plan.Width, plan.Height, WICBitmapInterpolationModeFant)`; otherwise use the bitmap as-is. `IWICImagingFactory.CreatePalette` → `IWICPalette.InitializeFromBitmap(scaled, 256, fAddTransparentColor: false)`. `IWICFormatConverter.Initialize(scaled, GUID_WICPixelFormat8bppIndexed, WICBitmapDitherTypeNone, palette, 0, WICBitmapPaletteTypeCustom)` (no dithering: screen content is mostly flat color, and dithering shimmers between frames and grows files). The palette is per frame (local color table), so every frame keeps its own best 256 colors.
  - **Encoder:** `IWICStream.InitializeFromFilename(output, GENERIC_WRITE)` → `CreateEncoder(GUID_ContainerFormatGif)` → `Initialize(stream, WICBitmapEncoderNoCache)`. Container metadata via `IWICBitmapEncoder.GetMetadataQueryWriter`: `/appext/application` = `GifMetadata.NetscapeApplicationId` (VT_UI1|VT_VECTOR), `/appext/data` = `GifMetadata.LoopForeverData()` (VT_UI1|VT_VECTOR), `/logscrdesc/Width` and `/logscrdesc/Height` = plan size (VT_UI2). Per frame: `CreateNewFrame` → `Initialize(propertyBag)` → `SetSize(plan.Width, plan.Height)` → `SetPixelFormat(8bppIndexed)` → `SetPalette(palette)` → frame metadata writer: `/grctlext/Delay` = `DelayCentiseconds` (VT_UI2), `/grctlext/Disposal` = `GifMetadata.DisposalDoNotDispose` (VT_UI1) → `WriteSource(converter, null)` → `Commit()`. After the last frame, `IWICBitmapEncoder.Commit()`, then release the stream (closes the file). Any HRESULT failure throws (`Marshal.ThrowExceptionForHR` / CsWin32's `ThrowOnFailure`), and the caller deletes the partial output. Check `token` between frames.
  - Full frames only (no inter-frame difference/transparency optimization).
- **`windows/src/Snapper.Windows/Recording/RecordingSession.cs`** (0034/0045):
  - Build the plan with `RecordingExportPlan.Make(format, mp4Quality, gifQuality, w, h, configuration.Scale)` from the settings snapshot taken at start (0045). Rectangle: `RecordingConfiguration` output size and the selection monitor's scale (if 0034's rectangle configuration doesn't set `Scale`, set it there from the start monitor's `DisplayInfo` scale). Window: 0036's fixed output size and `Scale`. GIF sizes are fixed from the start; moving the window to another monitor later doesn't change them (0036 letterboxes inside the fixed frame).
  - Microphone: pass the snapshotted `RecordingFormat` as `MicrophoneCaptureInputs.OutputFormat` (replacing the hard-coded `Mp4` from 0037), so a GIF recording never opens `MicrophoneSource` and `Mp4Writer` gets no audio stream.
  - `Convert(path)`: switch on `plan.Transcode` — `Mp4` → `Mp4Transcoder` (0045, unchanged), `Gif` → `GifEncoder.EncodeAsync(path, RecordingTempFiles.OutputFor(..., Gif), gifPlan, token)`, then move to the reserved final `.gif` path (re-resolve with the 0026 unique-path helper and `CaptureKind.RecordingGif` on collision). Failure path as 0045 (delete `-out.gif`, move the capture into the save folder as `CaptureKind.RecordingMp4`), but with `RecordingFailure.GifConversion(ex.Message)`.
  - The write-failure partial file is saved as MP4 without converting (0045's `SaveOriginal`, unchanged).
- **`App/AppCoordinator.StartRecording`** (0034/0045): reserve the final path with `plan.FinalCaptureKind` (`.gif` for GIF). For window recordings, where the plan is made after the window config is built (0045), reserve with `settings.RecordingFormat.ToCaptureKind()` — the format doesn't depend on the size.
- **Settings window, Recording tab** (`Settings/SettingsWindow.xaml(.cs)`, tab from 0037, group from 0045), top to bottom:
  - **Format**: two horizontal radio buttons "MP4" / "GIF" from `FormatOptions`, bound to `AppSettings.RecordingFormat`.
  - **Quality**: three radio buttons "High" / "Medium" / "Low" (`QualityTitles`), replacing 0045's "MP4 quality" group, same style. Checked index = `QualityIndex(...)`; a click applies `WithQualityIndex(...)` and writes the result to `AppSettings.Mp4Quality`/`GifQuality`. Re-evaluate when `RecordingFormat` changes (switching formats shows the other format's own level). `QualityCaption(format)` below in secondary text.
  - **Record microphone** checkbox (0037): `IsEnabled = MicrophoneToggleEnabled(format)`; below it the `MicrophoneNote(format)` in secondary text when non-null. Switching formats never writes `RecordMicrophone`. The 0037 denied-permission note with **Open Privacy Settings** still follows its own rules; when the format is GIF and the denied note would show, show both notes (GIF note first) — the button stays usable.
  - Changes save immediately and don't affect a recording in progress (snapshot at start).
- **`NativeMethods.txt`**: add what 0034/0037/0045 didn't already list: `MF_PD_DURATION`, `MF_MT_MINIMUM_DISPLAY_APERTURE`, `MF_MT_DEFAULT_STRIDE`, `MFVideoFormat_RGB32`, `MFVideoArea`, `IMFMediaBuffer`, `IMF2DBuffer`, `CLSID_WICImagingFactory`, `IWICImagingFactory`, `IWICBitmap`, `IWICBitmapSource`, `IWICBitmapScaler`, `IWICPalette`, `IWICFormatConverter`, `IWICBitmapEncoder`, `IWICBitmapFrameEncode`, `IWICMetadataQueryWriter`, `IWICStream`, `GUID_ContainerFormatGif`, `GUID_WICPixelFormat8bppIndexed`, `GUID_WICPixelFormat32bppBGR`, `WICBitmapInterpolationMode`, `WICBitmapDitherType`, `WICBitmapPaletteType`, `WICBitmapEncoderCacheOption`, `PROPVARIANT`, `CoCreateInstance`. `PropVariant` construction for VT_UI1|VT_VECTOR / VT_UI2 / VT_UI1 goes in a small helper `Interop/PropVariantHelper.cs` (Windows) that frees with `PropVariantClear`. If CsWin32 doesn't emit a GUID constant, define it in `Interop/WicGuids.cs` (or 0045's `Interop/MediaFoundationGuids.cs`) with a comment citing the SDK header.

### Docs / project

- New files are picked up by the SDK-style projects (no project edits besides `NativeMethods.txt`).
- No PRD or AGENTS.md change: PRD §7, §9, §10, §12, §13.6 and §13.9 already describe this behavior.

## Acceptance criteria

- [ ] Settings → Recording shows **Format** (MP4 / GIF) and **Quality** (High / Medium / Low) with the format's caption. Quality shows the selected format's own level; switching formats switches the shown level; defaults MP4 High, GIF Medium; all persist in `%APPDATA%\Snapper\settings.json` across restarts.
- [ ] With GIF, Ctrl+Shift+5 → Stop saves `Recording <start>.gif` in the save folder and File Explorer opens with it selected; it loops forever in a browser (Edge) and in Photos.
- [ ] GIF **High** is native pixels at ~15 fps; **Medium** is pixel size ÷ the monitor scale at ~10 fps; **Low** is that, capped at 640 px on the longest side, at ~8 fps. Playback length matches the recording within ~0.1 s.
- [ ] The GIF contains neither the pill nor the border.
- [ ] With GIF, the *Record microphone* checkbox is disabled (keeping its value) with "GIFs don't include sound.", and no microphone is opened during the recording (no microphone-in-use icon in the taskbar).
- [ ] During GIF conversion the 0045 conversion behavior applies: red tray dot, tray clicks and shortcuts do nothing, sign-out waits behind "Saving recording…", Cancel during recording leaves nothing.
- [ ] A GIF conversion failure saves the original `.mp4`, reveals it, and shows "Couldn't create the GIF" with the error.
- [ ] No `Snapper-*` files remain in `%TEMP%\Snapper` after success, failure, or cancel.

## Edge cases

- **Mixed DPI:** Medium/Low use the **starting** monitor's scale. A 400×300 DIP rectangle records 400×300 px at 100%, 600×450 at 150%, 800×600 at 200%; Medium gives 400×300 in all three cases. At 125% a 1001×501 px recording → 801×401 (half away from zero). A rectangle on a negative-origin monitor is unaffected (sizes only).
- **Window moved to a monitor with another scale** during recording: GIF size stays as computed at start; the content is letterboxed by 0036.
- **100% monitor:** High and Medium have the same pixel size; only fps differs.
- **Tiny selections:** 4×4 DIP at 200% → 8×8 px → Medium 4×4, Low 4×4 (no upscale); 2×2 px at 200% → 1×1; never 0.
- **Odd sizes:** the temp MP4 has even dimensions (0034); the GIF size derives from those even pixels. Decoded frames may be padded (1088 rows) — `GifFrameCrop` removes the padding before scaling, so no green/black bar appears at the bottom.
- **Portrait Low:** the cap applies to the height (800×1600 → 320×640).
- **Static screen:** WGC delivers frames only on change → few frames with long delays and the correct total length. A static screen for more than ~10.9 min → split delays (repeated frames).
- **Single frame:** one GIF frame lasting `max(duration, 1/fps)`.
- **Zero video frames** → "The recording has no video frames." → conversion failure → original saved (or deleted by 0034's empty-file rule) with "Couldn't create the GIF".
- **Minimized window** (§13.6): no frames while minimized → the previous GIF frame's delay covers the gap.
- **Decoded timestamps that differ slightly from the compressed ones** (decoder rounding): the cursor's 1 ms tolerance pairs them; any plan frames still owed at the end use the last decoded image.
- **Long High recordings** (full 4K monitor, minutes): large files and slow conversion are expected; memory stays bounded (one decoded frame + one WIC frame alive at a time); no timeout.
- **Microphone on → GIF → back to MP4**: the checkbox is checked again and enabled; the next MP4 recording records audio.
- **`settings.json` with GIF from before this spec**: now honored as GIF.
- **Save folder unwritable / removed** during conversion: as 0045 (final move fails → save-original → if that fails too, `Failed`, alert, temp deleted).

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`, xUnit):
  - `GifConversionPlanTests.cs` (port of `GIFEncodingSettingsTests.swift`, spec `0024:106-112`, plus Windows scales):
    - `ForQuality(High) == (15, false, null)`; `Medium == (10, true, null)`; `Low == (8, true, 640)`.
    - High: 2880×1800 @2.0 → 2880×1800; 2880×1620 @1.5 → 2880×1620.
    - Medium: 2880×1800 @2.0 → 1440×900; 1000×500 @1.0 → 1000×500; 1001×501 @2.0 → 501×251 (half away from zero; banker's would give 500×250); 1500×900 @1.5 → 1000×600; 1001×501 @1.25 → 801×401; 1750×700 @1.75 → 1000×400.
    - Low: 2880×1800 @2.0 → 640×400; 1000×500 @1.0 → 640×320; 800×1600 @1.0 → 320×640; 300×200 @2.0 → 150×100 (under the cap, untouched); 3840×2160 @1.5 → 640×360.
    - Tiny: 8×8 @2.0 Low → 4×4; 2×2 @2.0 Medium → 1×1; 1×1 @1.5 Medium → 1×1; every result ≥ 1×1.
    - Scale 1.0, 0.75, 0 and NaN never change the size (no upscale).
    - `OutputPixelSize(0, 10, 1)` throws `ArgumentOutOfRangeException`.
    - `GifConversionPlan.Make(Medium, 2880, 1800, 2.0) == new(ForQuality(Medium), 1440, 900)`.
  - `GifFrameSamplerTests.cs` (port of `GIFFrameSamplerTests.swift`, spec `0024:113-120`; times written in ticks, `T(s) = (long)Math.Round(s * 10_000_000)`):
    - Uniform 30 fps, 30 timestamps `T(k/30)` (k = 0…29), duration `T(1.0)`, fps 10 → 10 frames, source indices 0, 3, …, 27, every delay 10, sum 100.
    - Same input, fps 15 → 15 frames (indices 0, 2, …, 28), each delay ∈ {6, 7}, sum 100.
    - Same as the fps 15 case but with timestamps **truncated** (`k * 10_000_000 / 30` integer division) → same indices (the 1 ms tolerance absorbs the 1-tick error).
    - VFR: `[0, 0.5, 2.0]` s, duration 3.0 s, fps 10 → 3 frames, delays `[50, 150, 100]`.
    - Burst: `[0, 0.01, 0.02, 0.03, 0.2]` s, duration 0.2 s, fps 10 → indices `[0, 4]`, delays `[20, 10]`.
    - Non-zero start: `[0.35, 0.40, 0.55]` s, duration 0.8 s, fps 10 → all selected, delays `[5, 15, 25]` (relative to 0.35).
    - Clamp carried: `[0.095, 0.1]` s, duration 0.3 s, fps 10 → both selected; delays `[2, 19]` (the first target is 1 → clamped to 2, the second pays it back); sum 21 == round(100 × 0.205) (0.5 rounds up).
    - Accumulated error: 60 s at 30 fps (1800 timestamps `T(k/30)`), duration `T(60)`, fps 15 → 900 frames, sum exactly 6000 (per-frame rounding of 6.67 would give 6300), every delay ∈ {6, 7}.
    - Single frame: `[0]`, duration 0, fps 8 → one frame, delay 13 (round(12.5) = 13, half away from zero).
    - Long delay split: `[0]`, duration 700 s, fps 10 → frames `(0, 65535)`, `(0, 4465)`, both `SourceIndex == 0`; sum 70000.
    - Split with tiny remainder: `[0]`, duration `T(655.36)` (65536 cs) → `[65534, 2]` (sum 65536, every piece in `2..65535`).
    - Unsorted with duplicates: `[0.2, 0, 0.1, 0.1]` s, duration 0.3 s, fps 10 → indices `[0, 1, 2]` over the sorted list `[0, 0.1, 0.2]`, delays `[10, 10, 10]`.
    - Empty → empty list. `framesPerSecond == 0` → `ArgumentOutOfRangeException`.
  - `GifFrameCursorTests.cs`:
    - Plan with timestamps `[0, 1_000_000, 2_000_000]`: `Take(0)` → frame 0; `Take(333_333)` → empty; `Take(999_990)` → frame 1 (within 1 ms); `Take(2_000_000)` → frame 2; `IsComplete`.
    - Decoded stream skips a planned time (decoded `[0, 2_500_000]` for plan `[0, 1_000_000, 2_000_000]`) → `Take(2_500_000)` returns frames 1 and 2 in order.
    - Split long delay (two plan frames with the same timestamp) → one `Take` returns both.
    - Timestamp going backwards → empty, state unchanged.
    - End of stream with frames owed → `Remaining()` returns them; after `Take` of everything, `Remaining()` is empty.
  - `GifFrameCropTests.cs`: decoded 1920×1088, aperture (0,0,1920,1080) → 1920×1080; no aperture, expected 1920×1080 → (0,0,1920,1080); aperture outside the frame → falls back to expected; expected larger than decoded → clamped to decoded; result never empty.
  - `GifMetadataTests.cs`: `NetscapeApplicationId` is the 11 ASCII bytes of "NETSCAPE2.0"; `LoopForeverData()` is `{3, 1, 0, 0, 0}`; `DisposalDoNotDispose == 1`.
  - `RecordingExportPlanTests.cs` (0045, update): `Make(Gif, Medium(mp4 any), Medium, 2880, 1800, 2.0)` → `Format == Gif`, `Transcode == new Gif(new GifConversionPlan(ForQuality(Medium), 1440, 900))`, `Converts`, `FinalCaptureKind == RecordingGif`; `mp4Quality` (all three) doesn't change a GIF plan; replace 0045's GIF-placeholder test. MP4 plans have `FinalCaptureKind == RecordingMp4`.
  - `RecordingFormatTests` (or inside `AppSettingsTests.cs` from 0027): `Mp4.ToCaptureKind() == RecordingMp4`, `Gif.ToCaptureKind() == RecordingGif`, `(RecordingFormat)9` throws.
  - `RecordingTempFilesTests.cs` (0045, extend): `OutputFor(root, id, Gif)` ends in `Snapper-<id:N>-out.gif`, `Mp4` in `-out.mp4`; both in `Directory(root)`.
  - `RecordingFailureTests.cs`: `RecordingFailure.GifConversion("x").Title == "Couldn't create the GIF"`, `.Message == "x"`.
  - `MicrophoneCapturePolicyTests.cs` (0037): confirm the existing all-favorable + `Gif` → false case exists; add it if missing.
  - `RecordingSettingsPresentationTests.cs` (0045, extend): `FormatOptions` MP4/GIF in order; `QualityTitles` High/Medium/Low; `QualityIndex(Mp4, Low, High) == 2`, `QualityIndex(Gif, Low, High) == 0`; `WithQualityIndex(Gif, 2, High, Medium) == (High, Low)` (MP4 untouched) and `WithQualityIndex(Mp4, 1, High, Medium) == (Medium, Medium)`; index 3 throws; `MicrophoneToggleEnabled` Mp4 true / Gif false; `MicrophoneNote` Gif "GIFs don't include sound." / Mp4 null; `QualityCaption` exact strings for both formats.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed (both run on macOS).
- **Manual verification** (Windows 11; x64, and Arm64 if available; can't be automated):
  1. Settings → Recording: Format MP4, Quality High, caption "Medium and Low re-encode…". Click GIF → Quality shows Medium, the GIF caption appears, *Record microphone* is disabled with "GIFs don't include sound.". Click MP4 → Quality shows High; the checkbox is enabled with its previous value.
  2. GIF Medium on a 150% monitor: Ctrl+Shift+5, drag a rectangle (the readout shows ~400×300), drag a window around inside it for ~5 s, Stop → after a short pause Explorer selects `Recording … .gif`. Open it in Edge: it loops forever. File Properties → Details: 400×300 (not 600×450). Its length is ~5 s (count, or step through in an editor).
  3. GIF High, same area → 600×450 px, smoother motion, larger file.
  4. GIF Low, full-screen selection on a 1920×1080 (or larger) monitor → longest side 640 px.
  5. Repeat step 2 on a 100% monitor (if available) → 400×300; on a negative-origin monitor (left of the primary) → correct content and size.
  6. Window mode (Space) with GIF Medium → the GIF shows only that window, with no pill and no border; drag the window to a monitor with another scale mid-recording → GIF size unchanged, content letterboxed.
  7. *Record microphone* on, format GIF: during recording no microphone-in-use icon appears in the taskbar; switch to MP4 and record → the icon appears and the MP4 has audio.
  8. GIF High, record ~30 s of a full high-DPI monitor, Stop, then immediately press Ctrl+Shift+4/5/6 and left/right-click the tray icon → nothing happens; tray is the red dot "Snapper — Recording" until Explorer selects the GIF.
  9. GIF, record, press Cancel on the pill → no new file in the save folder; `%TEMP%\Snapper` has no `Snapper-*` file.
  10. Static screen: GIF Medium, record an unchanging area for ~20 s → small GIF with few frames that still lasts ~20 s.
  11. Failure path: GIF, start recording, make the save folder read-only (deny Write for your user) before Stop → "Couldn't create the GIF" (or the recording error if the original can't be saved either); restore permissions. Alternatively use a USB save folder removed before Stop.
  12. Restart Snapper → Format GIF and each format's quality level are kept.

## Out of scope

- The temp-file pipeline, MP4 quality, the `Converting` lifecycle stage, shutdown/exit behavior during conversion, and the stale-temp-file and forced-shutdown questions — **0045**.
- Custom palettes, global palettes, dithering options, inter-frame optimization (transparency/diff frames), fps or size sliders, trimming, GIF to clipboard, conversion progress UI.
- Capturing frames directly to GIF without the temp MP4.
- Packaging and the full §13.11 acceptance pass — **0047-windows-packaging-release**.


## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**, and `dotnet test windows/Snapper.slnx` passed **1,358 tests** with **0 failures and 0 skips** (62 added tests). Logs: `/tmp/snapper-0046-build.log` and `/tmp/snapper-0046-test.log`.

Implemented the two-pass Media Foundation/WIC encoder, per-format settings, GIF microphone gating, format-aware output publication and stale-GIF-output cleanup. Core tests cover sizing at mixed monitor scales, sorted presentation timestamps, carried rounding and long-delay splitting, decoder cursor/crop/signed-stride rows, loop metadata, capture-kind mapping, settings presentation and GIF publication collisions. The existing favorable-input GIF microphone test remains green.

Implementation details adapted to the delivered 0045 APIs:

- `RecordingOutputFiles` accepts a capture kind for converted-file publication and collision re-resolution; save-original fallback keeps the MP4 default. Partial `-out.gif` files are recognized as scratch output by startup recovery.
- Microphone gating uses the requested format snapshot even if temporary-directory failure falls back to recording an MP4, so a GIF request never opens the microphone. Switching the Settings format does not write its microphone value.
- Native rows use the actual signed `IMF2DBuffer` pitch or `MF_MT_DEFAULT_STRIDE` and a tested Core row-copy helper. Decoded-frame padding is cropped before WIC scaling; backwards decoded timestamps are ignored, and the last decoded sample supplies any frames still owed at end of stream. Per-frame WIC objects are released, and source pixel memory remains pinned until its WIC bitmap is released.
- The encoder uses canonical `/appext/Application` and `/appext/Data` query names from [Microsoft's WIC metadata reference](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-native-image-format-metadata-queries), rather than the lowercase spellings in the design sketch. Signed RGB stride follows [Microsoft's image-stride reference](https://learn.microsoft.com/en-us/windows/win32/medfound/image-stride). No product behavior changed, and no PRD or AGENTS.md edit was needed.

**Native GIF encoding, capture, audio suppression, playback and GUI behavior have not been run on Windows.** All Windows 11 manual checks remain pending (x64, and Arm64 if available):

- [ ] not yet run by user: In Recording settings, confirm MP4/High defaults, GIF/Medium default, exact captions, disabled microphone with the GIF note, retained microphone choice and independent per-format quality levels when switching back.
- [ ] not yet run by user: On a 150% monitor, record about 400×300 DIPs for five seconds with GIF Medium. Confirm Explorer selects the GIF, it is about 400×300 pixels, loops in Edge/Photos and lasts about five seconds.
- [ ] not yet run by user: Repeat the area with GIF High; confirm about 600×450 pixels, smoother motion and a larger file.
- [ ] not yet run by user: Record a full monitor with GIF Low; confirm the longest output side is 640 pixels or smaller without upscaling.
- [ ] not yet run by user: Repeat Medium on a 100% monitor and a negative-origin monitor; confirm correct content and native-to-DIP sizing.
- [ ] not yet run by user: Record a window with GIF Medium, then move/resize it across monitors with different scales. Confirm fixed GIF dimensions, expected letterboxing, window-only content, no pill/border and no padded bottom strip.
- [ ] not yet run by user: With the stored microphone option on, record GIF and confirm no microphone-in-use icon. Switch to MP4 and confirm microphone capture resumes.
- [ ] not yet run by user: Stop a long, full high-DPI GIF recording and immediately try the three shortcuts and both tray buttons. Confirm they do nothing during conversion, the tray remains red, and normal controls return after Explorer reveals the GIF.
- [ ] not yet run by user: Cancel a GIF recording; confirm no final file and no `Snapper-*` temporary files remain.
- [ ] not yet run by user: Record an unchanging screen for about twenty seconds; confirm the GIF is small, retains the held frame and has the correct total duration.
- [ ] not yet run by user: Make the destination unwritable or remove its drive before Stop; confirm the GIF failure title and original-MP4 fallback when saving remains possible. Restore permissions and confirm temporary output cleanup.
- [ ] not yet run by user: Restart Snapper and confirm the format and both quality levels persist.
- [ ] not yet run by user: Sign out or shut down during GIF conversion; confirm the saving shutdown block. If forcing session end, confirm the original MP4 is preserved, and the next launch removes stale GIF conversion scratch output.
- [ ] not yet run by user: Optionally record a static area for more than eleven minutes; confirm split long GIF delays retain the full playback duration and loop correctly.
