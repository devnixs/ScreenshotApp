# 0035 — Windows: show the recording pill and dashed border, and make Cancel discard the recording

> Source: docs/PRD.md §7 item 3 (pill placement, `elapsed time · Stop · Cancel`, stays on the recorded display, dashed border, excluded from the recording), §7 item 4 (Stop saves, Cancel discards immediately without confirmation, Esc doesn't cancel), §12 (video contains neither pill nor border; Cancel discards), §13.1 (excluded windows, tray-only app: the pill and border never appear in the taskbar or Alt+Tab), §13.2 (points → DIPs, visible frame → monitor work area), §13.4 (physical-pixel global coordinates, no Y flip), §13.6, §13.11 ("Overlays, the recording pill, and the border never appear in screenshots or recordings")
> Depends on: 0034-windows-recording-core (and transitively 0025, 0026, 0027, 0028, 0029, 0030, 0031)

## Context

After 0034, Ctrl+Shift+5 → select a rectangle → recording starts immediately, the tray icon turns
into the red record dot, and Stop works through Ctrl+Shift+5 or a tray click (saves the `.mp4` and
opens File Explorer with it selected). There is no on-screen indication of what is being recorded,
no elapsed time, and no way to throw a recording away. `windows/` doesn't exist in the repo yet;
every Windows path below is created by this spec or a lower-numbered sibling.

PRD §7 item 3:

> A floating pill below the recorded area, or above it if there is no room below. If there is no
> room above either (for example, a full-screen selection), it is placed inside the area, near its
> bottom edge. It shows `elapsed time · Stop · Cancel` and stays on the recorded area's display.
> A thin dashed border marks the recorded area.

§7 item 4: **Stop** saves (same as Ctrl+Shift+5 / tray click); **Cancel** discards the recording
immediately without confirmation; **Esc does not cancel**. §13.1: the pill and border call
`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` and never appear in the taskbar or Alt+Tab.
§13.2: "points" are DIPs, a display's "visible frame" is the monitor's **work area** (excludes the
taskbar).

### macOS reference implementation (port behavior and tests, don't redesign)

- `Snapper/Recording/RecordingPillPlacement.swift:1-40`: below → above → inside, clamp to the
  visible frame, centre horizontally, round; `reachableFrame` (`:12-16`) for window mode.
- `Snapper/Recording/RecordingBorderGeometry.swift:1-15`: 3 pt outset, clipped to the display
  (unless `followsWindow`), 1.5 pt stroke inset.
- `Snapper/Recording/ElapsedTimeFormatter.swift:1-10`: truncated `m:ss` / `h:mm:ss`, negative → `0:00`.
- `Snapper/Recording/RecordingPillPanel.swift:1-70`: non-activating panel, 32 pt high, corner
  radius 16, label + Stop + Cancel, buttons accept first mouse, `disableButtons()`.
- `Snapper/Recording/RecordingBorderWindow.swift:1-42`: click-through window, underlay 2 pt black
  35 % + white 1 pt dashes `[4, 4]`, butt caps.
- `Snapper/Recording/RecordingOverlayController.swift:1-126`: `show` / `update(recordedRect:on:)` /
  `setBorderVisible` / `dismiss` (idempotent), 250 ms timer deriving elapsed time from the start
  instant, re-place on screen changes and when the label width changes, Stop/Cancel disable +
  dismiss **before** invoking the callback (`:121-125`).
- `Snapper/App/AppCoordinator.swift:107-170`: the overlay is created in the session's `onStarted`
  callback, dismissed in `onStopping`/`onEnded`/stop/cancel; `cancelRecording()` dismisses then
  calls `RecordingSession.cancel()`.
- Tests: `SnapperTests/RecordingPillPlacementTests.swift:5-78`,
  `SnapperTests/RecordingBorderGeometryTests.swift:5-32`,
  `SnapperTests/ElapsedTimeFormatterTests.swift:5-15`; Cancel semantics in
  `SnapperTests/RecordingLifecycleTests.swift:34-67,144` (ported by 0034).
- Spec: `docs/specs/done/0012-recording-overlay_COMPLETED.md`.

**macOS facts that don't apply on Windows:** there is no Y flip (AppKit bottom-left points vs
Windows top-left physical pixels), so "below" means a *larger* y; there is no `SCContentFilter`
app-level exclusion (Windows excludes per window with `WDA_EXCLUDEFROMCAPTURE`, applied to each
pill/border HWND as it is created); no Spaces / `collectionBehavior`; no menu bar; no Dock (the
taskbar is excluded through the work area); no `acceptsFirstMouse` (the Windows equivalent is
`WS_EX_NOACTIVATE` + `MA_NOACTIVATE`).

### Types this spec relies on from siblings

- `Snapper.Core.Geometry`: `PixelRect`, `PixelSize`, `SizeD`, `RectD`, `DisplayInfo` (monitor
  bounds and work area in physical pixels, scale factor), `DisplayGeometry` (DIP↔pixel conversion,
  monitor lookup), `WindowDisplayRule` (0028). Use 0028's actual member names; this spec writes
  `Bounds`, `WorkArea`, `Scale`, `Left/Top/Right/Bottom` (Right/Bottom exclusive) for readability.
- `Snapper.Windows.Capture.DisplayInfoProvider`, `DisplayChangeWatcher` (0028).
- `Snapper.Windows.Support.CaptureExclusion` (0030): applies `WDA_EXCLUDEFROMCAPTURE` to an HWND and
  registers it as a Snapper window so window-mode hit-testing skips it.
- `Snapper.Windows.Recording.RecordingSession`, `Snapper.Core.Recording.RecordingLifecycle`
  (0034, port of the Swift session/lifecycle including `CancelRequested` → delete file, no reveal,
  no alert), and 0034's started / stopping / ended callbacks.
- `Snapper.Windows.App.AppCoordinator` (0025, extended by 0029/0034).

## User story

As a Snapper user on Windows who is recording, I see a thin dashed border around the area being
recorded and a small floating pill next to it with a running timer, a **Stop** button that saves
the video and a **Cancel** button that throws it away. Clicking them never steals focus from the
app I'm working in, and none of it ever shows up in the video.

## Scope & changes

### Pure logic — `Snapper.Core` (xUnit-tested, no WPF/WinForms/interop)

- **`windows/src/Snapper.Core/Recording/RecordingPillPlacement.cs`** — `public static class
  RecordingPillPlacement`, port of the Swift type, in **physical pixels, y down**:
  ```csharp
  public enum PillPosition { Below, Above, Inside }
  public readonly record struct PillPlacementResult(PixelRect Frame, PillPosition Position);
  public const double DefaultMarginDips = 8;
  public const double PillHeightDips = 32;
  public static PixelSize PillPixelSize(SizeD pillSizeDips, double scale);
  public static int MarginPixels(double scale, double marginDips = DefaultMarginDips);
  public static PillPlacementResult Place(SizeD pillSizeDips, PixelRect recordedRect,
                                          DisplayInfo display, double marginDips = DefaultMarginDips);
  public static PixelRect ReachableFrame(PixelRect frame, PixelRect workArea);
  ```
  - `recordedRect` is the recorded area in virtual-screen physical pixels. `display` is the
    recorded area's monitor (rectangle mode: the selection's monitor; a window recorded as a
    rectangle before 0036: the `WindowDisplayRule` monitor). Placement uses `display.WorkArea`
    and `display.Scale` only, so the pill always stays on that monitor and off the taskbar.
  - `PillPixelSize`: width and height = DIPs × scale, **rounded up** (ceiling) so the content never
    clips. Do the conversion through `DisplayGeometry`'s DIP→pixel helper (AGENTS.md: every
    pixel↔DIP conversion goes through it); if 0028 has no size conversion with ceiling rounding,
    add one as an overload on `DisplayGeometry` with its own test here (extending, not redefining).
  - `MarginPixels`: `marginDips × scale`, rounded half away from zero (`MidpointRounding.AwayFromZero`,
    matching Swift's `.rounded()`): 8 / 10 / 12 / 16 px at 100 / 125 / 150 / 200 %.
  - `Place` algorithm, in this order (`w`, `h` = pill pixel size, `m` = margin pixels, `wa` = work area):
    1. **Below:** `y = recordedRect.Bottom + m`. Fits if `y + h <= wa.Bottom` (inclusive).
    2. **Above** (only if below doesn't fit): `y = recordedRect.Top - m - h`. Fits if `y >= wa.Top`.
    3. **Inside** (neither fits): `y = min(recordedRect.Bottom, wa.Bottom) - m - h` — near the
       bottom edge of the part of the area that's in the work area (a full-monitor selection's
       bottom is behind the taskbar, so the pill is lifted above it).
    4. Clamp `y` into `[wa.Top, wa.Bottom - h]`; if `h > wa.Height`, `y = wa.Top`.
    5. **Horizontal:** `x = recordedRect.Left + recordedRect.Width / 2.0 - w / 2.0`, rounded half
       away from zero, then clamped into `[wa.Left, wa.Right - w]`; if `w > wa.Width`, `x = wa.Left`.
       Implement the clamps as `max(min, min(value, max))` like the Swift code so the oversized case
       falls out without special-casing and never throws (`Math.Clamp` throws when min > max — don't use it).
  - **Why the work area** (same reasoning as macOS `visibleFrame`): "room" means room where the
    pill is visible and clickable; the taskbar sits above normal windows. The recorded area itself
    may extend beyond the work area (full-monitor selection); step 3 handles that.
  - `ReachableFrame`: port of `reachableFrame` — keeps `frame`'s size, clamps its origin into the
    work area (`x = max(wa.Left, min(frame.Left, wa.Right - frame.Width))`, same for y). Used by
    0036 to keep a hidden window's pill reachable at its last position; added here because it is
    part of the type.

- **`windows/src/Snapper.Core/Recording/RecordingBorderGeometry.cs`** — `public static class
  RecordingBorderGeometry`:
  ```csharp
  public const double OutsetDips = 3;        // border window extends beyond the recorded rect
  public const double StrokeInsetDips = 1.5; // line centre, measured from the window edge
  public const double UnderlayWidthDips = 2, DashWidthDips = 1, DashLengthDips = 4, DashGapDips = 4;
  public static int OutsetPixels(double scale);                    // ceiling(3 × scale)
  public static PixelRect WindowFrame(PixelRect recordedRect, PixelRect monitorBounds,
                                      double scale, bool followsWindow = false);
  public static RectD StrokeRect(SizeD windowSizeDips);            // inset by 1.5 on every side
  ```
  - `WindowFrame` = `recordedRect` inflated by `OutsetPixels(scale)` on every side, then
    intersected with `monitorBounds` (the full monitor bounds, **not** the work area — the border
    may sit over the taskbar) unless `followsWindow` (0036: a window can span monitors and the
    border must enclose the whole window). `scale` is the recorded monitor's scale.
  - **Outset rounds up** (3 → 3, 3.75 → 4, 4.5 → 5, 6 → 6 px) so the drawn line (underlay covers
    0.5–2.5 DIP from the window edge) lies entirely **outside** the recorded rect wherever the
    area isn't at a monitor edge. The border is therefore not in the crop even if exclusion failed.
    Where the area touches a monitor edge the window is clipped and the line falls just inside the
    area; there `WDA_EXCLUDEFROMCAPTURE` is the only guarantee, which is acceptable because it is
    the primary guarantee everywhere (same trade-off as macOS 0012).

- **`windows/src/Snapper.Core/Recording/ElapsedTimeFormatter.cs`** — `public static class
  ElapsedTimeFormatter` with `public static string Format(TimeSpan elapsed)`. Whole seconds,
  **truncated** (use `elapsed.Ticks / TimeSpan.TicksPerSecond`, not `TotalSeconds` rounding).
  Negative → `"0:00"`. Under one hour `m:ss`, otherwise `h:mm:ss` (hours unbounded, e.g.
  `"100:00:00"`). Format with `CultureInfo.InvariantCulture`. Tabular digits are a rendering concern.

### Platform glue — `Snapper.Windows` (WPF + interop, manually verified)

- **`windows/src/Snapper.Windows/Recording/RecordingPillWindow.xaml(.cs)`** — `internal sealed
  class RecordingPillWindow : Window`.
  - WPF properties: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`,
    `ResizeMode=NoResize`, `ShowInTaskbar=False`, `ShowActivated=False`, `Topmost=True`,
    `SizeToContent=Manual`, `Focusable=False`. Never set `Left`/`Top`/`Width`/`Height` for
    placement; position only with `SetWindowPos` in physical pixels (AGENTS.md).
  - In `SourceInitialized` (via `WindowInteropHelper`/`HwndSource`): add extended styles
    `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` (no activation, no taskbar button, no
    Alt+Tab), call `CaptureExclusion` (0030) to apply `WDA_EXCLUDEFROMCAPTURE` and register the
    HWND as a Snapper window, and add an `HwndSource` hook that:
    - answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE` (handled), so clicking Stop/Cancel never
      activates Snapper or takes focus from the foreground app;
    - on `WM_DPICHANGED` also calls 0028's `DisplayChangeWatcher.Recheck()` (the watcher's hidden
      window only gets this message for its own monitor), lets WPF process the message (not handled), then re-applies the
      controller's last pixel frame via `Dispatcher.BeginInvoke` (WPF would otherwise apply the
      suggested rect and change the size we computed).
  - Content (DIPs): a `Border` with `CornerRadius=16`, height `RecordingPillPlacement.PillHeightDips`
    (32), dark fill `#E6202020`, a 1 DIP `#33FFFFFF` outline, padding 14 left/right; inside a
    horizontal `StackPanel` (spacing 10) in PRD order:
    1. the elapsed-time `TextBlock`: Segoe UI, `FontSize=13`, `SemiBold`, white,
       `Typography.NumeralAlignment=Tabular` so digits don't jitter, initial text `"0:00"`;
    2. a **Stop** button (content "Stop");
    3. a **Cancel** button (content "Cancel").
    The PRD's `·` separators denote order; render them as spacing, not glyphs. Buttons use a flat
    template (transparent background, white text, `FontSize=12`, padding 8,2, corner radius 4,
    `#33FFFFFF` hover, `#55FFFFFF` pressed), `Focusable=False`, `IsTabStop=False`.
  - `public SizeD MeasureDips()`: measures the content with `Measure(∞)` and returns
    `(ceil(DesiredSize.Width), 32)`.
  - `public void SetElapsedText(string)`, `public void DisableButtons()`,
    `public event Action? StopClicked, CancelClicked`, `public void SetPixelFrame(PixelRect)`
    (`SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE)` and cache the frame for the
    `WM_DPICHANGED` re-apply).

- **`windows/src/Snapper.Windows/Recording/RecordingBorderWindow.cs`** — `internal sealed class
  RecordingBorderWindow : Window`.
  - Same WPF properties as the pill plus `IsHitTestVisible=False`. In `SourceInitialized`: extended
    styles `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`
    (`WS_EX_LAYERED` is already set by `AllowsTransparency`; `WS_EX_TRANSPARENT` makes every click
    pass through), `CaptureExclusion`, and the same `WM_DPICHANGED` re-apply hook.
  - Renders in `OnRender` (DIPs, using the window's current `VisualTreeHelper.GetDpi`): the
    `RecordingBorderGeometry.StrokeRect(windowSizeDips)` rectangle twice — underlay solid 2 DIP
    `#59000000` (black 35 %), then dashes 1 DIP white, `DashStyle` `[4, 4]` (in DIP units, i.e.
    dash array `{4, 4}` for a 1 DIP pen), `PenLineCap.Flat`. Set `SnapsToDevicePixels=True`.
    `windowSizeDips` = pixel frame size ÷ the window's current scale. Redraw only on resize/DPI
    change (`InvalidateVisual`), no animation.
  - `public void SetPixelFrame(PixelRect)` like the pill.

- **`windows/src/Snapper.Windows/Recording/RecordingOverlayController.cs`** — `internal sealed
  class RecordingOverlayController`, dispatcher thread only, owned by `AppCoordinator` for one
  recording. Port of the Swift controller:
  ```csharp
  public RecordingOverlayController(DisplayInfo display, PixelRect recordedRect, bool followsWindow,
      long startedAtTimestamp /* Stopwatch.GetTimestamp() */, Action onStop, Action onCancel);
  public void Show();
  public void Update(PixelRect recordedRect, DisplayInfo? display = null); // for 0036
  public void SetBorderVisible(bool visible);                               // for 0036
  public void Dismiss();                                                    // idempotent
  ```
  - `Show()`: no-op if dismissed or already shown. Creates the border window first, then the pill,
    for each: `new WindowInteropHelper(w).EnsureHandle()` (applies styles + exclusion before the
    window is ever visible), compute and apply the pixel frame, `Show()`, then apply the frame
    again (WPF may move a window on first show). The pill is shown last and gets `HWND_TOPMOST` last
    so it is above the border. Never call `Activate()` or `SetForegroundWindow`.
  - Placement (`Update`): look up the current `DisplayInfo` for the recorded monitor
    (`DisplayInfoProvider`, by the identity 0028 uses); if it's gone, do nothing (0034 stops and
    saves on monitor disconnect, which dismisses). Border frame =
    `RecordingBorderGeometry.WindowFrame(recordedRect, display.Bounds, display.Scale, followsWindow)`;
    pill frame = `RecordingPillPlacement.Place(pill.MeasureDips(), recordedRect, display).Frame`.
    If the border is hidden (0036), keep the pill at its last frame clamped with `ReachableFrame`,
    as in `RecordingOverlayController.swift:83-97`.
  - **Timer:** a `DispatcherTimer` (250 ms, `DispatcherPriority.Normal`) computes
    `Stopwatch.GetElapsedTime(startedAtTimestamp)`, formats it, and only when the string changes
    sets the label and re-runs placement (the label widens at `10:00` and `1:00:00`). Elapsed
    time is always derived from the start timestamp, never accumulated. The label shows `"0:00"`
    on `Show()`.
  - Subscribes to `DisplayChangeWatcher` (0028) while shown and re-runs placement (taskbar moved or
    auto-hidden, resolution or DPI changed: the pill's pixel size changes with the scale).
  - Stop/Cancel: `DisableButtons()`, `Dismiss()`, **then** invoke `onStop` / `onCancel`, so a double
    click or Stop-then-Cancel can't fire twice and the overlay never lingers while the encoder
    finalizes.
  - `Dismiss()`: stop the timer, unsubscribe the watcher, disable buttons, `Close()` both windows,
    unregister them from `CaptureExclusion`'s registry, drop references.

- **`AppCoordinator` wiring** (`windows/src/Snapper.Windows/App/AppCoordinator.cs`):
  - Create and `Show()` the controller **only in 0034's "started" callback** (after the WGC session
    and the sink writer started successfully), so a failed start never flashes the pill.
    `startedAtTimestamp` = `Stopwatch.GetTimestamp()` at that moment. Pass the recorded monitor's
    `DisplayInfo` and the recorded rect in physical pixels exactly as 0034 already holds them (no
    conversion in the controller). `followsWindow` is `false` until 0036.
  - `onStop` → the same stop path Ctrl+Shift+5 and the tray click use (0034).
  - `onCancel` → `CancelRecording()`: dismiss the overlay, then `RecordingSession.Cancel()`.
  - Every end path — Stop button, Ctrl+Shift+5, tray click, Cancel, monitor disconnect, write
    failure, Exit Snapper, shutdown/sign-out (0034's `ShutdownBlocker` path) — calls
    `Dismiss()` (via 0034's stopping/ended callbacks) and drops the reference.
  - **Exclusion:** add a one-line comment at the creation site: the pill and border are excluded
    per window through `WDA_EXCLUDEFROMCAPTURE` set before first show; unlike macOS there is no
    app-level filter, so any new Snapper window shown during a recording must do the same.

- **Cancel = discard** (`windows/src/Snapper.Windows/Recording/RecordingSession.cs`): if 0034
  already exposes `Cancel()` driving `RecordingLifecycle`'s `CancelRequested`, reuse it. Otherwise
  add it: send `CancelRequested`, finalize/abort the sink writer, then delete the output file
  (ignore "not found"; Cancel may arrive before the first frame). No confirmation, no Explorer
  reveal, no alert; cleanup errors go to `Debug.WriteLine`, not the user. Afterwards the tray icon
  returns to normal and `AppModeMachine` goes to idle through the same ended path as Stop. If
  0034's ported lifecycle lacks the cancel cases, add them and port the Swift tests listed in the
  test plan.

- **Esc does nothing** while recording: install no keyboard hook and register no Esc hotkey for
  recording mode. Because neither window can be activated, Esc reaches the foreground app.

- **`windows/src/Snapper.Windows/NativeMethods.txt`**: add (if not already present from
  0025/0030) `SetWindowPos`, `GetWindowLongPtr`, `SetWindowLongPtr`, `WINDOW_EX_STYLE`,
  `HWND_TOPMOST`, `WM_MOUSEACTIVATE`, `MA_NOACTIVATE`, `WM_DPICHANGED`, `SET_WINDOW_POS_FLAGS`.
  `SetWindowDisplayAffinity` comes through 0030's `CaptureExclusion`.

- **Intermediate state:** until 0036, a Ctrl+Shift+5 window selection is recorded as a rectangle of
  the window's frame at start (0034); the overlay treats it as a rectangle on the
  `WindowDisplayRule` monitor (border clipped to that monitor, pill on its work area). It doesn't
  follow the window. Until 0045/0046 there's no converting state; the overlay is already dismissed
  in the stopping callback, which is what those specs expect.

- **PRD:** no update; this implements §7.3/§7.4/§13.1 as written. Margin, sizes, colors, timer
  mechanism and the rounding rules are implementation details.

## Acceptance criteria

- [ ] When a recording starts, a dashed border appears around the recorded area and a pill
      showing `0:00`, **Stop**, **Cancel** appears centred 8 DIP below it.
- [ ] If there's no room below (inside the monitor's work area), the pill is above the area; if
      there's no room above either (full-monitor selection), it is inside the area near its bottom
      edge, above the taskbar.
- [ ] The pill never leaves the recorded area's monitor and is never under the taskbar, on 100 %,
      125 %, 150 % and 200 % monitors and on monitors left of or above the primary.
- [ ] The pill and border have the same DIP size on every monitor scale (crisp, not blurry).
- [ ] The timer ticks once per second (`0:01`, …, `1:00`, …) with digits that don't jitter.
- [ ] **Stop** saves the `.mp4` and opens File Explorer with it selected, exactly like Ctrl+Shift+5 or
      a tray click; pill and border disappear.
- [ ] **Cancel** stops immediately with no confirmation, leaves no file in the save folder, doesn't
      open File Explorer, and resets the tray icon.
- [ ] One click on Stop/Cancel works while another app is in the foreground and doesn't take
      focus from it.
- [ ] Esc during a recording does nothing to the recording.
- [ ] Clicks on the border pass through to whatever is underneath.
- [ ] The saved video contains neither the pill nor the border (PRD §12, §13.11).
- [ ] Neither window appears in the taskbar or Alt+Tab.
- [ ] Pill and border are removed on every end path: Stop, Ctrl+Shift+5, tray click, Cancel,
      monitor disconnect, write failure, Exit Snapper.
- [ ] `RecordingOverlayController.Update` and `SetBorderVisible` exist for 0036.

## Edge cases

- Full-monitor selection: below/above don't fit → inside, lifted above the taskbar; border window
  clipped to the monitor bounds, line just inside the edge (exclusion covers it).
- Taskbar on top/left/right, or auto-hidden: the work area reflects it; nothing special.
- Secondary monitor without a taskbar: work area = bounds.
- Area whose bottom is exactly `wa.Bottom - m - h`: below fits (inclusive); one pixel lower → above.
- Area at the left/right monitor edge → pill clamped, not centred. Area narrower than the pill →
  pill centred on it and wider than it.
- Mixed DPI: recording on a 150 % monitor while the primary is 100 % → pill is 300 × 48 px with a
  12 px margin; the WPF window first created on the primary monitor gets `WM_DPICHANGED` when moved
  — the hook re-applies our frame, so no wrong size or position.
- Fractional scales (125 %, 175 %): pill size rounds up, margin rounds half away from zero, border
  outset rounds up.
- Negative-origin monitors (left of or above the primary): all math in virtual-screen pixels.
- Display change mid-recording (taskbar moved, DPI or resolution changed): re-placed. Monitor
  disconnected: 0034 stops and saves; no crash if the monitor is gone when placement re-runs.
- Double click on Stop, or Stop then Cancel quickly: only the first action happens.
- Ctrl+Shift+5 after Stop was clicked but before the writer finished: 0029's machine ignores it; no
  second overlay.
- Cancel before the first frame was written: file may not exist → ignore not-found.
- Another topmost window (e.g. Task Manager "Always on top") may cover the pill; Stop via
  Ctrl+Shift+5/tray still works. Re-assert `HWND_TOPMOST` on every placement update.
- Elapsed time ≥ 1 h → `h:mm:ss`; the pill widens and is re-placed.

## Test plan

- **Unit tests** — `windows/tests/Snapper.Core.Tests/RecordingPillPlacementTests.cs`. Pill 200 × 32
  DIP, margin 8 DIP unless stated. Monitor A (100 %): bounds (0, 0, 1920, 1080), work area
  (0, 0, 1920, 1032) (48 px taskbar at the bottom). Rects are (x, y, w, h).
  1. Room below: rect (400, 300, 600, 400) → `Below`, (600, 708, 200, 32).
  2. Exactly enough room below: rect (400, 592, 600, 400) → `Below`, (600, 1000, 200, 32).
  3. One pixel lower → above: rect (400, 593, 600, 400) → `Above`, (600, 553, 200, 32).
  4. Full monitor: rect (0, 0, 1920, 1080) → `Inside`, (860, 992, 200, 32).
  5. Rect = work area (0, 0, 1920, 1032) → `Inside`, (860, 992, 200, 32).
  6. Left-edge clamp: rect (0, 300, 100, 200) → `Below`, (0, 508, 200, 32).
  7. Right-edge clamp: rect (1860, 300, 60, 200) → `Below`, (1720, 508, 200, 32).
  8. Tiny area: rect (700, 500, 20, 10) → `Below`, (610, 518, 200, 32).
  9. Taskbar at the top: work area (0, 48, 1920, 1032), full-monitor rect → `Inside`, (860, 1040, 200, 32).
  10. **150 %** monitor bounds (0, 0, 2880, 1620), work area (0, 0, 2880, 1548): rect (600, 450, 900, 600)
      → `Below`, (900, 1062, 300, 48).
  11. Same monitor, full-monitor rect → `Inside`, (1290, 1488, 300, 48).
  12. **200 %** monitor bounds (0, 0, 3840, 2160), work area (0, 0, 3840, 2064): rect (800, 600, 1200, 800)
      → `Below`, (1200, 1416, 400, 64).
  13. Same monitor, rect (800, 1200, 1200, 800) → `Above`, (1200, 1120, 400, 64).
  14. **Negative origin, 150 %** (left of primary): bounds (-2880, -300, 2880, 1620), work area
      (-2880, -300, 2880, 1548): rect (-1500, 0, 600, 450) → `Below`, (-1350, 462, 300, 48).
  15. Same monitor, area at its work-area bottom: rect (-2880, 900, 750, 348) → `Above`,
      (-2655, 840, 300, 48).
  16. Same monitor, left-edge clamp: rect (-2880, 0, 75, 75) → (-2880, 87, 300, 48).
  17. **Monitor above the primary** (100 %): bounds = work area (0, -1080, 1920, 1080), full-monitor
      rect → `Inside`, (860, -40, 200, 32).
  18. **Mixed DPI uses the recorded monitor's scale:** the same DIP pill placed for a rect on
      monitor A (100 %) and on the 150 % monitor gives sizes 200 × 32 and 300 × 48 and margins 8 / 12.
  19. **125 %**: `PillPixelSize((200, 32), 1.25)` = (250, 40); `MarginPixels(1.25)` = 10.
  20. Rounding up and midpoints: `PillPixelSize((201, 32), 1.5)` = (302, 48); placing it for rect
      (600, 450, 901, 600) on the 150 % monitor gives x = 900 (1050.5 − 151 = 899.5 → 900).
      `MarginPixels(1.75)` = 14.
  21. Oversized pill: `Place((2000, 1100), workArea-rect, monitor A)` → origin (0, 0), no exception.
  22. Wider elapsed label at the right edge: pill 260 DIP, rect (1860, 300, 60, 200) → `Frame.Right` = 1920.
  23. `ReachableFrame`: frame (-900, 100, 200, 40) in work area (-1000, 0, 1000, 900) → unchanged;
      in (0, 20, 1000, 880) → (0, 100, 200, 40); frame (900, 1000, 200, 40) in (0, 20, 1000, 880) →
      (800, 860, 200, 40) (port of `RecordingPillPlacementTests.swift:68-77`).
- **Unit tests** — `windows/tests/Snapper.Core.Tests/RecordingBorderGeometryTests.cs`:
  1. 100 %: rect (400, 300, 600, 400), bounds (0, 0, 1920, 1080) → (397, 297, 606, 406).
  2. Full-monitor rect → clipped to (0, 0, 1920, 1080).
  3. 150 % (outset 5): rect (600, 450, 900, 600), bounds (0, 0, 2880, 1620) → (595, 445, 910, 610).
  4. 200 % (outset 6): rect (800, 600, 1200, 800), bounds (0, 0, 3840, 2160) → (794, 594, 1212, 812).
  5. Negative origin, 150 %: bounds (-2880, -300, 2880, 1620), rect (-2880, 0, 750, 750) at the
     left edge → (-2880, -5, 755, 760).
  6. Monitor above the primary, 125 % (outset 4): bounds (0, -1350, 2400, 1350), rect
     (100, -1350, 500, 300) at its top edge → (96, -1350, 508, 304).
  7. `followsWindow` spanning monitors: rect (-700, 100, 1000, 600), bounds (-1000, 0, 1000, 900),
     scale 1 → (-703, 97, 1006, 606), not clipped.
  8. `OutsetPixels` for 1, 1.25, 1.5, 1.75, 2 → 3, 4, 5, 6, 6; and for each, `OutsetPixels(s) / s
     >= StrokeInsetDips + UnderlayWidthDips / 2` (the line is outside the recorded rect).
  9. `StrokeRect((606, 406))` → (1.5, 1.5, 603, 403).
- **Unit tests** — `windows/tests/Snapper.Core.Tests/ElapsedTimeFormatterTests.cs` (port of
  `ElapsedTimeFormatterTests.swift:6-14`): 0 → `"0:00"`; 999 ms → `"0:00"`; 59 s → `"0:59"`;
  60 s → `"1:00"`; 61 s → `"1:01"`; 3599 s → `"59:59"`; 3600 s → `"1:00:00"`; 36309 s →
  `"10:05:09"`; −5 s → `"0:00"`; −999 ms → `"0:00"`; 360000 s → `"100:00:00"`; and the result is
  the same under `CultureInfo.CurrentCulture = new("ar-SA")` (invariant digits).
- **Unit tests (only if 0034 didn't port them)** — `RecordingLifecycleTests.cs`: cancel while
  recording → `[StopCapture, ScheduleStopTimeout]`, then output finished → phase `Cancelled`,
  `[DeleteFile(url), Ended]`; double stop / cancel after stop ignored; cancel wins over a pending
  stop in either order; cancel discards on failure or timeout (ports of
  `RecordingLifecycleTests.swift:34-67,144`).
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  succeed.
- **Manual verification** (Windows 11 machine; can't be automated):
  1. Ctrl+Shift+5, drag a medium rectangle in the middle of the primary monitor. Expected: dashed
     border around it, pill centred just below showing `0:00`, then `0:01`, `0:02` … once per
     second; digits don't shift sideways.
  2. Drag a rectangle whose bottom edge is right above the taskbar. Expected: pill above the area.
  3. Drag over the entire monitor. Expected: pill inside the area near the bottom, fully above the
     taskbar. Repeat with the taskbar auto-hidden (pill near the very bottom).
  4. Drag small rectangles touching the left and right monitor edges. Expected: pill fully on screen.
  5. Two monitors at different scales (e.g. 100 % primary, 150 % secondary; then make the secondary
     left of the primary in Display settings so it has a negative origin): record an area on the
     secondary. Expected: pill and border only on that monitor, same physical-looking size as on
     the primary, crisp, border hugging the area.
  6. While recording, click into Notepad and type, then click **Stop** once. Expected: the single
     click stops recording; Notepad keeps focus (the caret stays, typing continues to go there);
     File Explorer opens with the new `Recording … .mp4` selected; pill and border gone; tray icon
     back to the viewfinder.
  7. Play that `.mp4` and the full-monitor one from step 3. Expected: no pill and no dashed border
     anywhere in the video.
  8. Start a recording, click **Cancel**. Expected: stops at once, no dialog, Explorer doesn't
     open, no new file in `%USERPROFILE%\Pictures\Screenshots`, tray icon resets.
  9. Start a recording, press Esc several times, wait, press Ctrl+Shift+5. Expected: Esc did nothing;
     the saved video includes the time after Esc.
  10. Start a recording and click a button directly under the dashed line. Expected: the click
      reaches the underlying app.
  11. While recording, press Alt+Tab and look at the taskbar. Expected: neither the pill nor the
      border appears.
  12. Stop via the tray icon; start another and stop via Ctrl+Shift+5; start another and choose
      Exit Snapper. Expected: pill and border disappear every time (and the last recording is saved).
  13. Click **Stop** twice quickly. Expected: one file, one Explorer window, no error.
  14. While recording on the secondary monitor, change its scale in Settings → Display, then move
      the taskbar. Expected: the pill is re-placed with the new size and stays on the work area.
  15. Record for over 10 minutes (or temporarily test at the right monitor edge). Expected: the
      pill widens at `10:00` and stays fully on screen.

## Out of scope

- Following a moving window (pill/border tracking, ~4 Hz frame polling, hidden/minimized window,
  window closed → stop and save): **0036-windows-window-recording**. This spec only provides
  `Update`, `SetBorderVisible`, `ReachableFrame`, and the `followsWindow` border rule.
- Starting/stopping capture, encoding, file naming, Explorer reveal, monitor-disconnect stop,
  failure alerts, tray icon state, shutdown handling: **0034-windows-recording-core**.
- Microphone: **0037**. Converting state and MP4/GIF quality: **0045**, **0046**.
- Selection overlay and its Esc handling: **0030** / **0032**.
- Pause/resume, countdown, dragging the pill: not in the PRD (§2 non-goals).
