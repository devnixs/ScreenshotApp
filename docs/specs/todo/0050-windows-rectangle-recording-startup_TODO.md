# 0050 — Fix immediate Windows rectangle recording startup failure

> Source: user bug report (2026-11-04), screenshot and follow-up; docs/PRD.md §7 items 1–6, §12, §13.1, §13.6, §13.11
> Depends on: 0034-windows-recording-core, 0035-windows-recording-overlay, 0037-windows-microphone-audio, 0045-windows-recording-mp4-quality, 0046-windows-recording-gif-format (all completed)

## Context

On Windows, the user presses Record, drags a rectangle, and immediately after releasing the mouse sees **“Couldn't record the screen” / “Value does not fall within the expected range.”** The failure is before any visible successful recording. Microphone setting, output format/quality, selected dimensions, DPI, GPU, and failing native operation are not known. Do not infer an HRESULT, codec defect, or invalid selection from this message alone.

PRD §7.1 requires recording to start immediately after selection. §7.2 requires native pixel dimensions rounded down to even counts, H.264 at 30 fps, and cursor capture. §13.6 supplies the Windows bitrate rule. This is a repair of existing behavior; no product decision or PRD change is required.

Relevant implementation:

- `windows/src/Snapper.Core/Recording/RecordingConfiguration.cs:19`: rectangle configuration, monitor-local pixel crop, even dimensions, bitrate.
- `windows/src/Snapper.Windows/Recording/RecordingSession.cs:75`: preparation failures also use the reported alert title, so the screenshot does not prove writer creation was reached.
- `windows/src/Snapper.Windows/Recording/RecordingSession.cs:214`: device/microphone acquisition, writer creation, BeginWriting, capture source creation and StartCapture; line 274 reduces failures to their message.
- `windows/src/Snapper.Windows/Recording/Mp4Writer.cs:63`: MTA startup, hardware creation and CPU fallback. Hardware failure logging currently retains only a message; the BeginWriting retry does not log the first failure. `Create` at line 128 configures media types, D3D manager, allocator, and optional AAC stream; `ResetForFallback` at line 216 releases the sink before deleting the attempted output.
- `windows/src/Snapper.Windows/Recording/CaptureFrameSource.cs:34`: WGC item, frame pool, session and cursor setup; line 71 starts capture.
- `windows/src/Snapper.Windows/Interop/MediaFoundationRuntime.cs:15`: MF startup; line 30 packs media-type sizes/rates.
- Reference semantics: `Snapper/Recording/RecordingConfigurationBuilder.swift:28`, `Snapper/Recording/RecordingLifecycle.swift`, and their `SnapperTests/RecordingConfigurationBuilderTests.swift` / `RecordingLifecycleTests.swift`. Windows pixel coordinates and Media Foundation negotiation remain platform-specific; do not transplant SCK APIs.

Microsoft documents that sink-writer input negotiation may insert an encoder/converter and can fail for unsupported formats: [IMFSinkWriter::SetInputMediaType](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-imfsinkwriter-setinputmediatype). This is a candidate investigation point, not a confirmed diagnosis.

## User story

As a Windows Snapper user, I can drag a rectangle and immediately record it, then stop and open a playable recording instead of receiving a generic range error.

## Scope & changes

- **Platform investigation and repair:** reproduce first with MP4 High and microphone off, then the user's settings if available. Trace preparation, device acquisition, media-type attributes, AddStream, SetInputMediaType, allocator creation, BeginWriting, WGC setup/StartCapture, and first frame submission. Record the exact failing operation, exception type, HRESULT in hexadecimal, stack, dimensions/crop, frame rate/bitrate, encoder attempt, and whether audio was included using development diagnostics (`Debug`/`Trace`); preserve both hardware and fallback failures. No new persistent telemetry, settings, or UI workflow is needed. If reproduction is unavailable, do not present an inferred root cause as established or close the spec as verified.
- **Platform fix:** correct the evidenced invalid argument, interop declaration, media-type negotiation, or initialization order in the files above. Inspect CsWin32-generated signatures and `NativeMethods.txt` if the failure crosses COM/WinRT. Keep the fix narrow and document the observed operation/HRESULT and before/after reproduction in the completion record. A successful build alone does not satisfy this item.
- **Pure logic:** put any changed configuration, packing, or retry decisions into `Snapper.Core/Recording/` (for example `RecordingStartupAttempt.cs` only if needed), accepting ordinary values and injected outcomes rather than native objects. Extract only decisions the repair actually needs. Keep existing geometry in `DisplayGeometry`; do not silently downscale, cap dimensions, change bitrate, or disable microphone to conceal a video startup defect.
- **Platform lifecycle:** preserve hardware-first operation and existing CPU fallback. Release the first writer/allocator/manager before replacement or deletion. Preserve the MTA writer thread, sink lock, nonblocking bounded queue, shared device lease and context lock, dispatcher UI updates, and deletion after writer release. Startup failure must end once, clear the timer/overlays/tray state, and permit another recording; cancellation and timeout must not strand a writer waiting for BeginWriting.
- No macOS source, dependency, project.yml, or Xcode project changes. PRD stays unchanged because this restores §7/§13 behavior.

## Acceptance criteria

- [ ] The reported Windows rectangle flow starts recording immediately after mouse release without the range-error alert; the failing operation and corrective change are supported by reproduction evidence.
- [ ] MP4 High yields playable H.264 video with native even dimensions, visible cursor, expected orientation/colors and capture timing; Stop saves and selects it in File Explorer.
- [ ] Supported hardware encoding works; an unavailable hardware path can use the existing software path without retaining the failed writer/file or losing the original diagnostic context.
- [ ] MP4 Medium/Low and GIF still start through the shared recorder and finish their existing conversion paths.
- [ ] Genuine failures retain the PRD's short error alert, clean up failed-start files after release, restore idle state, and allow a subsequent successful capture. Write failures still preserve non-empty partial recordings.
- [ ] No overlays, pill, or border appear in recorded output. No GUI verification is claimed unless actually run.

## Edge cases

- Odd and tiny rectangles (including existing minimum 2 × 2 output), full-monitor selection, monitor edges, and negative virtual-screen origins.
- 100%, 150%, and 200% DPI; adjacent monitors with different DPI. Rectangle crop remains monitor-local physical pixels and is never scaled twice.
- Hardware initialization failure versus BeginWriting failure; software initialization failure; failure before writer creation; first-frame failure; cancellation/Stop/quit during startup; no-frame timeout; immediate retry after an alert.
- MP4 microphone off/on, absent/denied microphone (video-only per PRD), and GIF (no microphone). Do not assume the original report had microphone enabled.

## Test plan

- **Pure unit regression:** extend `windows/tests/Snapper.Core.Tests/RecordingConfigurationBuilderTests.cs` with the actual reproducing rectangle/display once known and assert exact crop, even dimensions, 30 fps and bitrate. Retain explicit 96/144/192 DPI, negative/non-zero origins, odd dimensions, edge clamping and 2 × 2 cases. Do not add a test that merely reproduces the generic error text.
- **Lifecycle regression:** extend `windows/tests/Snapper.Core.Tests/RecordingLifecycleTests.cs` for startup failure followed by duplicate/late callbacks: only one terminal alert/end and no transition back to Recording. Assert existing Stop/Cancel-during-start semantics. If the repair changes startup retry policy, add `RecordingStartupAttemptTests.cs` with injected hardware-create failure, hardware-BeginWriting failure, software success/failure, and cancellation: bounded attempts, original failure retained, cleanup before replacement, no retry after cancellation. Do not introduce Core references to COM/WPF/WinRT.
- **Native validation:** exercise the corrected native operation with real WGC/Media Foundation on Windows. If only native argument/glue changes are required, explicitly state why a Core test cannot validate that API contract and provide before/after native results; do not substitute a mock assertion for codec validation.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` must pass. This spec-authoring change does not implement or run the repair.
- **Manual verification** (Windows 11 GUI required; PRD §7, §12, §13.11):
  1. Settings → Recording: MP4, High, microphone off. Press Ctrl+Shift+5; drag an approximately 800 × 600 rectangle and release. Expect the pill/timer, border and red tray icon without an alert. Move the cursor and some content, record five seconds, then press Ctrl+Shift+5. Expect Explorer selecting a playable MP4 with the expected area/cursor and no Snapper overlays.
  2. Repeat the originally failing rectangle and monitor configuration. Record the dimensions, DPI, format, quality, microphone state, OS and GPU/driver with the development failure trace. Confirm the corrected build succeeds on that configuration.
  3. Repeat with odd-size, small and full-monitor rectangles and on available 100%/150%/200% monitors, including a negative-origin monitor. Check output size, orientation and boundaries.
  4. Exercise both hardware and software attempts using a development-only injected failure if necessary; verify a failed hardware attempt releases resources before software succeeds. Force both to fail: expect one alert, no orphan output/overlays, idle tray, and successful recording after restoring normal operation.
  5. Test MP4 microphone on with an available input, then absent/denied input (video-only). Test Medium, Low and GIF. Stop and inspect each output; GIF must contain no audio.
  6. Cancel a running recording; request Stop/Cancel during delayed startup; exercise a no-frame timeout. Verify discarded files disappear after release, the UI recovers, and another capture works. Smoke-test window recording to protect the shared startup path.

## Out of scope

- Shared Direct3D device COM interface repair belongs to `0048-windows-direct3d-device-interface-failure_TODO.md`, which precedes this spec. Apply that repair first and re-test recording before changing the shared helper again; if it also resolves this report, document that evidence and retain only the recording regression coverage needed here. Spec 0049 addresses unrelated shortcut-tab behavior.
- New codecs, dependencies, encoder settings, automatic resolution reductions, persistent diagnostic collection, or changes to user-facing error design.
- macOS changes, annotation changes, and unrelated recording/conversion redesigns.

