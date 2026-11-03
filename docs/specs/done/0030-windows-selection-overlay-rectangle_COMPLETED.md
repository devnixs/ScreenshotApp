# 0030 — Windows: show the rectangle selection overlay on every monitor

> Source: docs/PRD.md §5 (rectangle mode, Esc, focus return incl. the Ctrl+Shift+6 exception, shortcuts ignored, display-change cancel, overlay removed before capture), §13.4 (overlay per monitor, physical pixels, DIP readout and threshold, DPI-change cancel, focus rule), §13.1 (excluded windows, Per-Monitor V2), §13.2 (points → DIPs, ⌘⇧N → Ctrl+Shift+N), §12 / §13.11 (Esc and multi-display criteria, "the overlay matches the dragged area" with mixed scales)
> Depends on: 0028-windows-display-geometry, 0029-windows-global-hotkeys-app-mode (and transitively 0025, 0027)

## Context

The `windows/` app does not exist yet; 0025–0029 create it. After 0029:

- `AppCoordinator` (`windows/src/Snapper.Windows/App/AppCoordinator.cs`, 0025) routes the three hotkeys (0029 `App/HotkeyManager`) and the tray menu items through the pure `App/AppModeMachine` (0029, a port of `Snapper/App/AppModeMachine.swift:34-60`). Its modes are idle / selecting(purpose) / recording. It already ignores every shortcut and menu item while selecting (`AppModeMachine.swift:59`). On `selectionFinished` it moves to `recording` for `CapturePurpose.Record` and to idle otherwise (`:54`). `StartScreenshot` / `StartRecording` / `StartAnnotate` are still stubs that end up doing nothing after the machine says "show overlay".
- 0028 (`docs/specs/todo/0028-windows-display-geometry_TODO.md`) provides, and this spec **uses without duplicating**:
  - Core primitives `PointD`, `SizeD`, `RectD`, `PixelRect`. Global coordinates are physical virtual-screen pixels, carried as `PointD`/`RectD` (mouse positions are whole pixels, so the doubles are exact).
  - `Geometry/DisplayInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)` with `Scale` and `DipSize`, and record value equality.
  - `Geometry/DisplayGeometry`: `DisplayContaining`, `NearestDisplay` (containment, then the closest monitor), `Clamp`, `NormalizedRect`, **`DragRect(start, current, display)`** (both points clamped to the start monitor's closed bounds), **`DipSize(RectD, display)`**, **`ToLocalDips(PointD/RectD, display)`**, **`CaptureRect(RectD, display)`** (→ `PixelRect`), and **`ToMonitorLocal(PixelRect, display)`**.
  - `Geometry/DisplayConfigurationChange.HasChanged(old, current)` (order-insensitive; ignores work-area-only changes).
  - Windows: `Capture/DisplayInfoProvider.GetCurrent()` and `Capture/DisplayChangeWatcher` (`Current`, `event Changed` with old/new lists, raised only for real changes, and `Recheck()`). The watcher's hidden window only gets `WM_DPICHANGED` for its own monitor, so **every overlay window must call `Recheck()` from its own `WM_DPICHANGED` handler** (0028 §"DisplayChangeWatcher").
- What is missing is the overlay between "shortcut accepted" and "capture".

**macOS reference implementation (port its behavior and tests; don't redesign):**

- `Snapper/Capture/RectSelectionTracker.swift:1-64` (drag state, start-display confinement, nearest-display fallback, 4×4 threshold on the clamped rect), tests `SnapperTests/RectSelectionTrackerTests.swift:10-120`.
- `Snapper/Capture/SelectionSession.swift:1-51` (press tracking around the tracker), tests `SnapperTests/SelectionSessionTests.swift:76-93`. The mode toggle parts (`:3`, `:19-24`, `:31`, `:47-48`, and tests `:10-74`) belong to 0032.
- `Snapper/Capture/SelectionReadout.swift:1-20` (text and placement), tests `SnapperTests/SelectionReadoutTests.swift:6-45`.
- `Snapper/Capture/SelectionResult.swift:1-8` (only the `rect` case; `window` is 0032).
- `Snapper/Capture/DisplayConfigurationChange.swift:1-5` is already ported by 0028 (`HasChanged`). 0030 only calls it.
- `Snapper/Capture/SelectionOverlayController.swift:20-178`, `SelectionOverlayPanel.swift`, `SelectionOverlayView.swift` (platform glue: present, update views, finish with dismissal delay, cancel, close, focus restore).
- `docs/specs/done/0007-selection-overlay-rectangle_COMPLETED.md` (full original spec) and `docs/specs/todo/0022-annotation-window-focus_TODO.md` (`SelectionFocusPolicy`: don't restore focus after an annotate selection).

**macOS facts that do not apply on Windows:**

- **No Y flip.** macOS works in AppKit bottom-left points. Here every global coordinate is a physical pixel in virtual-screen space (top-left origin, y down). The readout placement rules are mirrored accordingly ("below" means larger y).
- **No global point space.** Monitors with different DPIs have no shared DIP space, so the tracker works in **physical pixels**. DIPs are used only for the 4×4 threshold and the readout, both measured with the **start monitor's** scale (PRD §13.4).
- **No Screen Recording permission / onboarding gate** (PRD §13.10). Every accepted shortcut shows the overlay.
- No `NSWindow.Level`, Spaces, or `collectionBehavior`. "Above everything" means `HWND_TOPMOST`, and "not in ⌘\`" means `WS_EX_TOOLWINDOW`.
- No `SCContentFilter`. Our overlays are excluded from capture with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` (PRD §13.1).
- Focus is per window, not per app: Snapper remembers the foreground **window** (`HWND`), not an application (PRD §13.4).

PRD behavior delivered (docs/PRD.md §5, §13.4):

- A borderless, topmost overlay on **every** monitor, covering its full bounds including the taskbar, excluded from capture, not in the taskbar or Alt+Tab.
- Rectangle mode: crosshair cursor; dragging draws a rectangle; outside is dimmed; a live **W × H** readout in DIPs of the start monitor appears near the cursor; the rectangle is confined to the start monitor; a click, or a drag smaller than 4 × 4 DIPs, does nothing and the overlay stays open.
- **Esc** cancels at any time and closes every overlay.
- After a selection, the overlay is removed before capture.
- When the overlay closes, focus returns to the window that was in the foreground when the shortcut fired, **except after a Ctrl+Shift+6 selection** (Snapper keeps the foreground so the annotation window can open in front, §8.1 / §13.7). Esc and display-change cancellations always return focus.
- While the overlay is open, all Snapper shortcuts are ignored.
- Connecting or disconnecting a monitor, or changing a monitor's arrangement, resolution, or DPI, cancels the selection.

Window mode and Space (PRD §5 "Window mode", §13.4 candidates) are **0032**. The types below are shaped so 0032 can add them without a redesign.

## User story

As a Snapper user on Windows, when I press Ctrl+Shift+4, Ctrl+Shift+5, or Ctrl+Shift+6 (or pick Screenshot / Record / Annotate in the tray menu), every monitor dims and the cursor becomes a crosshair. I drag a rectangle on one monitor and see its size in DIPs next to the cursor, even on mixed-DPI setups. Releasing the mouse finishes the selection. Esc backs out cleanly on every monitor, and my previous window gets the keyboard back.

## Scope & changes

### Pure logic (`windows/src/Snapper.Core/Capture/`, xUnit-tested, no WPF/WinForms/interop types)

All coordinates below are `Snapper.Core.Geometry` types from 0028. "Global" means physical virtual-screen pixels, top-left origin, y down.

- **`SelectionResult.cs`**: the value the overlay returns.
  ```csharp
  public abstract record SelectionResult;
  /// Rect is in global physical pixels, fully inside Display.Bounds, and at least 4×4 DIPs at Display.Dpi.
  public sealed record RectSelection(DisplayInfo Display, PixelRect Rect) : SelectionResult;
  // 0032 adds: public sealed record WindowSelection(...) : SelectionResult;
  ```
  Consumers (0031 capture, 0034 recording) convert `Rect` to a monitor-local crop with 0028's `DisplayGeometry.ToMonitorLocal`. This spec does no capture math. C# can't make switches over a record hierarchy exhaustive, so every consumer's `switch` ends with `_ => throw new UnreachableException()`. 0032 then has to search for those arms (note this in a code comment on `SelectionResult`).

- **`RectSelectionTracker.cs`**: `public struct RectSelectionTracker` (mutable value type, port of `RectSelectionTracker.swift`). It works in global physical pixels (`PointD` for points). All geometry is delegated to 0028's `DisplayGeometry`. The tracker adds only the drag state and the 4-DIP rule.
  - `public const double MinimumSizeDips = 4;`
  - Private state: idle, or dragging(`DisplayInfo startDisplay`, `PointD start`, `PointD current`), with both points stored already clamped.
  - `void MouseDown(PointD point, IReadOnlyList<DisplayInfo> displays)`: the start monitor is `DisplayGeometry.NearestDisplay(point, displays)` (containment with 0028's shared-edge rule, then the closest monitor; this replaces `RectSelectionTracker.swift:19-24`). If it returns `null` (empty list), the tracker stays idle. Otherwise `start = DisplayGeometry.Clamp(point, startDisplay.Bounds)` and `current = start`.
  - `RectD? Drag(PointD point)`: when idle it does nothing and returns `null`. Otherwise `current = DisplayGeometry.Clamp(point, startDisplay.Bounds)` (the **start monitor**, never another one), and it returns `CurrentRect`.
  - `RectD? CurrentRect`: `null` when idle, otherwise `DisplayGeometry.DragRect(start, current, startDisplay)`.
  - `PointD? CurrentPoint`, `DisplayInfo? StartDisplay`.
  - `RectSelectionOutcome MouseUp(PointD point)`: applies `Drag(point)` first. Then:
    - idle → `Ignored`;
    - `DisplayGeometry.DipSize(rect, startDisplay)` with `Width < MinimumSizeDips - 1e-9` or `Height < MinimumSizeDips - 1e-9` → `TooSmall` (a 0×0 click lands here). At 96 DPI the minimum is 4 px, at 120 DPI 5 px, at 144 DPI 6 px, at 192 DPI 8 px. The epsilon only absorbs floating-point error.
    - otherwise → `Selected(new RectSelection(startDisplay, DisplayGeometry.CaptureRect(rect, startDisplay)))`. `rect` already has integer edges, so `CaptureRect` returns the same rect as a `PixelRect`.
    - In every case the tracker returns to idle.
  - `void Reset()`: discards any drag (used on cancel, and by 0032 when Space toggles modes mid-drag).
  - `RectSelectionOutcome`: a small closed hierarchy or a struct with `Kind { Selected, TooSmall, Ignored }` plus `SelectionResult? Result`. It needs value equality for the tests.
  - The threshold applies to the **clamped** rect. A drag that starts 2 px from a monitor edge and continues onto the next monitor is too small.

- **`SelectionSession.cs`**: `public struct SelectionSession`, the rectangle-only part of `SelectionSession.swift`.
  - `RectSelectionTracker Tracker { get; }`, `bool IsMouseDown { get; }`.
  - `void MouseDown(PointD, IReadOnlyList<DisplayInfo>)`, `void MouseDragged(PointD)`, `SelectionSessionOutcome MouseUp(PointD)`, `void Reset()`.
  - `SelectionSessionOutcome`: `None` or `Rectangle(SelectionResult)`. `TooSmall` and `Ignored` both map to `None` (port of `SelectionSession.swift:39-46` without the window branch).
  - 0032 adds `Mode`, `ToggleMode()`, `IsPressStale`, `IsWindowClickArmed`, and `WindowClick(PointD)`. Keep the type a thin wrapper around `RectSelectionTracker`: it holds no geometry of its own, only the tracker and the press flag, and delegates every rectangle decision to the tracker. Make `SelectionSessionOutcome` an extensible shape (for example an abstract record with `None`/`Rectangle` subtypes, or a struct with a `Kind` enum) so 0032 can add `WindowClick` and the mode branch without changing the existing members or tests.

- **`SelectionReadout.cs`**: `public static class SelectionReadout`, a port of `SelectionReadout.swift`, mirrored for y-down. It works in **monitor-local DIPs** (origin = the monitor's top-left, size = the monitor's DIP size).
  - `const double Offset = 12, Margin = 4;`
  - `static string Text(SizeD dips)` → `$"{w} × {h}"`, where `w`/`h` are `Math.Round(v, MidpointRounding.AwayFromZero)` printed as integers with `CultureInfo.InvariantCulture`, no grouping, separator `" × "`. Example: `(1440, 900)` → `"1440 × 900"`.
  - The DIP size comes from 0028's `DisplayGeometry.DipSize(rect, startDisplay)`. Don't add a second conversion here.
  - `static PointD Origin(SizeD labelSize, PointD cursor, RectD display)` returns the label's **top-left** in the same coordinate space as `display`:
    1. Default (below-right): `x = cursor.X + Offset`, `y = cursor.Y + Offset`.
    2. If `x + labelSize.Width > inset.Right`, flip left: `x = cursor.X - Offset - labelSize.Width`.
    3. If `y + labelSize.Height > inset.Bottom`, flip above: `y = cursor.Y - Offset - labelSize.Height`.
    4. Clamp: `x = max(inset.Left, min(x, inset.Right - w))`, and the same for y, so an oversized label is pinned to the inset's left/top edge.
    `inset` is `display` deflated by `Margin` on every side.

- **`SelectionOverlayRenderState.cs`** (new, not in the macOS code: it extracts the logic of `SelectionOverlayController.swift:128-148` so it can be tested):
  - `public sealed record OverlayFrame(RectD? SelectionDips, string? ReadoutText, PointD? ReadoutOriginDips)`, with `static OverlayFrame Empty`.
  - `static OverlayFrame For(DisplayInfo overlayDisplay, in RectSelectionTracker tracker, Func<string, SizeD> measureLabel)`:
    - If the tracker is idle, or `overlayDisplay` is not the tracker's `StartDisplay` (compare by `DeviceName`), → `Empty`. So other monitors stay fully dimmed.
    - Otherwise: `SelectionDips = DisplayGeometry.ToLocalDips(CurrentRect, overlayDisplay)`. `ReadoutText = SelectionReadout.Text(DisplayGeometry.DipSize(CurrentRect, overlayDisplay))`. `ReadoutOriginDips = SelectionReadout.Origin(measureLabel(text), DisplayGeometry.ToLocalDips(CurrentPoint, overlayDisplay), new RectD(0, 0, overlayDisplay.DipSize.Width, overlayDisplay.DipSize.Height))`.
  - `measureLabel` returns the full label size including padding (the WPF side injects a `FormattedText` measurement; tests inject a constant).

- **No `DisplayConfigurationChange` here:** 0028 owns `Geometry/DisplayConfigurationChange.HasChanged`, which already ignores order and work-area-only changes. 0030 uses it.

- **`SelectionFocusPolicy.cs`**: a port of 0022's `SelectionFocusPolicy`, plus the Windows "which window to remember" rule.
  - `enum SelectionOutcome { Selected, Cancelled }`.
  - `static bool RestoresPreviousWindow(CapturePurpose purpose, SelectionOutcome outcome)`: `Cancelled` → `true` for every purpose. `Selected` → `true` for `Screenshot` and `Record`, `false` for `Annotate`. Use a `switch` over `CapturePurpose` (0029) with no discard arm except `_ => throw new UnreachableException()`.
  - `static bool ShouldRemember(bool isWindow, bool isVisible, string className)`: `true` only for an existing, visible window whose class is not `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `Progman`, or `WorkerW` (ordinal comparison). Restoring the taskbar or the desktop would just park the keyboard focus there. Snapper's own visible windows (Settings, annotation windows) **are** remembered and restored like any other window. The tray menu's hidden window is excluded because it isn't visible.

### Platform glue (`windows/src/Snapper.Windows/`, manually verified)

- **`Support/CaptureExclusion.cs`**: `internal static class CaptureExclusion`, dispatcher thread only.
  - `static bool Exclude(HWND hwnd)` calls `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`. On failure it logs `Marshal.GetLastWin32Error()` with `Debug.WriteLine` and returns `false`. The window still works, and the screenshot path in 0031 doesn't depend on it, because the overlay is gone before capture.
  - A registry of Snapper-owned HWNDs: `Register(HWND)`, `Unregister(HWND)`, `IReadOnlySet<HWND> RegisteredWindows { get; }` (a read-only view of the registered handles, for 0032's candidate filter), and `bool IsSnapperWindow(HWND)` (registered, **or** `GetWindowThreadProcessId` returns our process id). Overlays register in `SourceInitialized` and unregister on close. 0032 (window hit-testing) and 0035 (pill and border) use the same class.

- **`Support/ForegroundRestorer.cs`**: `internal sealed class ForegroundRestorer`.
  - `void Remember()`: `GetForegroundWindow()`. It keeps the handle only if `SelectionFocusPolicy.ShouldRemember(IsWindow, IsWindowVisible, GetClassName)` returns `true`, and otherwise stores none.
  - `void Restore()`: if a handle is stored and `IsWindow(hwnd)` is still true, it calls `SetForegroundWindow(hwnd)`. This is allowed because our overlay is the foreground window at that moment. It then clears the handle.
  - `void Forget()`: clears the handle without restoring.

- **`Capture/SelectionOverlayWindow.cs`** (+ code-only, no XAML needed): `internal sealed class SelectionOverlayWindow : Window`, one per monitor.
  - Window setup: `WindowStyle = None`, `ResizeMode = NoResize`, `AllowsTransparency = true` (per-pixel alpha), `Background = Brushes.Transparent`, `ShowInTaskbar = false`, `Topmost = true`, `WindowStartupLocation = Manual`, `ShowActivated = false` (the controller activates one window explicitly), `Cursor = Cursors.Cross`, `Focusable = true`. Don't set WPF `Left`/`Top`/`Width`/`Height` for positioning.
  - In `SourceInitialized` (via `WindowInteropHelper.EnsureHandle()` before `Show()`):
    - Add `WS_EX_TOOLWINDOW` to the extended style (`GetWindowLongPtr`/`SetWindowLongPtr`, `GWL_EXSTYLE`) so it never appears in Alt+Tab or the taskbar. Don't add `WS_EX_NOACTIVATE`, because the overlay must receive Esc.
    - Call `CaptureExclusion.Exclude(hwnd)` and `CaptureExclusion.Register(hwnd)`.
    - Call `SetWindowPos(hwnd, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE)` with `b = display.Bounds` in **physical pixels**. Moving a Per-Monitor-V2 window to another monitor can trigger `WM_DPICHANGED`, and WPF then resizes the window to the suggested rect. To end exactly on the monitor bounds, hook `WM_DPICHANGED` (`HwndSource.AddHook`, not handled) and, after WPF has processed it, post (`Dispatcher.BeginInvoke`) the same `SetWindowPos` again. **Every** `WM_DPICHANGED` the overlay receives (during setup or while open) also calls the shared `DisplayChangeWatcher.Recheck()`, because 0028's hidden watcher window only sees DPI changes on its own monitor. A setup-time DPI message is harmless: the topology hasn't changed, so the check raises nothing. In Debug builds, log a warning if `GetWindowRect` ≠ `Bounds` or if `VisualTreeHelper.GetDpi(this).DpiScaleX` ≠ `display.Scale` after showing.
  - Rendering: a child `FrameworkElement` (`OverlaySurface`) that overrides `OnRender` and draws from the current `OverlayFrame` (DIPs, local to this window). Setting a new frame calls `InvalidateVisual()`.
    - Dim: fill the whole surface with black at 35% opacity (`#59000000`), using a `CombinedGeometry(Exclude, bounds, selection)` so the selection is a hole.
    - Fill the hole with `#01000000` (alpha 1/255), not fully transparent. Layered windows are click-through where alpha is 0, and the selection area must keep receiving mouse input. 0032's window mode relies on the same rule.
    - Draw the border as a 1-DIP white pen on the selection rect, inset by 0.5 DIP, with `GuidelineSet` for crisp lines.
    - Readout: a rounded rect (radius 4 DIP, black at 75% `#BF000000`) at `ReadoutOriginDips`, with the text in Segoe UI 12 DIP white, 6 DIP horizontal / 3 DIP vertical padding. Segoe UI's digits are tabular by default, so the width doesn't jitter. `MeasureLabel(string)` uses `FormattedText` with the same font and `VisualTreeHelper.GetDpi(this).PixelsPerDip` and adds the padding. The controller passes it to `SelectionOverlayRenderState.For`.
  - Input, through a `HwndSource` hook (raw messages, so coordinates stay physical):
    - `WM_LBUTTONDOWN` / `WM_MOUSEMOVE` / `WM_LBUTTONUP`: the client coordinates in `lParam` are **signed** 16-bit values (`(short)(lParam & 0xFFFF)`, `(short)((lParam >> 16) & 0xFFFF)`) in physical pixels, because the window is PMv2 and borderless (client origin = window origin). Global = `new PointD(display.Bounds.X + cx, display.Bounds.Y + cy)`. This is a plain pixel offset, not a DIP conversion. Forward to the controller. On `WM_LBUTTONDOWN` call `SetCapture(hwnd)`, so moves and the release keep coming to the window that received the press even after the cursor crosses onto another monitor (the macOS equivalent is `SelectionOverlayView.swift:80-91`). On `WM_LBUTTONUP` call `ReleaseCapture()`. Forward `WM_MOUSEMOVE` only while the left button is down (`MK_LBUTTON`).
    - `WM_CAPTURECHANGED` while a drag is in progress (capture taken away by the system, for example Ctrl+Alt+Del or a UAC prompt): forward to the controller, which resets the drag (`session.Reset()`, all frames `Empty`). The overlay stays open.
    - Right, middle, and X buttons are ignored.
  - Keys: `PreviewKeyDown` with `Key.Escape` → controller `Cancel()`. Every other key is ignored for now (0032 adds Space). Mark Esc as handled.
  - `Closing` raised when the controller didn't start the close (for example Alt+F4 on the focused overlay) → set `e.Cancel = true` and call the controller's `Cancel()`. Alt+F4 then behaves like Esc and closes every overlay.

- **`Capture/SelectionOverlayController.cs`**: `internal sealed class SelectionOverlayController`, owned by `AppCoordinator`, dispatcher thread only. It is a port of `SelectionOverlayController.swift` without window mode.
  - `Task<SelectionResult?> SelectRegionAsync(CapturePurpose purpose)`: shows the overlay, completes when the user finishes or cancels (`null` = cancelled). It is backed by a `TaskCompletionSource<SelectionResult?>(TaskCreationOptions.RunContinuationsAsynchronously)` that is completed **exactly once** (`TrySetResult` plus a field cleared before completing). If it is called while already presenting, it logs (`Debug.Fail` in Debug, `Trace.WriteLine` in Release) and returns `null`.
  - **Present:**
    1. `foregroundRestorer.Remember()`, before any overlay window exists.
    2. Snapshot `DisplayInfoProvider.GetCurrent()` (0028) as the session's displays. If the list is empty, cancel at once.
    3. Create one `SelectionOverlayWindow` per monitor, `EnsureHandle()` + position as above, then `Show()` each.
    4. Make the overlay keyboard-reachable: pick the window on the monitor containing `GetCursorPos()` (physical pixels; fallback: the first window), then call `Activate()` and `SetForegroundWindow(hwnd)`, then `Keyboard.Focus(window)`. This works because `WM_HOTKEY` and the tray menu click both give Snapper the right to take the foreground (the last input went to our process). If `GetForegroundWindow()` afterwards isn't the chosen overlay, log it. The overlay still works, and clicking it activates it, after which Esc works.
    5. Subscribe to `DisplayChangeWatcher.Changed` (0028; the coordinator owns the single watcher instance and passes it to the controller).
  - **Input:** `MouseDown(PointD)` → `session.MouseDown(point, displays)`. `MouseDragged(PointD)` → `session.MouseDragged(point)`. After each, push `SelectionOverlayRenderState.For(window.Display, session.Tracker, window.MeasureLabel)` to **every** window. `MouseUp(PointD)` → `session.MouseUp(point)`: `Rectangle(result)` → **finish**; `None` → push `Empty` to every window, and the overlay stays open, ready for a new drag. `CaptureLost()` → `session.Reset()` and push `Empty`.
  - **Esc / Alt+F4** → **cancel**.
  - **Display change:** the watcher raises `Changed` on the dispatcher, and only for real changes. On `Changed`, check `DisplayConfigurationChange.HasChanged(displays, e.New)` (the session snapshot against the new list). If it's true, **cancel**. Spurious `WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE` and work-area-only changes never reach the controller.
  - **Close** (shared): `session.Reset()`; unsubscribe from the watcher; for each window: `CaptureExclusion.Unregister`, mark it controller-closed, `Close()`; drop the list; then focus: `SelectionFocusPolicy.RestoresPreviousWindow(purpose, outcome)` ? `foregroundRestorer.Restore()` : `foregroundRestorer.Forget()`.
  - **Removal before capture:** on **finish**, close first, then call `DwmFlush()`, then `await Task.Delay(DismissalDelay)` with `static readonly TimeSpan DismissalDelay = TimeSpan.FromMilliseconds(50)` (same value as `SelectionOverlayController.swift:7`), then complete the task with the result. On **cancel**, complete with `null` right after closing. Guard against a cancel that arrives during the dismissal delay with an `isDismissing` flag (port of `:117`, `:152`).

- **`App/AppCoordinator.cs`** (modify 0025/0029's stubs): when `AppModeMachine` returns "show overlay" for a purpose, the coordinator runs `var result = await overlay.SelectRegionAsync(purpose);` on the dispatcher. The mode must already be `selecting` **before** the windows appear (0029's machine guarantees this when the action is returned).
  - `null` → send `selectionCancelled`.
  - non-`null` → send `selectionFinished`, then `Trace.WriteLine($"Selection for {purpose}: display {display.DeviceName}, rect ({x}, {y}, {w}, {h}) px")`.
  - **Intermediate behavior per purpose, until later specs land:**
    - **Screenshot (Ctrl+Shift+4):** the overlay closes, focus returns to the previous window, and nothing is captured or saved. 0031 replaces the log line with capture.
    - **Record (Ctrl+Shift+5):** the overlay closes and focus returns. The machine moves to `recording` on `selectionFinished`, so the coordinator immediately sends `recordingEnded` (with `// TODO(0034): start RecordingSession here`). The tray stays in its idle look and all shortcuts keep working.
    - **Annotate (Ctrl+Shift+6):** the overlay closes and, per the focus policy, Snapper does **not** restore the previous window. Nothing opens yet (the annotation window is 0040). Windows then activates the next window in z-order on its own, which is normally the previous window. This is acceptable for the intermediate state.
  - Shortcuts pressed while the overlay is open still arrive as `WM_HOTKEY` and are ignored by `AppModeMachine` (0029). The overlays are topmost and cover the taskbar, so the tray icon can't be clicked anyway.

- **`NativeMethods.txt`** (add): `SetWindowDisplayAffinity`, `WINDOW_DISPLAY_AFFINITY`, `SetWindowPos`, `HWND_TOPMOST`, `GetWindowLongPtr`, `SetWindowLongPtr`, `WINDOW_EX_STYLE`, `GetForegroundWindow`, `SetForegroundWindow`, `IsWindow`, `IsWindowVisible`, `GetClassName`, `GetWindowThreadProcessId`, `GetWindowRect`, `GetCursorPos`, `SetCapture`, `ReleaseCapture`, `DwmFlush`, `WM_LBUTTONDOWN`, `WM_LBUTTONUP`, `WM_MOUSEMOVE`, `WM_CAPTURECHANGED`, `WM_DPICHANGED` (skip any that 0025/0028/0029 already list).

- No new projects or packages. No PRD or AGENTS.md change in this spec.

## Acceptance criteria

- [ ] Ctrl+Shift+4, Ctrl+Shift+5, Ctrl+Shift+6, and the tray menu's Screenshot / Record / Annotate items each show a dimmed overlay on **every** monitor, covering the taskbar and maximized and full-screen windows.
- [ ] The overlays are not in the taskbar or Alt+Tab, and they don't appear in screenshots taken by other tools while they're open.
- [ ] The cursor is a crosshair over every monitor.
- [ ] Dragging draws a rectangle: the inside is clear with a thin white border, and everything else (including all other monitors) stays dimmed. The drawn rectangle matches the dragged area exactly at 100%, 125%, 150%, and 200% scale.
- [ ] A live `W × H` readout in integer DIPs of the start monitor, using `×`, follows the cursor and never leaves the start monitor. It flips sides near its right and bottom edges.
- [ ] A drag that continues onto another monitor is clamped to the edge of the start monitor, and the readout and the result use the clamped rect.
- [ ] A click, or a drag with either side under 4 DIPs (4 px at 100%, 6 px at 150%, 8 px at 200%), does nothing. Nothing is logged, the overlay stays open, and a new drag works.
- [ ] Releasing a ≥ 4 × 4 DIP drag closes every overlay and logs the purpose, monitor id, and rect in physical pixels, fully inside that monitor's bounds.
- [ ] Esc (and Alt+F4 on an overlay) closes every overlay at any time, before or during a drag, and logs no selection.
- [ ] While the overlay is open, Ctrl+Shift+4/5/6 do nothing (no second set of overlays, and one Esc closes everything).
- [ ] Connecting or disconnecting a monitor, or changing a monitor's arrangement, resolution, or scale while the overlay is open, cancels it on every monitor. Moving or auto-hiding the taskbar doesn't.
- [ ] After Esc, a display-change cancel, or a Ctrl+Shift+4/5 selection, the window that was in the foreground before the shortcut has keyboard focus again. After a Ctrl+Shift+6 selection, Snapper does not call `SetForegroundWindow` on it.
- [ ] Every overlay window is closed and the 50 ms dismissal delay has elapsed before the result is delivered.
- [ ] After every outcome the app is idle again: all three shortcuts and the tray menu work.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` pass.

## Edge cases

- **Mixed DPI** (100% + 150% + 200%): the tracker works in physical pixels. The threshold and readout use the start monitor's scale, and each overlay renders in its own monitor's DIPs. Covered by tests.
- **Negative-origin monitors** (left of or above the primary monitor): containment, clamping, and the pixel→DIP conversion are relative to `Bounds`, so they work for negative coordinates. Covered by tests. Client coordinates from `lParam` must be read as signed, because they go negative when the captured cursor moves left of or above the window.
- **Cursor on a shared edge** at mouse-down: the half-open rule assigns it to the right/lower monitor. A point outside every monitor (it can happen at the virtual-screen's outer edge) uses the nearest monitor.
- **Mouse-down on one monitor, mouse-up on another:** `SetCapture` keeps messages on the start window, and global positions keep the result on the start monitor.
- **Mouse-up without a preceding mouse-down** (for example, a press that began before the overlay appeared): `Ignored` → `None`, and the overlay stays open.
- **Capture lost mid-drag** (`WM_CAPTURECHANGED`, for example Ctrl+Alt+Del or a UAC prompt on the secure desktop): the drag resets and the overlay stays open.
- **Overlay loses the foreground** (for example, Win key, or Alt+Tab to another window): the overlay stays open (topmost) and isn't cancelled. Clicking it reactivates it, and Esc works again. The PRD only cancels on Esc and display changes.
- **Spurious display-change notifications** (`WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE` with no real change): the watcher raises no `Changed`, so the overlay stays.
- **DPI change of a monitor the watcher's hidden window isn't on** while the overlay is open: that monitor's overlay receives `WM_DPICHANGED` and calls `Recheck()`, the watcher raises `Changed`, and the selection is cancelled. WPF may resize that overlay first; that's harmless because it's closing.
- **Previous foreground window was the taskbar** (the user opened the tray menu) **or the desktop**: nothing is remembered, nothing is restored, and Windows picks the next window.
- **Previous foreground window was a Snapper window** (Settings or an annotation window): it's remembered and restored like any other window.
- **Previous window closed while the overlay was open:** `IsWindow` is false, so the restore is skipped.
- **`SetWindowDisplayAffinity` fails** (it shouldn't on Windows 11): log it and continue. The overlay is closed before capture anyway.
- **Selection requested while already presenting:** prevented by `AppModeMachine`. If it happens anyway, the call returns `null` and logs.
- **Very large monitors (for example 7680×4320 at 288 DPI):** the readout shows DIPs (`2560 × 1440` for the full monitor).

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`, xUnit). Fixtures shared via a `SelectionFixtures` helper:
  `A = new DisplayInfo(@"\\.\DISPLAY1", bounds (0, 0, 1920, 1080), work area = bounds, Dpi 96, IsPrimary true)` (100%),
  `B = new DisplayInfo(@"\\.\DISPLAY2", (1920, 0, 2880, 1620), …, Dpi 144, false)` (right, 150%, 1920×1080 DIPs),
  `C = new DisplayInfo(@"\\.\DISPLAY3", (-3840, -500, 3840, 2160), …, Dpi 192, false)` (left, negative origin, 200%, 1920×1080 DIPs).
  Points are `PointD` in global pixels.
  Rects are `(x, y, width, height)`. `RectSelection.Rect` is a `PixelRect`, and `CurrentRect` is a `RectD`.

  **`RectSelectionTrackerTests.cs`** (port of `SnapperTests/RectSelectionTrackerTests.swift:10-120`, y-down, pixels):
  1. `ForwardAndReverseDrags`: down (100,100) on [A,B], `CurrentRect == (100,100,0,0)`. Up (400,300) → `Selected(RectSelection(A, (100,100,300,200)))`. Down (400,300), up (100,100) → the same.
  2. `CrossMonitorClampsToStartMonitor`: down (1800,500) on [A,B]. `Drag((2500,800))` returns `(1800,500,120,300)`. Up (2500,800) → `Selected`, display A, that rect.
  3. `VerticalClamping`: down (100,100), up (300,-50) → `(100,0,200,100)`. Down (100,100), up (300,2000) → `(100,100,200,980)`.
  4. `StartMonitorStaysFixedAt150Percent`: down (2000,100) on [A,B], drag to (500,500) → `(1920,100,80,400)`, display B.
  5. `NegativeOriginAt200Percent`: down (-100,100) on [A,C], up (50,-800) → `Selected(RectSelection(C, (-100,-500,100,600)))`.
  6. `ThresholdIsFourDipsPerStartMonitorDpi`:
     A: up from (100,100) to (103,110) → `TooSmall`, to (110,103) → `TooSmall`, to (104,104) → `Selected` with 4×4 px.
     B: down (2000,100), up (2005,200) → `TooSmall` (5 px = 3.33 DIP), up (2006,106) → `Selected` 6×6 px.
     C: down (-100,100), up (-93,200) → `TooSmall` (7 px), up (-92,108) → `Selected` 8×8 px.
     A 125% fixture `D = new DisplayInfo(@"\\.\DISPLAY4", (0,0,2400,1350), …, Dpi 120, true)`: down (0,0) on [D], up (5,5) → `Selected` (5 px = 4 DIP exactly, the epsilon case). Up (4,10) → `TooSmall`.
  7. `ClickIsTooSmall`: down (100,100), up (100,100) → `TooSmall`.
  8. `ThresholdUsesClampedRect`: down (1918,100) on [A,B], up (2600,300) → width 2 → `TooSmall`.
  9. `IdleAfterOutcomeAndNewDragWorks`: after a `TooSmall`, `CurrentRect == null`, and a new down/up pair → `Selected`.
  10. `ResetDiscardsDrag`: down, drag, `Reset()` → `CurrentRect == null`, `Drag(...) == null`, `MouseUp(...)` → `Ignored`.
  11. `NoMouseDown`: `Drag(...) == null`, `MouseUp(...) == Ignored` on a fresh tracker.
  12. `EmptyDisplaysStaysIdle`: `MouseDown((10,10), [])` → `StartDisplay == null`, `MouseUp` → `Ignored`.
  13. `OuterEdgeAndNearestFallback`: [A] alone, down (1920,1080) → `StartDisplay == A`, `CurrentPoint == (1920,1080)`; up (1800,1000) → `(1800,1000,120,80)`. On [A,B], down (5000,5000) → `StartDisplay == B`, `CurrentPoint == (4800,1620)`; up (4700,1500) → `(4700,1500,100,120)`, display B.
  14. `SharedEdgeBelongsToRightMonitor`: down (1920,500) on [A,B], up (2000,600) → display B, `(1920,500,80,100)`.

  **`SelectionSessionTests.cs`** (port of `SnapperTests/SelectionSessionTests.swift:76-93`):
  1. `ClicksAndBelowThresholdDragsStayOpen`: `MouseUp` without down → `None`. For ends (100,100), (103,200), (200,103) from down (100,100) on [A] → `None`.
  2. `FullDragReturnsRectangle`: down (100,100), dragged (300,200), up (300,200) → `Rectangle(RectSelection(A, (100,100,200,100)))`. `IsMouseDown` is `true` between down and up and `false` after.
  3. `NegativeOriginMonitorClamps`: down (-100,100) on [A,C], up (100,-800) → `Rectangle(RectSelection(C, (-100,-500,100,600)))`.
  4. `ResetClearsPress`: down, `Reset()` → `IsMouseDown == false`, `Tracker.CurrentRect == null`, up → `None`.

  **`SelectionReadoutTests.cs`** (port of `SnapperTests/SelectionReadoutTests.swift:6-45`, mirrored to y-down, display `(0,0,1440,900)`, label 60×20):
  1. `TextUsesRoundedDipsAndMultiplicationSign`: `Text((1440,900)) == "1440 × 900"`; `(0,0)` → `"0 × 0"`; `(99.5,10.4)` → `"100 × 10"`; `(12345,4)` → `"12345 × 4"`, also with `CultureInfo.CurrentCulture = de-DE` (no grouping separator).
  2. `TextOfDipSizes`: the conversion itself is tested in 0028, so this only checks the rounding of typical `DisplayGeometry.DipSize` outputs. `Text((200, 133.333…)) == "200 × 133"`, `Text((200.666…, 2)) == "201 × 2"`, `Text((2560, 1440)) == "2560 × 1440"`.
  3. `DefaultPlacementIsBelowRight`: cursor (500,400) → `(512,412)`.
  4. `FlipsLeftNearRightEdge`: cursor (1430,400) → `(1358,412)`.
  5. `FlipsAboveNearBottomEdge`: cursor (500,890) → `(512,858)`.
  6. `BottomRightCornerFlipsBoth`: cursor (1430,890) → `(1358,858)`.
  7. `TopLeftCorner`: cursor (0,0) → `(12,12)`.
  8. `NegativeOriginDisplayRect`: display `(-1920,-200,1920,1080)`, cursor (-5,875) → `(-77,843)`, and the label rect lies inside the display deflated by 4.
  9. `OversizedLabelPinsToInsetLeft`: label 2000×20, cursor (500,400) → `x == 4`.
  10. `LabelStaysInsideAcrossCursorGrid`: for cursors on a 10×10 grid over `(0,0,1440,900)` and over `(-1920,-200,1920,1080)`, the label rect is always inside the display deflated by 4.

  **`SelectionOverlayRenderStateTests.cs`** (measure = constant 60×20):
  1. `IdleTrackerIsEmptyEverywhere`: `For(A, idle tracker)` and `For(B, …)` == `Empty`.
  2. `At150PercentConvertsToLocalDips`: tracker down (2070,150), drag (2370,450) on [A,B] → `For(B)`: `SelectionDips == (100,100,200,200)`, `ReadoutText == "200 × 200"`, `ReadoutOriginDips == (312,312)`. `For(A) == Empty`.
  3. `NegativeOriginAt200Percent`: down (-3800,-400), drag (-3000,0) on [A,C] → `For(C)`: `SelectionDips == (20,50,400,200)`, text `"400 × 200"`, origin `(432,262)`.
  4. `At100PercentNearBottomRightFlips`: down (1000,500), drag (1915,1075) on [A] → `For(A)`: `SelectionDips == (1000,500,915,575)`, origin `(1843,1043)`.
  5. `CrossMonitorDragRendersOnlyOnStartMonitor`: down (1800,500) on [A,B], drag (2500,800) → `For(A).SelectionDips == (1800,500,120,300)`, and `For(B) == Empty`.

  **`SelectionFocusPolicyTests.cs`** (port of 0022's tests):
  1. `CancelledAlwaysRestores`: for every `CapturePurpose` value (`Enum.GetValues`), `RestoresPreviousWindow(p, Cancelled) == true`.
  2. `ScreenshotSelectionRestores`, `RecordSelectionRestores`: `Selected` → `true`.
  3. `AnnotateSelectionKeepsFocus`: `Annotate`, `Selected` → `false`.
  4. `ShouldRememberNormalVisibleWindow`: `(true, true, "Chrome_WidgetWin_1")` → `true`. `(true, true, "HwndWrapper[Snapper;;…]")` → `true`.
  5. `ShouldNotRememberShellOrHiddenOrDeadWindows`: class `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `Progman`, `WorkerW` → `false`. `isVisible == false` → `false`. `isWindow == false` → `false`.

- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` both succeed (they run on macOS too).

- **Manual verification** (Windows 11; ideally two or three monitors at different scales, for example a 100% monitor and a 150% monitor, one placed left of the primary so it has a negative origin; run a Debug build under a debugger or with DebugView to see `Trace` output):
  1. Press Ctrl+Shift+4. Every monitor dims, including the taskbar, and the cursor is a crosshair everywhere. Press Esc: every overlay disappears, and no "Selection for" line is logged.
  2. Repeat step 1 with Ctrl+Shift+5, Ctrl+Shift+6, and each of the tray menu's Screenshot / Record / Annotate items.
  3. With the overlay open, press Alt+Tab and look at the taskbar: no overlay entry appears in either. Press Esc (click the overlay first if it lost focus).
  4. Ctrl+Shift+4 on the 100% monitor, drag a rectangle. The inside is clear with a thin white border, the rest dims, and the other monitors stay fully dimmed. The readout shows `W × H` and updates live. Release: every overlay closes, and the log shows `Screenshot`, the monitor id, and the rect in pixels.
  5. Repeat step 4 on the 150% monitor. The rectangle edges follow the cursor exactly (no offset or scale drift). A drag of about 200 × 100 DIPs shows roughly `200 × 100`, and the logged pixel rect is about 300 × 150.
  6. Start a drag near the right edge of one monitor and move onto the next (try 100% → 150% and 150% → 100%). The rectangle stops at the start monitor's edge, the readout stays on the start monitor, and the logged rect lies within the start monitor's pixel bounds.
  7. On the negative-origin monitor, drag a rectangle and move past its top-left corner. It clamps to that corner, and the logged x/y are negative and inside its bounds.
  8. Drag toward a monitor's bottom-right corner: the readout flips left/above the cursor and stays on screen.
  9. Click without moving: nothing happens, and the overlay stays open. Make a 2–3 px drag: same. Then make a normal drag: it's logged.
  10. With the overlay open, press Ctrl+Shift+4, Ctrl+Shift+5, and Ctrl+Shift+6: nothing changes. One Esc closes everything.
  11. During a drag, press Esc: every overlay closes. Open it again and press Alt+F4 instead: every overlay closes, and Snapper keeps running (the tray icon is still there).
  12. Focus a Notepad window, press Ctrl+Shift+4, then Esc: typing goes to Notepad. Repeat and finish a selection: typing goes to Notepad. Repeat with Ctrl+Shift+5: typing goes to Notepad.
  13. Focus Notepad, press Ctrl+Shift+6, and finish a selection. Nothing opens (the annotation window is 0040), and the log shows `Annotate`. Then press Ctrl+Shift+6 and Esc: typing goes to Notepad.
  14. With a maximized or full-screen app (for example F11 in Edge) in front, press Ctrl+Shift+4: the overlay covers it, and Esc works without clicking first.
  15. Open the overlay, then change a monitor's scale in Settings → System → Display (use the keyboard or a second device, or a timed script), or unplug/plug a monitor: every overlay closes, and the app is idle again. A new Ctrl+Shift+4 covers the new monitor set at the right size. Do the scale change once on the **primary** monitor and once on a **secondary** monitor. The secondary case exercises the overlay's `WM_DPICHANGED` → `DisplayChangeWatcher.Recheck()` path.
  16. Open the overlay and toggle taskbar auto-hide (or let it pop up): the overlay stays open.
  17. Open the Snipping Tool, set a 5-second delay, start a snip, then press Ctrl+Shift+4 within the delay. The resulting snip doesn't show Snapper's dimming (WDA_EXCLUDEFROMCAPTURE works). Press Esc.
  18. Ctrl+Shift+5, finish a selection: the log shows `Record`. Afterwards the tray icon has its idle look, and Ctrl+Shift+4 opens the overlay again.

## Out of scope

- Window mode, Space toggle, window highlight, and window candidates (0032-windows-window-selection-mode).
- Capturing, saving, copying, the shutter sound, and capture errors (0031-windows-screenshot-rectangle-capture).
- Starting a recording from the selection (0034-windows-recording-core). The pill and border use `CaptureExclusion` in 0035.
- Opening the annotation window after a Ctrl+Shift+6 selection, and giving it focus (0040-windows-annotation-window-canvas).
- Hotkey registration and `AppModeMachine` itself (0029), and display enumeration and the change watcher (0028).

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**. `dotnet test windows/Snapper.slnx` passed **462 tests**, including **64 new cases**, with no skips. Logs: `/tmp/snapper-0030-build.log` and `/tmp/snapper-0030-test.log`.

Implemented the pure tracker/session, monitor-local readout/render state and focus policy, plus WPF overlays, native mouse capture, capture exclusion/registry, focus restoration, display-change cancellation and the 50 ms close-before-result delay. The negative-origin render example's x-coordinate is corrected to 432: the cursor is at local x = 420 and the readout offset is 12. This corrects arithmetic only; the specified formula and PRD behavior are unchanged. Alt+F4 cancellation is posted after the WPF Closing callback returns to avoid recursively closing the same window. PRD and AGENTS.md required no changes.

GUI behavior has not been verified. On Windows 11 with mixed-DPI monitors:

- [ ] not yet run by user: Press Ctrl+Shift+4. Every monitor dims, including the taskbar, and the cursor is a crosshair everywhere. Press Esc: every overlay disappears, and no "Selection for" line is logged.
- [ ] not yet run by user: Repeat step 1 with Ctrl+Shift+5, Ctrl+Shift+6, and each of the tray menu's Screenshot / Record / Annotate items.
- [ ] not yet run by user: With the overlay open, press Alt+Tab and look at the taskbar: no overlay entry appears in either. Press Esc (click the overlay first if it lost focus).
- [ ] not yet run by user: Ctrl+Shift+4 on the 100% monitor, drag a rectangle. The inside is clear with a thin white border, the rest dims, and the other monitors stay fully dimmed. The readout shows `W × H` and updates live. Release: every overlay closes, and the log shows `Screenshot`, the monitor id, and the rect in pixels.
- [ ] not yet run by user: Repeat step 4 on the 150% monitor. The rectangle edges follow the cursor exactly (no offset or scale drift). A drag of about 200 × 100 DIPs shows roughly `200 × 100`, and the logged pixel rect is about 300 × 150.
- [ ] not yet run by user: Start a drag near the right edge of one monitor and move onto the next (try 100% → 150% and 150% → 100%). The rectangle stops at the start monitor's edge, the readout stays on the start monitor, and the logged rect lies within the start monitor's pixel bounds.
- [ ] not yet run by user: On the negative-origin monitor, drag a rectangle and move past its top-left corner. It clamps to that corner, and the logged x/y are negative and inside its bounds.
- [ ] not yet run by user: Drag toward a monitor's bottom-right corner: the readout flips left/above the cursor and stays on screen.
- [ ] not yet run by user: Click without moving: nothing happens, and the overlay stays open. Make a 2–3 px drag: same. Then make a normal drag: it's logged.
- [ ] not yet run by user: With the overlay open, press Ctrl+Shift+4, Ctrl+Shift+5, and Ctrl+Shift+6: nothing changes. One Esc closes everything.
- [ ] not yet run by user: During a drag, press Esc: every overlay closes. Open it again and press Alt+F4 instead: every overlay closes, and Snapper keeps running (the tray icon is still there).
- [ ] not yet run by user: Focus a Notepad window, press Ctrl+Shift+4, then Esc: typing goes to Notepad. Repeat and finish a selection: typing goes to Notepad. Repeat with Ctrl+Shift+5: typing goes to Notepad.
- [ ] not yet run by user: Focus Notepad, press Ctrl+Shift+6, and finish a selection. Nothing opens (the annotation window is 0040), and the log shows `Annotate`. Then press Ctrl+Shift+6 and Esc: typing goes to Notepad.
- [ ] not yet run by user: With a maximized or full-screen app (for example F11 in Edge) in front, press Ctrl+Shift+4: the overlay covers it, and Esc works without clicking first.
- [ ] not yet run by user: Open the overlay, then change a monitor's scale in Settings → System → Display (use the keyboard or a second device, or a timed script), or unplug/plug a monitor: every overlay closes, and the app is idle again. A new Ctrl+Shift+4 covers the new monitor set at the right size. Do the scale change once on the **primary** monitor and once on a **secondary** monitor. The secondary case exercises the overlay's `WM_DPICHANGED` → `DisplayChangeWatcher.Recheck()` path.
- [ ] not yet run by user: Open the overlay and toggle taskbar auto-hide (or let it pop up): the overlay stays open.
- [ ] not yet run by user: Open the Snipping Tool, set a 5-second delay, start a snip, then press Ctrl+Shift+4 within the delay. The resulting snip doesn't show Snapper's dimming (WDA_EXCLUDEFROMCAPTURE works). Press Esc.
- [ ] not yet run by user: Ctrl+Shift+5, finish a selection: the log shows `Record`. Afterwards the tray icon has its idle look, and Ctrl+Shift+4 opens the overlay again.
