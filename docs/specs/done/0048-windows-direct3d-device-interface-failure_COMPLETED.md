# 0048 — Fix the Windows capture device COM interface failure

> Source: user screenshot bug report (2026-11-04); docs/PRD.md §6, §8.1, §12, §13.1, §13.5, §13.6, §13.11
> Depends on: 0031-windows-screenshot-rectangle-capture, 0032-windows-window-selection-mode, 0034-windows-recording-core, 0040-windows-annotation-window-canvas, 0047-windows-packaging-release (all completed)

## Context

The user attempted a screenshot on Windows and received **Couldn't take screenshot** with:

```text
Unable to cast COM object of type 'System.__ComObject' to interface type
'Windows.Win32.Graphics.Direct3D11.ID3D11Device'.
This operation failed because the QueryInterface call on the COM component
for the interface with IID '{DB6F6DDB-AC77-4E88-8253-819DF9BBF140}' failed
due to the following error: No such interface supported
(0x80004002 (E_NOINTERFACE)).
```

This is a runtime failure at the native COM boundary, despite the existing completed specs'
successful build and Core test results. The report does not establish whether the user selected
a rectangle or window, nor whether they ran Debug or a published executable. Exercise both.

Relevant implementation:

- `windows/src/Snapper.Windows/Interop/Direct3D11Helper.cs:65`: `DeviceResources` creates the
  hardware device, falls back to WARP after a failed creation HRESULT, obtains the device/context
  managed interfaces, enables multithread protection, queries `IDXGIDevice`, and wraps it as
  `IDirect3DDevice`. Lines 68–76 contain both generated `D3D11CreateDevice` calls; lines 80–96
  contain the native/WinRT bridge and partial-initialization cleanup.
- `windows/src/Snapper.Windows/Interop/Direct3D11Helper.cs:21`: `Acquire`, `Reset`, and
  reference-counted leases retain old device generations until their last capture/writer ends.
- `windows/src/Snapper.Windows/Capture/ScreenshotCapturer.cs:23`: monitor and window capture
  retry only device-removed/reset errors; `CaptureItemAsync` acquires a device at line 78.
- `windows/src/Snapper.Windows/App/AppCoordinator.cs:261`: `TakeScreenshot` calls shared
  `CaptureAsync`; annotation uses the same capture path at line 296.
- `windows/src/Snapper.Windows/Recording/RecordingSession.cs:233`: recording acquires the same
  helper with `videoSupport: true` on a worker thread.
- `windows/src/Snapper.Windows/NativeMethods.txt:46`: CsWin32-generated device/COM declarations.
  `windows/Directory.Packages.props:4` pins CsWin32 to 0.3.367.

**Diagnosis boundary:** the exception identifies a failed device interface query, but there is
no stack trace or native reproduction yet proving which operation produced the wrong object.
Do not present a bad driver, a particular generated overload, an IID typo, or an apartment issue
as the established cause. Confirm the failing call and actual generated declaration first.

Microsoft's [D3D11CreateDevice contract](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-d3d11createdevice)
distinguishes the device output from the immediate-context output. Use that native contract when
checking CsWin32's generated signature, parameter ordering, marshaling, and returned interfaces.

PRD §6/§13.5 already requires working native-resolution screenshots and §8.1 reuses that capture
for annotation. §13.1 mandates Windows.Graphics.Capture; §13.5 requires the existing error alert
for real capture failures. This is a correction to those requirements; no new product behavior
or PRD change is needed. The macOS screenshot references in specs 0031/0032 describe output
behavior, but have no Direct3D equivalent to port.

## User story

As a Windows user, I can select an area or window and receive my screenshot, or open it for
annotation, without Snapper failing to initialize its Direct3D device.

## Scope & changes

- **Platform diagnosis:** reproduce on Windows 11 with first-chance exception/stack information
  from `DeviceResources`. Inspect the generated `D3D11CreateDevice` overload selected by each
  call, including COM GUIDs and ABI output types. Generated sources may be emitted into ignored
  build output for inspection; never edit generated declarations. Record the confirmed cause and
  failing stage in the completed spec's verification notes.
- **Platform fix:** correct device creation/interface acquisition in `Direct3D11Helper.cs`.
  Prefer explicit generated parameter names/types so device, feature-level, and context outputs
  cannot be confused. If the managed projection is the cause, use the appropriate CsWin32 native
  pointer entry point and explicit interface acquisition with balanced ownership. Confirm the
  actual generated signature before choosing the fix. Change `NativeMethods.txt` only if another
  generated entry point/interface is required. Keep the pinned dependencies.
- Check native HRESULTs before consuming outputs. Publish a device generation only after device,
  immediate context, required multithread interfaces, and WinRT device initialization succeed.
  Every intermediate native pointer/RCW must have an explicit ownership and release path,
  including hardware failure, WARP failure, and WinRT wrapping failure. Do not release an object
  still owned by an active lease or double-release aliases of the same COM object.
- Preserve current hardware-first/WARP-on-creation-failure behavior, BGRA support, recording's
  video-support flag, multithread protection, `ContextLock`, and device-generation lease lifetime.
  A managed cast failure must be fixed at its source; suppressing it, repeatedly retrying it, or
  replacing capture APIs does not satisfy this spec.
- **Pure/platform boundary:** this slice corrects native interface acquisition and ownership.
  COM objects, driver initialization, and ABI checks stay in `Snapper.Windows`; none belongs in
  `Snapper.Core`. No new pure decision logic is required. Preserve existing crop, retry,
  screenshot-output, and app-mode behavior and their Core tests.
- Keep dispatcher ownership and callback marshaling unchanged. Preserve capture exclusion,
  cursor policy, native physical-pixel dimensions, clipboard PNG/DIB output, and alert behavior.
  Audit screenshot and recording callers for compatibility with the corrected helper; modify
  callers only where the corrected interface ownership requires it.
- No macOS, `project.yml`, settings, shortcut, or publish-profile changes are expected.

## Acceptance criteria

- [ ] A fresh Windows 11 process can acquire a screenshot device, bridge it to WinRT, capture a
  frame, and copy its pixels without the reported `ID3D11Device`/`E_NOINTERFACE` exception.
- [ ] Rectangle and window screenshots save/copy according to the selected destination; PNG
  size and content match PRD §6/§13.5, with no cursor or selection overlay.
- [ ] Ctrl+Shift+6 opens a focused annotation editor containing the selected image.
- [ ] Recording still starts, stops, and produces a playable MP4 after screenshot capture, and
  screenshots still work after recording has upgraded the shared device to video support.
- [ ] Reset/dispose does not invalidate active leases; failed initialization releases native
  resources and leaves the helper able to attempt initialization on the next operation.
- [ ] Real capture failures still display the existing error alert and return the app to idle;
  they do not cause a crash, hang, or unbounded retry.
- [ ] Debug and published Release executables pass the applicable Windows runtime checks.

## Edge cases

- First capture after launch versus subsequent captures; screenshot → recording → screenshot.
- Hardware creation failure followed by WARP success; both creation attempts failing. Keep
  outputs from a failed attempt separate from valid objects and clean up partial results.
- Bridge initialization failure after native creation; helper disposal with live leases;
  reset while an older generation remains leased. Preserve existing synchronization rules.
- Window closing during capture, denied/unavailable capture, and display removal retain their
  current behavior; do not reinterpret all COM failures as device removal.
- 100%/150%/200% monitors and a negative-origin secondary monitor: interface changes must not
  change physical-pixel crop coordinates or introduce a DPI conversion.
- x64 and Arm64 use the same correct ABI; execute on each architecture when hardware is
  available and explicitly record any architecture not exercised.

## Test plan

- **Unit tests:** no new pure logic is specified, so no new Core unit tests are required for
  this native-only correction. Mocking COM success cannot prove the actual interface/ABI fix.
  Run existing `windows/tests/Snapper.Core.Tests/CaptureCropTests.cs`,
  `WindowCaptureCropTests.cs`, `ScreenshotCaptureExceptionTests.cs`, `AppModeMachineTests.cs`,
  `RecordingLifecycleTests.cs`, and the full suite. If implementation introduces a pure decision,
  add focused Core tests for each new branch rather than moving interop into Core.
- **Build checks:** `dotnet build windows/Snapper.slnx -c Debug` and
  `dotnet test windows/Snapper.slnx` must pass. Publish both existing profiles using
  `dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64` and the corresponding
  `win-arm64` command. A build or Core test pass alone does not verify this runtime fix.
- **Native diagnostic verification (Windows 11):** inspect device/context creation and native
  interface acquisition under a debugger or a temporary diagnostic harness calling the actual
  helper. Verify hardware and WARP creation separately, the native device → DXGI → WinRT bridge,
  and lease retention across reset. Exercise initialization failure cleanup where feasible;
  record precisely which fault paths were inspected versus executed. Remove temporary forced
  failures/diagnostic switches from shipped code. Record the failure before and success after
  the fix if the original failure is reproducible.
- **Manual verification (Windows 11 GUI; PRD §12/§13.11; not yet run):**
  1. Exit all Snapper instances, launch Debug, press Ctrl+Shift+4, and drag a rectangle. With
     destination Both, confirm a native-resolution PNG is saved and pastes into an image editor;
     no error, cursor, or overlay appears. Repeat capture without restarting.
  2. Press Ctrl+Shift+4, Space, and select a partly covered window. Confirm the entire window
     without its shadow is saved/copied. Repeat once with Save to file and once with Copy to
     clipboard; only the configured outputs occur.
  3. Repeat rectangle capture on available 100%/150%/200% monitors, including a secondary monitor
     positioned left of the primary. Confirm content and physical-pixel dimensions.
  4. Press Ctrl+Shift+6 and select an area, then repeat in window mode. Confirm each editor opens
     focused with the correct image; close unmodified editors.
  5. After a screenshot, press Ctrl+Shift+5, select an area, record five seconds, and stop with
     Ctrl+Shift+5. Confirm Explorer reveals a playable MP4 with cursor and no pill/border.
     Immediately take another screenshot; it succeeds. Repeat recording in window mode.
  6. Exit Debug, launch the published x64 executable, and repeat steps 1, 2, 4, and 5. Repeat on
     native Arm64 when available; mark unavailable coverage explicitly.
  7. If a genuine capture failure can be reproduced, dismiss its alert and take another capture;
     Snapper remains responsive and usable. Do not claim this path verified without running it.

## Out of scope

- New capture backends, GPU-selection UI, dependency upgrades, or generalized COM refactoring.
- Changes to image output policy, error wording, DPI geometry, permissions, and recording format.
- macOS changes or marking earlier GUI acceptance checks complete without executing them.

## Implementation notes

The native `D3D11CreateDevice` declaration and its generated eight-argument span wrapper were inspected with CsWin32 0.3.367. The wrapper supplies the feature-level pointer/count in the documented order; the generated device and context GUIDs are correct. Device creation was not the failing operation.

On Windows 11 x64, a temporary diagnostic harness invoked the actual `ScreenshotCapturer` on a WPF dispatcher. The original helper acquired its device on the STA successfully, then the first free-threaded WGC frame callback failed in `DeviceResources.CreateStagingTexture` when calling `ID3D11Device.CreateTexture2D`. The stack entered `StubHelpers.GetCOMIPFromRCWSlow` and reproduced the reported `InvalidCastException`, device IID and `0x80004002` exactly. Acquiring the device/bridge alone on STA or MTA did not reproduce it: crossing from the STA-created RCW to the MTA callback was required.

The helper now retains independently owned native device/context interfaces obtained with `QueryInterface` and uses CsWin32's generated unmanaged methods for staging creation and screenshot copy/map/unmap. Those generated creation/map methods check the native HRESULT with `ThrowOnFailure` before returning. The native references belong to the device generation and survive reset/disposal until its last lease ends; texture resource interfaces are released after each copy. Recording retains its existing MTA device/context use, shared context lock and video-support upgrade. Named creation parameters clarify the ABI, and initialization cleanup now also covers failed WARP creation and bridge projection. No dependency, capture backend, coordinate or output-policy changes were made.

The two existing Windows suite failures from the earlier verification report were repaired while bringing the required full suite green: settings JSON explicitly selects LF as required by completed spec 0027, and the atomic-write failure test accepts Windows' `UnauthorizedAccessException` as well as Unix's `IOException` before verifying preservation and temporary-file cleanup. No pure capture decision changed, so a Core mock test would not validate this COM apartment correction and no new Core test was added.

AGENTS.md now records the native-interface rule for free-threaded capture callbacks. The PRD requires no change because this restores its existing capture behavior.

## Verification status

Automated build/tests and native diagnostic verification completed **2026-11-04**. GUI acceptance remains **not yet run by user**.

- **Debug solution build: PASS**, zero warnings and errors. Command: `dotnet build windows/Snapper.slnx -c Debug -p:OutputPath=C:/Projects/CSharp/ScreenshotApp/windows/artifacts/validation-bin/`. An existing Snapper process locked the usual Debug output; it was preserved, and validation used an isolated output directory instead.
- **Full Core suite: PASS**, `dotnet test windows/Snapper.slnx`: 1,379 passed, 0 failed, 4 skipped, 1,383 total. The four existing physical-filesystem skips are not passes.
- **Publish: PASS** for both existing `win-x64` and `win-arm64` profiles. Artifact creation does not prove clean-machine or native Arm64 execution.
- **Before/after native reproduction: PASS** on x64. The original actual screenshot path reproduced the reported cast failure on its first callback. With the correction, three 128 × 128 monitor captures returned 65,536-byte buffers across repeated capture, video-support upgrade and reset. A controlled 320 × 240 red diagnostic window captured with center BGRA exactly `0,0,255,255`.
- **Native lifetime checks: PASS** on STA and MTA: screenshot and video-support generations could create staging textures after reset, and a live lease remained usable after helper disposal.
- **Hardware/WARP creation and bridge: PASS** on x64. Direct native hardware and WARP calls without video support returned S_OK and supported the device/context/DXGI interface queries and WinRT bridge. A documented invalid hardware attempt (`D3D_DRIVER_TYPE_UNKNOWN` with no adapter) returned `0x80070057`, followed by WARP S_OK; actual monitor captures across reset and the controlled red-window pixel check then passed. WARP with video support returned `0x887A0004` on this host; existing video-support requirements and fallback policy were preserved.
- **Injected initialization failure/recovery: exercised**. Making both creation attempts invalid produced `ArgumentException`, `0x80070057`; injecting a projection failure after successful native WinRT bridge creation produced `COMException`, `0x80004005`. The same helper acquired a valid generation after each failure, followed by successful monitor/window captures. Cleanup paths were inspected for balanced native references and RCWs; no native leak counter or driver fault injection was used.
- Diagnostic harnesses and forced-failure changes were temporary and are not shipped. Native diagnostics do not establish tray/hotkey, clipboard, annotation focus, playable recording, DPI, capture exclusion, or published executable GUI acceptance. No native Arm64 hardware was available.

Manual verification checklist (the detailed procedures in Test plan above still apply):

- [ ] **Not yet run by user:** fresh Debug process, rectangle capture with Both destination, repeat capture, verify saved PNG and clipboard pixels, no cursor/overlay/error.
- [ ] **Not yet run by user:** window capture of a partly covered window, then Save to file and Copy to clipboard separately; verify full window without shadow and only the selected outputs.
- [ ] **Not yet run by user:** capture on available 100%/150%/200% and negative-origin monitors; verify physical-pixel content and dimensions.
- [ ] **Not yet run by user:** rectangle/window annotation capture opens a focused editor with the correct image.
- [ ] **Not yet run by user:** screenshot → rectangle/window recording → screenshot; save/reveal playable MP4 with cursor and no pill/border.
- [ ] **Not yet run by user:** repeat the specified capture/annotation/recording checks using published x64, and native Arm64 when available; record unavailable architecture coverage.
- [ ] **Not yet run by user:** genuine capture failure shows the existing alert, returns to idle and permits another successful capture.
