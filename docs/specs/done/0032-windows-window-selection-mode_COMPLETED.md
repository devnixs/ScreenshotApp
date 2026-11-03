# 0032 — Windows: add window selection mode (Space toggle) and shadowless window screenshots

> Source: docs/PRD.md §5 (Window mode, lines 73–76; Esc and focus, lines 77–79), §6 item 2 (line 88), §12 (line 216), §13.1 (capture border, line 242), §13.4 (window-mode candidates and window capture, lines 289–290), §13.5 (errors, line 297)
> Depends on: 0028-windows-display-geometry, 0030-windows-selection-overlay-rectangle, 0031-windows-screenshot-rectangle-capture

## Context

Not yet implemented on Windows. After 0030 and 0031, the Windows overlay (`windows/src/Snapper.Windows/Capture/SelectionOverlayController.cs` with one `SelectionOverlayWindow` per monitor, plus Core `Capture/SelectionSession`, `Capture/RectSelectionTracker` and `Capture/SelectionResult`) supports rectangle mode only. `Capture/ScreenshotCapturer` (0031) captures a monitor through `Windows.Graphics.Capture` (WGC) and crops it, and the result flows through Core `ScreenshotOutputPlan`, `FileNamer`/`SaveFolderPolicy` (0026), `PngEncoder`, `Clipboard`, `ShutterSound` and `ErrorAlerts` (0031).

The PRD requires the second selection mode on Windows too:

- §5: "**Space** toggles between rectangle and window mode, either before or during a drag. Toggling during a drag discards that drag; switching back to rectangle mode starts a fresh drag." "The window under the cursor is highlighted, and clicking selects it." "Our own windows and system UI that can't be captured are excluded from window hit-testing." "**Esc** cancels at any time."
- §6 item 2: "Window captures contain only that window, with no shadow, even if other windows cover it." Format stays PNG at native pixel resolution (§13.5: the monitor's physical pixels).
- §13.4: "**Window-mode candidates** are visible, top-level, non-minimized windows with a non-empty frame, in z-order from the top. These are excluded: cloaked windows (other virtual desktops, suspended apps; `DWMWA_CLOAKED`), Snapper's own windows, tool windows, the desktop (`Progman`/`WorkerW`), and the taskbars (`Shell_TrayWnd`/`Shell_SecondaryTrayWnd`). A window's frame is its `DWMWA_EXTENDED_FRAME_BOUNDS`, which excludes the shadow and the invisible resize borders." "A window capture uses `Windows.Graphics.Capture` on that window, so a covered window is still captured whole. The capture is cropped to the extended frame bounds, so it has no shadow."
- §12 (line 216), with the §13.2 mapping: "Pressing Space switches to window mode, and the resulting capture contains only that window with no shadow, even when it is covered."

This spec adds window mode to the shared overlay (so Ctrl+Shift+4, Ctrl+Shift+5 and Ctrl+Shift+6 all get it) and implements the window **screenshot** path end to end. What Ctrl+Shift+5 and Ctrl+Shift+6 do with a window selection belongs to 0034/0036 and 0040.

### macOS reference (port behavior and tests; don't redesign)

- `Snapper/Capture/WindowHitTester.swift:5-14` (`WindowCandidate`), `:28-41` (`WindowHitTester`: `isSelectable` + "first selectable candidate containing the point, front to back"; non-selectable windows are **skipped, not blocking**), `:43-58` (`WindowDisplayRule`; on Windows this type is owned by **0028**, reuse it).
- `Snapper/Capture/SelectionSession.swift:3-51`: mode, toggle, stale press, window-click arming. Port it 1:1.
- `Snapper/Capture/WindowCandidateProvider.swift:4-31`: snapshot taken once per overlay session.
- `Snapper/Capture/SelectionOverlayController.swift:53-64` (load candidates once, session token guards late results), `:79-90` (window click → hit-test → `WindowDisplayRule` → finish), `:92-101` (Space toggles, ignores auto-repeat, re-hit-tests at the current cursor), `:103-114` (cursor + hover, redraw only when the hovered window changes), `:128-148` (highlight per panel).
- `Snapper/Capture/SelectionOverlayView.swift:26-43`: window mode draws no dimming and no readout, only the accent fill (25 % alpha) plus a 2 pt accent stroke, clipped to the panel.
- `Snapper/Capture/ScreenshotCapturer.swift:20-41`: re-resolve the window at capture time, throw `windowUnavailable` ("The window is no longer available.") if it's gone.
- `Snapper/App/AppCoordinator.swift:196-215`: window and rect selections share one image → output path.
- Tests to port: `SnapperTests/WindowHitTesterTests.swift:12-66`, `SnapperTests/SelectionSessionTests.swift:10-93`. `SnapperTests/WindowCandidateBuilderTests.swift` does **not** port: Windows has a single z-ordered source (`EnumWindows`), so there's no SCK/CG join. `SnapperTests/WindowDisplayRuleTests.swift` ports in 0028.
- The macOS spec is `docs/specs/done/0009-window-selection-mode_COMPLETED.md`. Its decisions carry over: stale-press handling, arrow cursor in window mode, no refresh of the snapshot while the overlay is open, a click on empty desktop does nothing, a failed or pending candidate load leaves window mode inert.

### macOS facts that do not apply on Windows

- No Y flip and no primary-display height. Window frames, the cursor and monitor bounds are all **physical pixels** in virtual-screen coordinates (top-left, y down). The hard part is DIP↔pixel conversion per monitor DPI, and that always goes through `DisplayGeometry` (0028).
- No window layer, alpha join, or SCK/CG ordering join. Z-order comes from `EnumWindows`.
- No Screen Recording permission or onboarding. Every capture failure shows "Couldn't take screenshot" with the error (§13.5).
- No `ignoreShadowsSingleWindow`. The shadow is removed by **cropping** the WGC frame to `DWMWA_EXTENDED_FRAME_BOUNDS` (see "Crop" below).
- `WindowFromPoint` / `ChildWindowFromPoint` can't be used for hover: our topmost overlays cover every monitor, so they would always return the overlay. Hit-testing runs against the snapshot.

### Research notes (binding for the implementer)

- **Z-order:** `EnumWindows` enumerates top-level windows in z-order from the top (topmost windows first). Keep the callback's order unchanged. Owned top-level windows such as dialogs are included, which is correct: a dialog in front of its owner is selectable on its own.
- **Frame:** `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT)`. Snapper is Per-Monitor V2 aware (0025 manifest), so this and `GetWindowRect` return **physical** pixels for every window, including DPI-unaware ones. If the DWM call fails (non-`S_OK`), fall back to `GetWindowRect`.
- **Cloaked:** `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out uint)`. A non-zero value means cloaked (`DWM_CLOAKED_APP`, `_SHELL` or `_INHERITED`). If the call fails, treat the window as not cloaked.
- **Tool windows:** `GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW`.
- **Class name:** `GetClassName` (buffer of 256). Win32 class names are case-insensitive, so compare with `StringComparison.OrdinalIgnoreCase`.
- **Process:** `GetWindowThreadProcessId(hwnd, out pid)`. Snapper's own windows are excluded by **process ID** (it covers the overlays, Settings, annotation windows, pill and border, including future ones) **and** by the 0030 HWND registry in `Support/CaptureExclusion` (defense in depth).
- **WGC window item:** `IGraphicsCaptureItemInterop.CreateForWindow(hwnd, typeof(GraphicsCaptureItem).GUID)`, using the same interop path 0031 uses for `CreateForMonitor`. The frames of a window item cover the window's **`GetWindowRect`** area: the invisible resize borders (about 7 px each on the left, right and bottom at 100 %) are included as transparent or black pixels, and the DWM shadow is not. On some builds or window types the frame already equals the extended frame bounds. The crop rule below handles both cases.
- **Crop:** offset = `extendedFrameBounds.TopLeft − windowRect.TopLeft`, size = `extendedFrameBounds.Size`, intersected with `(0, 0, frame.ContentSize)`. If `ContentSize` already equals the extended size, the crop is the whole content. The rects are read **when the frame arrives**, so a window that moved or resized between hover and click still crops correctly. Maximized windows have `GetWindowRect` overhanging the monitor by the border width while the extended bounds don't, so the same offset rule applies.

## User story

As a Snapper user on Windows, after pressing Ctrl+Shift+4 I can press Space, see the window under my cursor highlighted, and click it to get a PNG that contains only that window, with no shadow, no invisible-border margins and nothing that was covering it, saved and/or copied according to my destination setting.

## Scope & changes

### Pure logic (`Snapper.Core`, xUnit-tested, no WPF/WinForms/WinRT/interop types)

- **`windows/src/Snapper.Core/Capture/WindowCandidate.cs`** (new): `public sealed record WindowCandidate(nint Handle, PixelRect Frame, uint ProcessId, string ClassName, bool IsVisible, bool IsMinimized, bool IsCloaked, bool IsToolWindow, string? Title)`. `Frame` is the extended frame bounds in **global physical pixels**. `Handle` is the HWND as `nint`, which is a plain C# primitive, so Core stays interop-free. `Title` is diagnostics only.
- **`windows/src/Snapper.Core/Capture/WindowCandidateFilter.cs`** (new): `public static class WindowCandidateFilter`
  - `public static readonly IReadOnlySet<string> ExcludedClassNames` = `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, using a case-insensitive comparer.
  - `public const int MinimumSize = 4;` (physical pixels). This ports the macOS ≥ 4 × 4 rule (`WindowHitTester.swift:35`) and satisfies §13.4's "non-empty frame", since it excludes 0-size and 1–3 px helper windows.
  - `public static bool IsSelectable(WindowCandidate c, uint ownProcessId, IReadOnlySet<nint> excludedHandles)` returns true only when **all** of these hold: `c.IsVisible`, `!c.IsMinimized`, `!c.IsCloaked`, `!c.IsToolWindow`, `c.ProcessId != ownProcessId`, `!excludedHandles.Contains(c.Handle)`, the class name is not in `ExcludedClassNames`, and `c.Frame.Width >= MinimumSize && c.Frame.Height >= MinimumSize`.
- **`windows/src/Snapper.Core/Capture/WindowHitTester.cs`** (new): `public sealed class WindowHitTester` (immutable), constructed with `IReadOnlyList<WindowCandidate> candidates` (z-order, topmost first), `uint ownProcessId` and `IReadOnlySet<nint> excludedHandles`.
  - `public bool IsSelectable(WindowCandidate c)` delegates to `WindowCandidateFilter`.
  - `public WindowCandidate? WindowAt(PixelPoint p)` returns the **first** candidate in order that is selectable and whose frame contains `p`, **half-open**: `Left <= x < Right && Top <= y < Bottom`. Implement this check here explicitly rather than relying on `PixelRect`'s own `Contains` semantics. Non-selectable candidates (an overlay, a cloaked window, a tool window) are skipped, not blocking. It returns `null` over empty desktop.
- **`windows/src/Snapper.Core/Capture/WindowSelection.cs`** (new): `public static class WindowSelection` with `public static SelectionResult? Resolve(WindowHitTester? tester, PixelPoint releasePoint, IReadOnlyList<DisplayInfo> displays)`. It returns `null` when `tester` is null (candidates not loaded), when there's no window at the point, or when `WindowDisplayRule` (0028) returns null (no displays). Otherwise it returns the new window result (below), with the display chosen by `WindowDisplayRule` from the candidate's frame: largest overlap, ties to the first display, nearest display as fallback. This is the pure equivalent of `SelectionOverlayController.swift:82-87`.
- **`windows/src/Snapper.Core/Capture/WindowHighlightGeometry.cs`** (new): `public static RectD? LocalRect(PixelRect windowFrame, DisplayInfo display)`. It intersects the window frame with the display's **full bounds** (the overlay covers the full monitor, taskbar included, §13.4) and returns `null` if the intersection is empty. Otherwise it converts the result to that overlay's window-local DIPs with `DisplayGeometry` (0028), using the display's own scale. Every overlay draws its own part, so a window spanning two monitors with different DPIs is highlighted correctly on both.
- **`windows/src/Snapper.Core/Capture/WindowCaptureCrop.cs`** (new): `public static PixelRect? Compute(PixelRect windowRect, PixelRect extendedFrameBounds, PixelSize contentSize)`. All inputs are physical pixels. It returns a rect in frame-local pixels:
  1. If `contentSize == extendedFrameBounds.Size`, return `(0, 0, contentSize)` (WGC already delivered the visible frame).
  2. Otherwise compute `(ext.X − win.X, ext.Y − win.Y, ext.Width, ext.Height)` and intersect it with `(0, 0, contentSize.Width, contentSize.Height)`.
  3. Return `null` if the result is empty.
  This is separate from 0031's `CaptureCrop` (monitor-rectangle math), which isn't modified.
- **`SelectionSession`** (0030, `windows/src/Snapper.Core/Capture/SelectionSession.cs`, extended): port `SelectionSession.swift:3-51` exactly.
  - Add `public enum SelectionMode { Rectangle, Window }`. A new session starts in `Rectangle`.
  - Add the read-only state `Mode`, `IsMouseDown`, `IsPressStale` and `IsWindowClickArmed`, keeping 0030's tracker property.
  - `ToggleMode()` flips the mode, **replaces the tracker with a fresh `RectSelectionTracker`** (this discards any in-progress drag), sets `IsWindowClickArmed = false`, and sets `IsPressStale = IsMouseDown`.
  - `MouseDown(p, displays)` sets `IsMouseDown = true` and `IsPressStale = false`. In `Rectangle` it forwards to the tracker. In `Window` it sets `IsWindowClickArmed = true`.
  - `MouseDragged(p)` forwards to the tracker only in `Rectangle` and only when `!IsPressStale`.
  - `MouseUp(p)` sets `IsMouseDown = false` and always disarms on return. If the press is stale, it clears the flag and returns `None`. In `Rectangle` it maps the tracker's selected result to `Rectangle(result)` and anything else to `None`. In `Window` it returns `WindowClick(p)` if armed, otherwise `None`.
  - The outcome type becomes (or is extended to) `SelectionSessionOutcome` with `None`, `Rectangle(SelectionResult Result)` and `WindowClick(PixelPoint At)`. Use whatever closed-hierarchy shape 0030 chose (an abstract record with sealed derived records, for example), and keep value equality so tests can compare outcomes.
  - Coordinates are the global physical pixels that 0030 already uses for the tracker.
  - If 0030's `SelectionSession` was a thin wrapper, this spec turns it into the full port. If 0030 had no session type and the controller fed the tracker directly, create `SelectionSession` here and route the controller through it. The behavior above is binding either way.
  - §5's "switching back to rectangle mode starts a fresh drag" means the discarded rectangle is never restored, and a new rectangle starts only with a **new** mouse press. A button held across a toggle is ignored until it's released (as on macOS).
- **`SelectionResult`** (0030, extended): add a window variant `Window(nint Handle, PixelRect Frame, DisplayInfo Display)` next to the rectangle variant. `Frame` is the extended frame bounds in global physical pixels, and `Display` comes from `WindowDisplayRule`. Document both on the type. Update every exhaustive `switch` in 0030/0031 code (C# switch expressions over the closed hierarchy must handle the new case. Add a default arm only where the compiler requires one, and make it throw `UnreachableException`).

### Platform glue (`Snapper.Windows`, verified manually)

- **`windows/src/Snapper.Windows/Capture/WindowCandidateProvider.cs`** (new): `internal static class WindowCandidateProvider` with `public static IReadOnlyList<WindowCandidate> Snapshot()`, which runs on the dispatcher thread.
  - `EnumWindows` collects HWNDs in callback order (topmost first). For each HWND it reads `IsWindowVisible`, `IsIconic`, `DWMWA_CLOAKED`, the `GWL_EXSTYLE` tool-window bit, `GetClassName`, `GetWindowThreadProcessId`, `DWMWA_EXTENDED_FRAME_BOUNDS` (with `GetWindowRect` as the fallback) and, optionally, `GetWindowText` for the title.
  - It skips the expensive calls (DWM, class name, title) for windows that aren't visible, to keep it fast.
  - It returns **all** enumerated visible candidates unfiltered. Filtering happens in Core, so the rules stay testable.
  - A failure for one HWND (the window was destroyed mid-enumeration) skips that HWND and never throws.
- **`windows/src/Snapper.Windows/Support/CaptureExclusion.cs`** (0030): if it doesn't already expose one, add a read-only snapshot accessor of the registered Snapper HWNDs (for example `IReadOnlySet<nint> RegisteredHandles`) for the hit tester's `excludedHandles`.
- **`SelectionOverlayController`** (0030, modified):
  - **Snapshot:** after the overlay windows are shown, take **one** snapshot with `Dispatcher.BeginInvoke(DispatcherPriority.Background, …)`, so the overlay appears first. Store `new WindowHitTester(candidates, (uint)Environment.ProcessId, CaptureExclusion.RegisteredHandles)`. Guard the callback with a session token: if the overlay closed or a new session started, drop the result. Until the snapshot is stored, window mode has no hit tester, so nothing is highlighted and clicks do nothing. If `Snapshot()` throws, log it (Debug output) and stay inert, without an alert. The snapshot isn't refreshed while the overlay is open. When it arrives, immediately hit-test at the current cursor if the session is in window mode.
  - **Input routing:** route all mouse input through `SelectionSession` (not the tracker directly). For window mode, the global physical point comes from the same conversion 0030 uses for rectangle drags. If that conversion relies on the pressed window's DPI while the cursor is over another monitor, use `GetCursorPos` instead (it returns physical pixels in a PMv2 process). Never convert DIPs from one monitor's window with another monitor's scale.
  - **Space:** in the overlay windows' `PreviewKeyDown`, `Key.Space` with `!e.IsRepeat` calls `session.ToggleMode()` and marks the event handled. A repeat is marked handled and ignored, so holding Space toggles once. After toggling, clear the hovered window, clear the rectangle drawing (dimming and readout) on every overlay, update the cursor, and hit-test at `GetCursorPos` right away so the highlight appears without moving the mouse. Esc (0030) is unchanged and works in both modes.
  - **Hover:** on `MouseMove` in window mode, hit-test at the global physical cursor point. Store the hovered candidate and re-render the overlays **only** when the hovered `Handle` changes or the mode changes. While a window-mode press is held and dragged, hovering continues (as on macOS `SelectionOverlayController.swift:73-77`).
  - **Click:** on `WindowClick(p)`, call `WindowSelection.Resolve(hitTester, p, displays)`. If it returns null (empty desktop, or the snapshot isn't loaded), do nothing and keep the overlay open, exactly like a click in rectangle mode. Otherwise finish through the same path as a rectangle result: the overlays are hidden before capture (§5), focus is restored (0030 `ForegroundRestorer`) unless the purpose is Annotate, and the capture is excluded from the overlays anyway (WDA_EXCLUDEFROMCAPTURE).
  - **Cursor:** rectangle mode keeps 0030's crosshair, and window mode uses `Cursors.Arrow` (same decision as macOS 0009).
  - **Display change** while in window mode cancels exactly as in 0030.
- **`SelectionOverlayWindow`** (0030, modified rendering):
  - In window mode there's no dimming and no W × H readout. If a window is hovered, draw `WindowHighlightGeometry.LocalRect(frame, thisDisplay)` (skip it when null): a fill in the Windows accent color at 25 % alpha plus a 2 DIP accent stroke inset by 1 DIP.
  - Get the accent color from `new Windows.UI.ViewManagement.UISettings().GetColorValue(UIColorType.Accent)` (WinRT, available through the TFM), read once per overlay session, with `#0078D4` as the fallback if the call throws.
  - The highlight covers the window's **full** frame, including the parts other windows cover, because that's what will be captured.
  - **Hit-testability:** WPF windows don't receive mouse input over fully transparent (alpha 0 or null) background pixels. In window mode the overlay background must stay hit-testable, using a near-transparent brush such as `#01000000`, so hover and clicks reach the overlay instead of the app underneath. Check that the rectangle-mode dimming change doesn't break this.
- **`ScreenshotCapturer.CaptureWindowAsync(nint hwnd, CancellationToken)`** (0031's class, new method). It returns the same image type that 0031's monitor capture returns (BGRA pixels plus pixel size), so the output path is shared.
  1. If `!IsWindow(hwnd)` or `IsIconic(hwnd)`, throw `WindowUnavailableException` (new, in `Snapper.Windows/Capture/`) with the message **"The window is no longer available."**
  2. Create the item through `IGraphicsCaptureItemInterop.CreateForWindow`. If that fails (for example `E_INVALIDARG` for a window that just closed), map the error to `WindowUnavailableException`. Any other exception propagates.
  3. Use 0031's D3D device helper (`Interop/Direct3D11Helper`). Create a free-threaded frame pool of `B8G8R8A8UIntNormalized`, size `item.Size`, 1 buffer, then create a session with `IsCursorCaptureEnabled = false`. Turn the border off with `IsBorderRequired = false` using exactly 0031's approach: if Windows refuses, capture proceeds with the border (§13.1).
  4. On the first `FrameArrived` (a background thread):
     - Read `frame.ContentSize`, and call `GetWindowRect` and `DWMWA_EXTENDED_FRAME_BOUNDS` (with the same fallback) **now**. These Win32 calls are thread-safe.
     - Compute `WindowCaptureCrop.Compute(...)`. A `null` crop throws `WindowUnavailableException`.
     - If the `ContentSize` is larger than the pool's texture size (the window grew since the item was created), call `framePool.Recreate(...)` with the new size and use the next frame. Allow one retry only, then use the content clipped to the texture.
     - Copy the crop region to a CPU-readable staging texture (`CopySubresourceRegion`), map it, and read rows honoring `RowPitch`.
  5. Use 0031's frame timeout and disposal pattern (session, pool and item are disposed on every path). A timeout throws the same error 0031 uses for monitor captures.
  6. **Alpha:** pass the pixels through unchanged as BGRA. If 0031's `PngEncoder` writes opaque `Bgr32`/`Bgra32` with alpha forced to 255, leave it alone. Don't add alpha processing in this spec. Windows 11 rounded corners appear however WGC delivers them, and the manual check records it.
- **`AppCoordinator`** (0025/0031, modified):
  - For the Screenshot purpose, a window result calls `CaptureWindowAsync(handle)` and then runs the **same** output path as a rectangle: `ScreenshotOutputPlan`, PNG via `FileNamer`/`SaveFolder`, `Clipboard`, `ShutterSound`, and the error mapping, where "Couldn't save to <folder>" with **Choose Folder…** applies as for rectangles and every capture error (including `WindowUnavailableException`) shows **"Couldn't take screenshot"** with the error message.
  - The PNG's DPI metadata, if 0031 writes any, uses the result's `Display` scale.
  - If 0031 inlined "image → output" inside the rectangle branch, factor it out so the rectangle and window paths don't duplicate it.
  - **Interim behavior for other purposes:** Record and Annotate send a window result to whatever handles a rectangle result for that purpose at this point (the 0025 stubs). They must not throw on the new variant.
- **`NativeMethods.txt`** (0025, append whatever isn't already there): `EnumWindows`, `IsWindowVisible`, `IsIconic`, `IsWindow`, `GetWindowRect`, `GetClassName`, `GetWindowText`, `GetWindowTextLength`, `GetWindowLongPtr`, `GetWindowLong`, `GetWindowThreadProcessId`, `GetCursorPos`, `DwmGetWindowAttribute`, `DWMWINDOWATTRIBUTE`, `WINDOW_EX_STYLE`, `IGraphicsCaptureItemInterop` (probably already added by 0031). CsWin32 generates `GetWindowLongPtr` correctly for x64/Arm64; keep the call in one helper.
- **PRD:** no change. The arrow cursor, highlight style, stale-press handling and ≥ 4 px minimum are ports of decisions recorded in macOS 0009 that are consistent with §5 and §13.4.

## Acceptance criteria

- [ ] With the overlay open (from any of the three shortcuts), Space switches to window mode, and Space again switches back. This works before a drag and during a drag. Holding Space toggles once.
- [ ] Pressing Space during a rectangle drag removes the rectangle (dimming and readout) at once. Releasing the mouse afterwards captures nothing. Back in rectangle mode, a new press starts a fresh drag.
- [ ] In window mode the cursor is an arrow, and the topmost selectable window under the cursor is highlighted with an accent fill and border that follows the cursor. A window spanning two monitors, including ones with different DPIs, is highlighted exactly over its frame on both.
- [ ] Clicking a highlighted window with Ctrl+Shift+4 produces a PNG whose pixel size equals that window's `DWMWA_EXTENDED_FRAME_BOUNDS` size. It contains only that window, with no shadow, no invisible-border margin, no covering windows, no cursor and no overlay. The file name, destination, clipboard and shutter sound behave as for rectangle captures.
- [ ] Minimized, cloaked (other virtual desktop), tool, desktop (`Progman`/`WorkerW`) and taskbar windows, plus all Snapper windows, are never highlighted or selectable, and they don't block the windows behind them.
- [ ] Clicking empty desktop in window mode does nothing, and the overlay stays open.
- [ ] Esc closes every overlay in both modes, and focus returns to the previous foreground window.
- [ ] If the selected window is gone at capture time, the alert "Couldn't take screenshot" shows "The window is no longer available." Nothing crashes.
- [ ] Ctrl+Shift+5 and Ctrl+Shift+6 with a window selection don't crash (interim stubs).
- [ ] UI work runs on the dispatcher thread. `FrameArrived` work never touches WPF objects.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed.

## Edge cases

- **Overlapping windows:** the topmost selectable window wins. Topmost (always-on-top) windows come first in `EnumWindows` order and win over normal windows.
- **Non-selectable windows in front** (our overlays, which cover every monitor and are first in z-order; tool windows; cloaked UWP frames) are skipped, not treated as blocking. If the overlays weren't excluded, every hover would highlight the whole monitor. This is the main regression check.
- **Covered window:** hovering over a visible part of it selects it, and the capture shows the whole window. A fully covered window can't be hovered (accepted, same as macOS).
- **Maximized window:** `GetWindowRect` overhangs the monitor by the resize border width. The crop offset removes it, so the PNG equals the monitor's work-area part that the window covers, with no black strip.
- **Borderless or full-screen windows** (games, video players, kiosk apps): extended frame bounds == window rect, so the offset is 0 and the whole frame is kept.
- **WGC already delivers the visible frame** (`ContentSize == extended size`): the whole content is used, with no double crop.
- **Window spanning monitors with different DPIs:** the capture is the whole window (WGC window item). `Display` (largest overlap) is used only for PNG metadata and later pill placement. §5's single-display confinement applies only to rectangles.
- **Window partly off-screen:** the WGC window item still renders the whole window. The crop uses window-relative offsets, so it's unaffected. The highlight is clipped to the monitors.
- **Negative-origin monitors** (left of or above the primary): all frames and cursor points are signed physical pixels, and the tests cover them.
- **Mixed DPI:** the output pixel size is the window's physical extended-bounds size. Nothing is rescaled.
- **DPI-unaware or system-aware target apps:** a PMv2 caller gets physical rects, and WGC renders their (bitmap-stretched) content at physical size. Accepted.
- **Window closes, minimizes or moves between hover and click:** the snapshot is stale by design. Closed or minimized → `WindowUnavailableException` → alert. Moved or resized → the capture is still correct, because the item and crop are resolved at capture time.
- **Elevated windows** (for example an administrator Command Prompt): if WGC refuses, the capture error shows in the "Couldn't take screenshot" alert. No crash, no special handling.
- **Snapshot not loaded yet, or it failed:** window mode is inert (no highlight, clicks ignored). Space and Esc still work.
- **Start menu, Search and notification flyouts:** they close or cloak when the overlay takes the foreground. If one is still uncloaked in the snapshot, it's a normal candidate (accepted).
- **Display change while in window mode:** cancel, as in 0030.
- **Shortcuts while the overlay is open** stay ignored (0029).
- **Yellow capture border:** turned off if Windows allows it. Otherwise it may flash around the target window during the one-frame capture (accepted per §13.1). It is never part of the cropped pixels except where it overlaps the window edge. The manual check records which happens.

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/WindowCandidateFilterTests.cs`):
  - `VisibleNormalWindowIsSelectable`: visible, not minimized, not cloaked, not a tool window, foreign PID, class `Notepad`, 800 × 600 → true.
  - `EachExclusionRuleRejects`, a theory with one row per rule: not visible, minimized, cloaked, tool window, `ProcessId == ownProcessId`, handle in `excludedHandles`, class `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd` → false.
  - `ClassNamesCompareCaseInsensitively`: `progman` and `SHELL_TRAYWND` → false.
  - `MinimumSizeBoundary`: 0 × 0, 3 × 100 and 100 × 3 → false. 4 × 4 → true.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/WindowHitTesterTests.cs`, porting `SnapperTests/WindowHitTesterTests.swift:12-66`):
  - `TopmostWindowWins`: front A (10, 10, 100, 100) and back B (50, 50, 200, 200). (60, 60) → A, (200, 200) → B, (0, 0) → null.
  - `NonSelectableWindowsAreSkippedNotBlocking`, a theory: an own-PID full-monitor overlay, a registered HWND, a cloaked window, a tool window, a minimized window and a `Shell_TrayWnd`, each in front of B. (50, 50) → B, and (500, 500) → null.
  - `ContainsUsesHalfOpenEdges`: (10, 10) hits. (110, 50) and (50, 110) miss.
  - `NegativeCoordinates`: a window at (−1920, −300, 800, 600) is hit at (−1500, 0) and missed at (−1000, 0).
  - `EmptyCandidatesReturnNull`.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/WindowSelectionTests.cs`). Displays: A primary 1920 × 1080 at (0, 0), 100 %; B 3840 × 2160 at (1920, −540), 200 %; C 2560 × 1440 at (−2560, 0), 150 %.
  - `NullTesterReturnsNull` (snapshot not loaded).
  - `EmptyDesktopReturnsNull`.
  - `WindowOnNegativeOriginMonitorPicksIt`: the window (−2000, 300, 900, 600) clicked at (−1500, 500) → `Window` with display C and the frame unchanged.
  - `SpanningWindowPicksLargestOverlap`: (1500, 100, 1000, 400) → B (580 px wide on B vs 420 px on A). (1700, 100, 400, 400) → A (220 vs 180).
  - `ReleaseOnOtherMonitorHitTestsReleasePoint`, a press on A and a release on B (the cross-monitor click): a session `MouseDown` at (100, 100) and `MouseUp` at (2500, 200) in window mode yields `WindowClick((2500, 200))`, which `Resolve` maps to the window under (2500, 200).
  - `NoDisplaysReturnsNull`.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/WindowHighlightGeometryTests.cs`, same displays):
  - `WindowOn150PercentNegativeOriginMonitor`: (−2000, 300, 900, 600) on C → local DIPs (373.33, 200, 600, 400), within 0.01.
  - `SpanningWindowSplitsAcrossMixedDpi`: (1500, 100, 1000, 400) → on A, (1500, 100, 420, 400) at 100 %. On B, local px (0, 640, 580, 400) → DIPs (0, 320, 290, 200) at 200 %.
  - `WindowNotOnMonitorReturnsNull`: the same window on C → null.
  - `IncludesTaskbarArea`: a window overlapping the bottom 48 px of A (where the taskbar sits) is highlighted there too (full bounds, not the work area).
- **Unit tests** (`windows/tests/Snapper.Core.Tests/WindowCaptureCropTests.cs`):
  - `InvisibleBordersRemoved`: windowRect (100, 100, 1014, 607), ext (107, 100, 1000, 600), content 1014 × 607 → (7, 0, 1000, 600).
  - `MaximizedWindowOverhang`: windowRect (−8, −8, 1936, 1056), ext (0, 0, 1920, 1040), content 1936 × 1056 → (8, 8, 1920, 1040).
  - `ContentAlreadyMatchesExtendedBounds`: content 1000 × 600, ext 1000 × 600, windowRect bigger → (0, 0, 1000, 600).
  - `BorderlessWindowNoOffset`: windowRect == ext → (0, 0, w, h).
  - `NegativeOriginMonitor`: windowRect (−2567, 300, 1214, 907), ext (−2560, 300, 1200, 900) → (7, 0, 1200, 900).
  - `ScaledBorders200Percent`: windowRect (1906, −540, 2028, 1214), ext (1920, −540, 2000, 1200) → (14, 0, 2000, 1200).
  - `ClampedToContent`: content smaller than offset + size (the window shrank) → intersected rect.
  - `DisjointReturnsNull`: the offset lies outside the content → null.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/SelectionSessionTests.cs`, extend 0030's file; port `SnapperTests/SelectionSessionTests.swift:10-93` using display A at 100 % and C at 150 % as the negative-origin display):
  - `StartsInRectangleModeAndTogglesBothWays`: mode Rectangle, all flags false. Toggle → Window, toggle → Rectangle.
  - `ToggleDuringDragDiscardsDragAndStaleMotion`: down → drag → toggle leaves no current rect and `IsPressStale`. Toggle → drag leaves no rect. Up → `None`, flags cleared. A new down → drag → up yields `Rectangle` with the expected rect.
  - `StalePressDragIsIgnoredAfterToggleBack`: down → toggle → toggle → drag → up → `None`.
  - `WindowClickReturnsReleasePointAndDisarms`: in window mode, up without down → `None`. Down → drag (the tracker stays empty) → up(p) → `WindowClick(p)`, then disarmed. A second up → `None`.
  - `RectanglePressReleasedInWindowModeDoesNotClick`: down → toggle → up → `None`. Then down → up → `WindowClick`.
  - `WindowPressReleasedInRectangleModeDoesNothing`: toggle → down → toggle → up → `None`.
  - `ClicksAndBelowThresholdDragsStayOpen`: stays `None` (the threshold in DIPs per 0030).
  - `RectangleStillClampsToStartingNegativeOriginDisplayAfterToggles`: toggle twice, then drag from monitor C into A → the rectangle is clamped to C (0030's rule) at 150 %.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` both pass. There's no XcodeGen step.
- **Manual verification** (Windows 11 machine. Can't be automated):
  1. Open Notepad and File Explorer so they overlap. Press Ctrl+Shift+4, then Space **before** dragging. Expected: an arrow cursor, and the window under the cursor gets an accent-colored tint and border that follows the cursor from window to window. The overlay itself never highlights a whole monitor.
  2. Press Space again. Expected: the crosshair returns, the highlight disappears, and dragging draws a rectangle as before.
  3. Press Ctrl+Shift+4, start a rectangle drag, and press Space **mid-drag**. Expected: the rectangle and readout disappear at once, and releasing captures nothing. Press Space again and drag anew. Expected: a fresh rectangle that captures normally.
  4. With Notepad partly covered by Explorer, press Ctrl+Shift+4, then Space, and click the visible part of Notepad. Expected: the overlay disappears and the shutter sound plays. The PNG in `%USERPROFILE%\Pictures\Screenshots` (and on the clipboard: paste into Paint) shows **all** of Notepad with no Explorer content, **no shadow**, no transparent or black margin on the left, right or bottom edges, and no yellow border (record whether a border flashed on screen). Check that the PNG's dimensions (File Properties → Details) equal the window's visible size in physical pixels (for example, about 1.5× its DIP size on a 150 % monitor). Note how the rounded corners look.
  5. Maximize a window and capture it in window mode. Expected: no black or transparent strip around the edges, and the PNG width equals the monitor width.
  6. With two monitors at different scales (for example 100 % and 150 %, one placed left of or above the primary), capture a window on each. Expected: the correct window at that monitor's native pixel size. Drag a window so it straddles both monitors and hover it. Expected: the highlight matches the window's outline exactly on both monitors. Capture it. Expected: the whole window.
  7. Press the mouse button on one monitor in window mode and release it over a window on the other monitor. Expected: the window under the release point is captured.
  8. Hover over the taskbar and the desktop wallpaper. Expected: no highlight, and clicking does nothing (the overlay stays open).
  9. Minimize a window, then hover where it was. Expected: it isn't highlighted (the window behind it, or nothing, is).
  10. Open a window on a second virtual desktop (Win+Ctrl+D, open Notepad, switch back with Win+Ctrl+Left), then use window mode where that window would be. Expected: it isn't highlighted.
  11. In window mode, press Esc. Expected: every overlay closes, nothing is captured, and the previously active window has focus again. After a successful window capture with Ctrl+Shift+4, focus also returns to the previous window.
  12. Hold Space for about a second. Expected: one toggle, no flicker.
  13. Run `$p = Start-Process notepad -PassThru; Start-Sleep 8; Stop-Process $p` in PowerShell. Within 8 s press Ctrl+Shift+4 and Space and hover Notepad. Wait for it to close, then click where it was. Expected: the alert "Couldn't take screenshot" with "The window is no longer available.", and no crash.
  14. Set the destination to *Copy to clipboard*, then *Save to file*, and repeat step 4. Expected: the same behavior as rectangle captures for each destination.
  15. Run an elevated Command Prompt and try to capture it in window mode. Expected: either a correct capture or a "Couldn't take screenshot" alert. No crash, and record which one happens.
  16. Press Ctrl+Shift+5 and Ctrl+Shift+6, press Space, and click a window. Expected: no crash (the interim stub behavior until 0034/0036/0040).

## Out of scope

- The overlay lifecycle, rectangle selection, the readout, Esc, display-change cancellation, focus restore and `CaptureExclusion`: **0030** (only extended here).
- The monitor capture path, `CaptureCrop`, `ScreenshotOutputPlan`, `PngEncoder`, the clipboard, the shutter sound and error alerts: **0031** (reused here).
- The geometry primitives, `DisplayGeometry` and `WindowDisplayRule`, with their tests: **0028**.
- Recording a selected window: **0034** must accept a `Window` result (until 0036 lands, record the window's current frame clipped to its `Display` as a rectangle). Following the window, letterboxing, and stop-on-close are **0036**. Pill and border placement: **0035**.
- Opening a window capture in the annotation editor: **0040** (it calls `CaptureWindowAsync` and uses the same image).
- Refreshing the window snapshot while the overlay is open, keyboard selection of windows, child windows, and capturing minimized windows: not in the PRD.
- Window shadows: a PRD non-goal (§2).

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**. `dotnet test windows/Snapper.slnx` passed **583 tests**, including **61 new cases**, with **0 failures and 0 skips**. Logs: `/tmp/snapper-0032-build.log` and `/tmp/snapper-0032-test.log`.

The implementation extends the existing flat result hierarchy with `WindowSelectionResult(nint Handle, PixelRect Frame, DisplayInfo Display)`, alongside `RectSelection`; `WindowSelection` remains the pure resolver. The hit tester copies its inputs to keep the snapshot immutable. The shared capture routine inspects actual texture dimensions, recreates a growing window's frame pool at most once, and clips a second growth to its texture. `GraphicsCaptureItem` is a non-closable WinRT object; the session, pool, frames, native factory references, texture references, and device lease are explicitly released. PRD and AGENTS.md already cover the behavior and architecture, so neither required changes.

GUI behavior has **not** been verified. Run these checks in a Windows 11 GUI session:

- [ ] not yet run by user: With overlapping Notepad and Explorer windows, Ctrl+Shift+4 then Space switches to an arrow cursor and accent highlight that follows selectable windows; Snapper's overlay never highlights itself.
- [ ] not yet run by user: Space again restores the crosshair and rectangle drag rendering.
- [ ] not yet run by user: Space during a rectangle drag removes its rectangle/readout; mouse release captures nothing. Toggle back and use a new press to capture normally.
- [ ] not yet run by user: Capture the visible part of a covered Notepad window. Confirm the complete Notepad image, clipboard/file/sound output, native physical dimensions, no covering app, shadow, margin, cursor, or overlay. Record rounded-corner appearance and whether a system capture border flashes.
- [ ] not yet run by user: Capture a maximized window and confirm the PNG has no edge strips and its width equals the monitor width.
- [ ] not yet run by user: On mixed-DPI monitors with a negative-origin monitor, capture a window on each; then span a window across them and confirm its highlight aligns on both and capture contains the whole window.
- [ ] not yet run by user: Press in window mode on one monitor and release over a window on another; confirm selection follows the release point.
- [ ] not yet run by user: Hover/click taskbar and empty wallpaper; confirm neither is selected and the overlay remains open.
- [ ] not yet run by user: Minimized windows are skipped in favor of the selectable window behind them or no selection.
- [ ] not yet run by user: Windows on another virtual desktop are never highlighted or selected.
- [ ] not yet run by user: Esc in window mode closes all overlays and restores prior focus; a successful screenshot also restores prior focus.
- [ ] not yet run by user: Hold Space for one second; confirm exactly one mode toggle without flicker.
- [ ] not yet run by user: Close a window after the snapshot/hover but before click; confirm "Couldn't take screenshot" / "The window is no longer available." with no crash.
- [ ] not yet run by user: Repeat a covered-window capture with Clipboard-only and File-only destinations; confirm the same output rules as rectangle captures.
- [ ] not yet run by user: Capture an elevated Command Prompt; record whether capture succeeds or a short capture-error alert appears, with no crash.
- [ ] not yet run by user: Ctrl+Shift+5 and Ctrl+Shift+6 window selections retain their interim stub behavior without crashing; annotation returns prior focus.
