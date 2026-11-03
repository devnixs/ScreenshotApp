# 0028 — Add Windows display geometry, monitor provider, and display-change watcher

> Source: docs/PRD.md §5 (lines 71, 81: single-display confinement, display changes), §13.1 (line 247: Per-Monitor V2), §13.2 (lines 265–267: DIPs, scale factor, work area), §13.4 (lines 285–287: physical-pixel coordinates, DIP readout/threshold, display-change cancel), §13.5 (line 294: native pixels); AGENTS.md "Windows app › Architecture rules (Windows)" (coordinates)
> Depends on: 0025-windows-solution-tray-shell

## Context

`windows/` is created by 0025 (solution, `Snapper.Core`, `Snapper.Core.Tests`, `Snapper.Windows`
with a Per-Monitor V2 `app.manifest`, `NativeMethods.txt`, `App/AppCoordinator`). Nothing in it
knows about monitors yet. Every capture spec after this one needs the same math: the selection
overlay (0030), screenshots (0031), window mode (0032), recording and its pill/border
(0034–0036), and the annotation window sizing (0040, which uses the work area).

**macOS reference:** spec `docs/specs/done/0004-display-geometry_COMPLETED.md` and
- `Snapper/Capture/DisplayGeometry.swift:4-142`: `DisplayInfo`, `PixelSize`, `CaptureRegion`, and
  `DisplayGeometry` (overlap `:35-44`, primary `:46-48`, Y flips `:50-69`, half-open/closed lookup
  `:71-82`, clamp/normalize/drag `:84-103`, display-local rect `:106-110`, outward pixel alignment
  with ε `:112-130`, pixel size `:132-134`, capture region `:136-141`);
- `Snapper/Capture/WindowHitTester.swift:43-59`: `WindowDisplayRule` (largest overlap, then
  nearest by the frame centre's squared distance, ties stable by list order);
- `Snapper/Capture/RectSelectionTracker.swift:18-30`: nearest display for a mouse-down outside
  every display (lookup, else smallest squared distance to the clamped point);
- `Snapper/Capture/DisplayConfigurationChange.swift:1-5`: order-independent set comparison;
- `Snapper/Capture/DisplayInfo+NSScreen.swift:1-15`: the main-actor platform adapter;
- consumers that react to display changes: `Snapper/Capture/SelectionOverlayController.swift:41-42,124-126`,
  `Snapper/Recording/RecordingSession.swift:196-205`,
  `Snapper/Recording/RecordingOverlayController.swift:49-56,83-92`;
- tests: `SnapperTests/DisplayGeometryTests.swift:1-299`, `SnapperTests/WindowDisplayRuleTests.swift:1-44`,
  `SnapperTests/DisplayConfigurationChangeTests.swift:1-34`.

**What changes on Windows (PRD §13.2, §13.4):**
- Global coordinates are **physical pixels** in virtual-screen coordinates, top-left origin, y down.
  Monitor bounds, work areas, cursor positions (`GetCursorPos`), window rects, and
  `DWMWA_EXTENDED_FRAME_BOUNDS` all use this space in a Per-Monitor V2 process.
- **There is no Y flip.** The macOS `primaryHeight`, `cgPoint(fromAppKit:)`, `appKitPoint(fromCG:)`,
  `cgRect(fromAppKit:)`, `appKitRect(fromCG:)` and `displayLocalTopLeftRect` flip are **not ported**.
  `WindowDisplayRule` takes no primary height.
- The error-prone part is instead **per-monitor DPI**: a monitor has a DPI (96 = 100%, 120 = 125%,
  144 = 150%, 168 = 175%, 192 = 200%), and DIPs = physical pixels × 96 ÷ DPI (PRD §13.2 line 265).
  DIPs exist only inside a window (WPF mouse positions and layout) and are always relative to that
  window's monitor. Two adjacent monitors with different DPIs touch in physical pixels, but their
  DIP spaces are unrelated, so **DIPs must never be compared or added across monitors**.
- A capture's "native pixel resolution" is the monitor's physical pixels (PRD §13.5 line 294).
  Since global coordinates already are physical pixels, the macOS `backingScale` multiplication of
  the output size and `CaptureRegion.sourceRect` disappear: a capture rect is a whole-pixel
  `PixelRect`, and its pixel size is its width and height.
- The visible frame becomes the monitor's **work area** (PRD §13.2 line 267). It is part of
  `DisplayInfo` (0035 pill placement and 0040 window sizing use it), but it does not count as a
  configuration change (see Scope §5).

AGENTS.md (Windows) requires that `DisplayGeometry` (Core) handle every pixel↔DIP conversion,
monitor lookup, clamping, and pixel alignment, and that mixed-DPI and negative-origin cases be
tested. **After this spec, no Windows feature code converts between DIPs and pixels or looks up a
monitor inline; it calls `DisplayGeometry`.**

## User story

As a Snapper user on Windows with one or more monitors at different scales (100%, 125%, 150%,
200%), arranged left of, right of, or above the primary monitor, the area I drag is exactly the area
that gets captured, at the monitor's native pixels, from the monitor where I started dragging, and
plugging in a monitor or changing its scale while I'm selecting cancels the selection instead of
capturing the wrong area.

## Scope & changes

All Core files use namespace `Snapper.Core.Geometry`, reference no WPF/WinForms/WinRT/interop
types, and are pure (static functions or immutable values, no I/O). Rounding uses
`Math.Round(v, MidpointRounding.AwayFromZero)` unless stated otherwise. `ε = 1e-6` (in pixel units).

### 1. Pure: geometry primitives — `windows/src/Snapper.Core/Geometry/` (one file per type)

- `PointD.cs`: `public readonly record struct PointD(double X, double Y)`.
- `SizeD.cs`: `public readonly record struct SizeD(double Width, double Height)`.
- `RectD.cs`: `public readonly record struct RectD(double X, double Y, double Width, double Height)`
  with `Left => X`, `Top => Y`, `Right => X + Width`, `Bottom => Y + Height`,
  `Center => new PointD(X + Width / 2, Y + Height / 2)`, `Size => new SizeD(Width, Height)`,
  `Standardized()` (negative width/height flipped so the size is non-negative; same area),
  `static RectD FromEdges(double left, double top, double right, double bottom)` (width = right −
  left, height = bottom − top; no normalization), and `static RectD FromPixelRect(PixelRect r)`.
- `PixelPoint.cs`: `public readonly record struct PixelPoint(int X, int Y)`.
- `PixelSize.cs`: `public readonly record struct PixelSize(int Width, int Height)`.
- `PixelRect.cs`: `public readonly record struct PixelRect(int X, int Y, int Width, int Height)`
  with `Left`, `Top`, `Right => X + Width` and `Bottom => Y + Height` (**exclusive**, like Win32
  `RECT`), `Size => new PixelSize(Width, Height)`, `IsEmpty => Width <= 0 || Height <= 0`, and
  `static PixelRect FromEdges(int left, int top, int right, int bottom)` that normalizes swapped
  edges (min/max) so the size is never negative.

These are the primitives AGENTS.md names; later specs (0030+, 0038) use them instead of WPF
`Point`/`Rect`. `Snapper.Windows` converts at the boundary (no extension methods on WPF types in
Core).

### 2. Pure: `DisplayInfo` — `windows/src/Snapper.Core/Geometry/DisplayInfo.cs`

```csharp
public sealed record DisplayInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)
```
- `DeviceName`: the monitor's GDI device name from `MONITORINFOEXW.szDevice` (for example
  `\\.\DISPLAY1`). It is the stable identity (the macOS `displayID`); `HMONITOR` values are not
  stored in Core (they are interop handles and can be reused).
- `Bounds`: the monitor rect (`rcMonitor`), physical pixels, virtual-screen coordinates.
- `WorkArea`: `rcWork`, same space (excludes the taskbar).
- `Dpi`: effective DPI (`GetDpiForMonitor(MDT_EFFECTIVE_DPI)` x value). The constructor throws
  `ArgumentOutOfRangeException` when `Dpi <= 0`, and `ArgumentNullException` for a null name.
- `IsPrimary`: `MONITORINFOF_PRIMARY`. The primary monitor's `Bounds` origin is (0, 0).
- Computed: `double Scale => Dpi / 96.0`; `SizeD DipSize` = `Bounds` size converted with
  `DisplayGeometry.PixelsToDips` (a 2880 × 1620 monitor at 144 DPI → 1920 × 1080 DIPs).
- Record value equality covers all five members (used by tests and by `HashSet`).

### 3. Pure: `DisplayGeometry` — `windows/src/Snapper.Core/Geometry/DisplayGeometry.cs`

`public static class DisplayGeometry` with `public const int BaseDpi = 96;`. All "global" values are
physical virtual-screen pixels; all "local DIP" values are relative to the given monitor's
`Bounds` origin.

**DIP ↔ pixel conversion.** Formulas multiply first and divide second, so integer-friendly inputs
are exact (`150 * 96 / 144 == 100` exactly):
- `double PixelsToDips(double pixels, int dpi)` = `pixels * 96.0 / dpi`.
- `double DipsToPixels(double dips, int dpi)` = `dips * dpi / 96.0`.
- `PointD ToLocalDips(PointD global, DisplayInfo d)` =
  `((global.X − d.Bounds.X) * 96.0 / d.Dpi, (global.Y − d.Bounds.Y) * 96.0 / d.Dpi)`.
  The origin is subtracted **before** scaling, so negative-origin monitors work and the result does
  not depend on any other monitor.
- `PointD ToGlobalPixels(PointD localDips, DisplayInfo d)` = `(d.Bounds.X + x * d.Dpi / 96.0, d.Bounds.Y + y * d.Dpi / 96.0)`
  (exact doubles, no rounding). Used to turn a WPF mouse position on an overlay into a global
  pixel position.
- `RectD ToLocalDips(RectD global, DisplayInfo d)` and `RectD ToGlobalPixels(RectD localDips, DisplayInfo d)`:
  standardize, convert the left/top and right/bottom corners with the point functions, and build
  the result with `RectD.FromEdges` (so width = converted right − converted left).
- `SizeD DipSize(RectD global, DisplayInfo d)` = the standardized width and height converted with
  `PixelsToDips(…, d.Dpi)`. This is what the W × H readout (0030) shows and what the 4 × 4 DIP click
  threshold (0030) compares, always with the **start** monitor's DPI (PRD §13.4 line 286).
- `PixelRect RoundToGlobalPixels(RectD localDips, DisplayInfo d)`: for placing Snapper's own
  windows with `SetWindowPos` (pill, border, annotation window). Standardize; convert each **edge**
  to local pixels (`edge * d.Dpi / 96.0`); round each edge to the nearest integer with
  `MidpointRounding.AwayFromZero` (rounding is done on the **local**, non-negative value, then the
  integer `Bounds` origin is added, so negative-origin monitors round the same way as the primary);
  return `PixelRect.FromEdges`. Rounding edges (not sizes) keeps adjacent DIP rects tiled with no
  gaps or overlaps.

**Primary and lookup.**
- `DisplayInfo? PrimaryDisplay(IReadOnlyList<DisplayInfo> displays)`: the first with
  `IsPrimary`; else the first whose `Bounds` origin is (0, 0); else the first; `null` when empty.
- `DisplayInfo? DisplayContaining(PointD p, IReadOnlyList<DisplayInfo> displays)` (port of
  `DisplayGeometry.swift:71-82`):
  1. **Half-open first:** `Left <= x < Right && Top <= y < Bottom` (matches Win32 `RECT`, where
     `Right`/`Bottom` are exclusive). Monitors don't overlap, so at most one matches. A point on a
     shared vertical edge belongs to the monitor on the **right**; on a shared horizontal edge, to
     the monitor **below** (the one whose `Top` equals that y). (macOS: "above", because AppKit's y
     points up; on Windows it is the monitor whose top/left edge the point is on.)
  2. **Closed fallback:** the first monitor in list order whose closed rect
     (`Left <= x <= Right && Top <= y <= Bottom`) contains `p` (outer right/bottom edges of the
     desktop, reachable by fractional DIP→pixel conversions).
  3. Otherwise `null` (off the desktop, or in an area no monitor covers).
- `DisplayInfo? NearestDisplay(PointD p, IReadOnlyList<DisplayInfo> displays)` (port of
  `RectSelectionTracker.swift:18-30`; same semantics as `MonitorFromPoint(MONITOR_DEFAULTTONEAREST)`):
  `DisplayContaining(p)`, else the monitor with the smallest `SquaredDistance(p, Bounds)`, ties
  going to the earlier monitor in list order; `null` only when the list is empty.
- `double SquaredDistance(PointD p, PixelRect bounds)`: squared distance from `p` to
  `Clamp(p, bounds)`; 0 inside or on the closed rect.
- `DisplayInfo? DisplayOverlapping(RectD r, IReadOnlyList<DisplayInfo> displays)` (port of
  `DisplayGeometry.swift:35-44`): standardize `r`; the monitor with the **largest positive
  intersection area in physical pixels²**, ties going to the earlier monitor; `null` if no monitor
  has a positive overlap. Area is computed in pixels, **never in DIPs** (a 500 × 500 px overlap on a
  150% monitor beats a 499 × 500 px overlap on a 100% monitor even though it has fewer DIPs).

**Clamping and drag rectangles** (global pixels; points are pixel-edge coordinates, not pixel
centres):
- `PointD Clamp(PointD p, PixelRect bounds)`: each coordinate clamped to the **closed** range
  `[Left, Right]` / `[Top, Bottom]`, so a selection that reaches the right/bottom edge includes the
  last column/row.
- `RectD Clamp(RectD r, PixelRect bounds)`: edge by edge (standardize; clamp left/right into
  `[Left, Right]` and top/bottom into `[Top, Bottom]`; width = right − left). A disjoint rect
  collapses to a zero-size rect on the nearest edge or corner; never NaN or negative size.
- `RectD NormalizedRect(PointD a, PointD b)`: `(min x, min y, |Δx|, |Δy|)`; equal points give a
  zero-size rect.
- `RectD DragRect(PointD start, PointD current, DisplayInfo d)` =
  `NormalizedRect(Clamp(start, d.Bounds), Clamp(current, d.Bounds))`. This is the PRD §5 line 71
  rule: a rectangle stays on the monitor where the drag started, also when the cursor crosses onto a
  monitor with a different DPI.

**Pixel alignment and capture rects** (port of `DisplayGeometry.swift:112-141`, simplified because
global coordinates are already pixels):
- `PixelRect PixelAligned(RectD global, DisplayInfo d)`: standardize; work in **local** pixels
  (`edge − d.Bounds.X/Y`) so the rounding never depends on the sign of the global coordinate;
  `left' = floor(localLeft + ε)`, `right' = ceil(localRight − ε)`, same for top/bottom (**round
  outward**, tolerating floating-point noise from DIP→pixel conversion such as `12.999999999`). A
  dimension of size `<= ε` stays 0 and its edge snaps to the **nearest** pixel
  (`Math.Round(local, MidpointRounding.AwayFromZero)`). Add the origin back, clamp the edges to the
  closed `Bounds`, and return a `PixelRect`.
- `PixelRect CaptureRect(RectD selection, DisplayInfo d)` = `PixelAligned(Clamp(selection, d.Bounds), d)`.
  Its `Size` is the output image's pixel size (the macOS `pixelSize(of:scale:)` is not needed).
- `PixelRect ToMonitorLocal(PixelRect global, DisplayInfo d)` = the rect offset by
  `−d.Bounds.X, −d.Bounds.Y`. No clamping. 0031's `CaptureCrop` uses it to crop a monitor frame.

Not in this file: the 4 × 4 DIP rule, the readout text and its placement (0030), pill placement
(0035), crop math against the captured texture size (0031 `CaptureCrop`), window hit-testing
(0032), canvas transforms and annotation window sizing (0040).

### 4. Pure: `WindowDisplayRule` — `windows/src/Snapper.Core/Geometry/WindowDisplayRule.cs`

`public static class WindowDisplayRule` with
`DisplayInfo? Display(RectD windowFrame, IReadOnlyList<DisplayInfo> displays)` (port of
`WindowHitTester.swift:43-59` without the primary-height parameter): `DisplayOverlapping(windowFrame)`;
if that is `null`, the monitor with the smallest `SquaredDistance(windowFrame.Standardized().Center, Bounds)`,
ties going to the earlier monitor; `null` when the list is empty. `windowFrame` is the window's
`DWMWA_EXTENDED_FRAME_BOUNDS` in physical pixels (0032/0036). The chosen monitor supplies the DPI
and work area for the window's pill and annotation window.

### 5. Pure: `DisplayConfigurationChange` — `windows/src/Snapper.Core/Geometry/DisplayConfigurationChange.cs`

`public static class DisplayConfigurationChange` with
`bool HasChanged(IReadOnlyList<DisplayInfo> old, IReadOnlyList<DisplayInfo> current)` (port of
`DisplayConfigurationChange.swift:1-5`): compares the two lists **as sets, ignoring order**, by the
key `(DeviceName, Bounds, Dpi, IsPrimary)`. `WorkArea` is **excluded**: moving, resizing, or
auto-hiding the taskbar is not a connect/disconnect, arrangement, resolution, or DPI change (PRD
§13.4 line 287), so it must not cancel a selection. Consumers (0030 overlay cancel, 0034/0036
recording) decide what a change means for them.

### 6. Platform glue: `DisplayInfoProvider` — `windows/src/Snapper.Windows/Capture/DisplayInfoProvider.cs`

`internal static class DisplayInfoProvider` (namespace `Snapper.Windows.Capture`), called on the
WPF dispatcher thread (which runs Per-Monitor V2 per the 0025 manifest, so the APIs return physical
pixels and real per-monitor DPIs):
- `IReadOnlyList<DisplayInfo> GetCurrent()`: `EnumDisplayMonitors(default, null, callback, 0)`; for
  each `HMONITOR`, `GetMonitorInfo` with a `MONITORINFOEXW` (`cbSize = sizeof(MONITORINFOEXW)`,
  passed as `MONITORINFO*`) for `szDevice`, `rcMonitor`, `rcWork`, `dwFlags`; then
  `GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out dpiX, out _)`. Order: the primary first, then the
  rest in enumeration order. A monitor whose `GetMonitorInfo` fails is skipped; if
  `GetDpiForMonitor` fails, use 96 and `Debug.WriteLine` the HRESULT. A duplicate device name keeps
  the first entry. No logic beyond this mapping (RECT → `PixelRect.FromEdges`).
- `bool TryGetMonitorHandle(DisplayInfo display, out HMONITOR monitor)`: re-enumerates and returns
  the handle whose device name **and** bounds equal `display`'s; `false` if the monitor is gone or
  changed. 0031/0034 use it for `GraphicsCaptureItem` creation (`CreateForMonitor`); `HMONITOR`
  never enters Core.
- `NativeMethods.txt` additions: `EnumDisplayMonitors`, `GetMonitorInfo`, `MONITORINFOEXW`,
  `MONITORINFOF_PRIMARY`, `GetDpiForMonitor`, `MONITOR_DPI_TYPE`, `WM_DISPLAYCHANGE`,
  `WM_DPICHANGED`, `WM_SETTINGCHANGE`. Keep the `MONITORENUMPROC` delegate alive for the
  (synchronous) duration of the call.

### 7. Platform glue: `DisplayChangeWatcher` — `windows/src/Snapper.Windows/Capture/DisplayChangeWatcher.cs`

`internal sealed class DisplayChangeWatcher : IDisposable` (the Windows counterpart of
`NSApplication.didChangeScreenParametersNotification` + `DisplayInfo.allCurrent()`):
- `DisplayChangeWatcher(Dispatcher dispatcher)`: captures `Current = DisplayInfoProvider.GetCurrent()`.
- `IReadOnlyList<DisplayInfo> Current { get; }`: the last enumerated configuration.
- `event EventHandler<DisplayConfigurationChangedEventArgs>? Changed`, where
  `DisplayConfigurationChangedEventArgs` (same file, `sealed class : EventArgs`) has
  `IReadOnlyList<DisplayInfo> Old` and `New`. Always raised on the dispatcher thread.
- **Triggers**, each of which only *schedules* a check:
  - a hidden **top-level** window created with `HwndSource` (`WS_POPUP`, extended
    `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, 0 × 0, never shown). It must be top-level, not
    `HWND_MESSAGE`, because message-only windows don't receive broadcasts. Its hook schedules a check
    on `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `WM_SETTINGCHANGE` and returns `handled = false`;
  - `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` (part of the Windows Desktop framework
    that the WPF/WinForms project already references; **no package**). It can fire on another
    thread, so it only schedules;
  - `public void Recheck()`: for Snapper windows that see a per-monitor `WM_DPICHANGED` (the hidden
    window only gets one for its own monitor). 0030's overlays and 0035's pill call it from their
    `WM_DPICHANGED` handling.
- **Check:** scheduling posts one `Dispatcher.BeginInvoke(DispatcherPriority.Background, …)` and
  coalesces (a burst of messages produces one check while one is pending), plus one follow-up check
  300 ms later via a `DispatcherTimer`, because Windows can deliver the message before the new
  topology and DPIs are readable. A check calls `GetCurrent()`; if
  `DisplayConfigurationChange.HasChanged(Current, fresh)` it updates `Current` **first**, then
  raises `Changed(old, fresh)`; otherwise it only updates `Current` silently (so work-area-only
  changes refresh `WorkArea` without an event). In Debug builds each raised change writes one
  `Debug.WriteLine("Displays changed: …")` line listing every monitor (`DeviceName Bounds Dpi
  primary`), for manual verification.
- `Dispose()`: unsubscribes from `SystemEvents` (a static event; leaking it keeps the watcher
  alive), stops the timer, and disposes the `HwndSource`. Idempotent.
- **Wiring:** `App/AppCoordinator` (0025) creates one watcher at startup on the dispatcher thread,
  exposes it as `internal DisplayChangeWatcher Displays { get; }` for later specs, and disposes it on
  Exit. No feature subscribes yet (0030 is the first consumer).

### 8. Project / docs

- New Core files are picked up by SDK globbing; add the test files to `windows/tests/Snapper.Core.Tests/`.
- No PRD update: this spec adds no product behavior. No AGENTS.md change (its Windows coordinate
  rule already names `DisplayGeometry`).

## Acceptance criteria

- [ ] `PointD`, `SizeD`, `RectD`, `PixelPoint`, `PixelSize`, `PixelRect`, `DisplayInfo`,
      `DisplayGeometry`, `WindowDisplayRule`, and `DisplayConfigurationChange` exist in
      `windows/src/Snapper.Core/Geometry/`, in namespace `Snapper.Core.Geometry`, with no Windows,
      WPF, WinForms, WinRT, or interop references.
- [ ] Every function behaves exactly as specified: multiply-then-divide DIP conversion relative to
      the monitor origin, half-open then closed lookup, nearest-monitor fallback with stable ties,
      largest-overlap in physical pixels, closed-range clamping, outward alignment with ε and
      nearest-edge zero sizes, edge rounding (away from zero, on local values) for window placement.
- [ ] `DisplayInfoProvider.GetCurrent()` returns the primary first with physical-pixel bounds, work
      areas, and effective DPIs; `TryGetMonitorHandle` resolves a current monitor and fails for a
      removed one.
- [ ] `DisplayChangeWatcher` raises `Changed` once per real configuration change (connect,
      disconnect, arrangement, resolution, DPI, primary), on the dispatcher thread, and never for a
      taskbar-only work-area change.
- [ ] All new xUnit tests pass; `dotnet build windows/Snapper.slnx -c Debug` and
      `dotnet test windows/Snapper.slnx` are green; the app still runs as after 0025.

## Edge cases

- **Mixed-DPI adjacency:** a 100% monitor and a 150% monitor touch at x = 1920 in pixels. The
  point (1919.5, y) is on the left monitor and (1920, y) on the right; a drag that crosses the edge
  stays clamped to the start monitor; the readout and 4 × 4 threshold use the start monitor's DPI
  even while the cursor is over the other monitor.
- **Negative origins:** monitors left of (negative x) and above (negative y) the primary. All
  rounding happens on local (origin-relative) values, so `Math.Round`/`Floor`/`Ceiling` behave the
  same as on the primary.
- **Cursor at the last pixel:** the cursor can reach `Right − 1` but not `Right`, so a drag to the
  far right/bottom of a monitor ends one pixel short of the edge, as on macOS (the cursor position
  is used as an edge coordinate). Full-monitor captures are not a PRD feature, so no snapping is
  added.
- **Fractional DIPs at 125%/150%/175%:** WPF reports DIPs such as `100.8`; `ToGlobalPixels` gives
  near-integers (`125.99999999`); `PixelAligned`'s ε keeps them from growing by a pixel.
- **Zero-size and negative-size rects:** standardized; zero sizes stay zero through clamping and
  alignment; never NaN or negative.
- **Point in an uncovered area** (desktop is not a rectangle, e.g. above a monitor right of a
  smaller one): `DisplayContaining` is `null`, `NearestDisplay` picks the closest monitor.
- **Window off every monitor or fully minimized position** (e.g. −32000, −32000): no overlap, so
  `WindowDisplayRule` falls back to the nearest monitor. Minimized windows are filtered out earlier
  by 0032.
- **Window spanning three monitors:** largest pixel overlap wins; exact ties go to the earlier
  monitor in list order (the primary is first in provider order).
- **Configuration events without a real change** (theme change → `WM_SETTINGCHANGE`, taskbar
  auto-hide → work area): no `Changed` event.
- **Burst of messages** during a monitor plug-in: coalesced into one check, plus the 300 ms
  follow-up; at most one `Changed` per distinct new configuration.
- **No monitors** (transient during session switches/RDP): `GetCurrent()` returns an empty list;
  lookups return `null`; a change to/from empty is a change.
- **GetDpiForMonitor failure:** treated as 96 DPI (logged), never 0 (the constructor rejects 0).
- **Intermediate state:** no spec consumes the watcher or geometry until 0030; the app behaves as
  after 0025 except the Debug log lines.

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`). Doubles that aren't exactly
  representable use `Assert.Equal(expected, actual, 1e-9)`; all others use exact equality.
  Fixtures (Bounds as X, Y, W, H; work areas only matter for equality tests):
  - `P` = `\\.\DISPLAY1`, (0, 0, 1920, 1080), 96 DPI (100%), primary, work (0, 0, 1920, 1032).
  - `R` = `\\.\DISPLAY2`, (1920, 0, 2880, 1620), 144 DPI (150%), right of P (taller), work (1920, 0, 2880, 1548).
  - `L` = `\\.\DISPLAY3`, (−3840, −540, 3840, 2160), 192 DPI (200%), left of and above P (negative x and y).
  - `A` = `\\.\DISPLAY4`, (0, −1440, 2560, 1440), 120 DPI (125%), above P and partly above R (negative y).
  - Layout `all = [P, R, L, A]` (non-overlapping; checked by a test). Single-monitor fixtures `S96`,
    `S120`, `S144`, `S168`, `S192` at origin (0, 0), each 1920 × 1080 DIPs (1920 × 1080, 2400 × 1350,
    2880 × 1620, 3360 × 1890, 3840 × 2160 px).

  **`GeometryPrimitivesTests.cs`**
  1. `RectD` edges: (10, 20, 30, 40) → Right 40, Bottom 60, Center (25, 40).
  2. `RectD.Standardized`: (40, 60, −30, −40) → (10, 20, 30, 40); a standard rect is unchanged.
  3. `RectD.FromEdges(10, 20, 40, 60)` → (10, 20, 30, 40); `FromPixelRect((−5, 3, 7, 9))` → (−5, 3, 7, 9).
  4. `PixelRect` (−3840, −540, 3840, 2160) → Right 0, Bottom 1620 (exclusive), Size 3840 × 2160.
  5. `PixelRect.FromEdges(10, 20, 5, 8)` → (5, 8, 5, 12); `IsEmpty` true for width 0 or height 0.

  **`DisplayInfoTests.cs`**
  6. `Scale`: 96 → 1.0, 120 → 1.25, 144 → 1.5, 168 → 1.75, 192 → 2.0.
  7. `DipSize`: R → 1920 × 1080; A → 2048 × 1152; L → 1920 × 1080; each `S*` → 1920 × 1080.
  8. Constructor with `Dpi` 0 or −96 throws `ArgumentOutOfRangeException`; null name throws `ArgumentNullException`.
  9. Value equality: a copy of P equals P; `new HashSet<DisplayInfo> { P, copy, R }.Count == 2`.
  10. The `all` fixtures are pairwise non-overlapping (positive intersection area is 0 for every pair).

  **`DisplayGeometryTests.cs`** (port of `SnapperTests/DisplayGeometryTests.swift`, Y-flip cases
  dropped, DPI cases added)
  11. `DipsToPixels(4, dpi)` for 96/120/144/168/192 → 4, 5, 6, 7, 8 (the 4 × 4 threshold in
      pixels); `PixelsToDips(6, 144)` → 4; `PixelsToDips(301, 144)` → 200.666… (1e-9);
      `PixelsToDips(1, 120)` → 0.8 (1e-9).
  12. `ToLocalDips` maps the same visual spot on every monitor to (100, 200), exactly:
      P (100, 200); R (2070, 300); L (−3640, −140); A (125, −1190).
  13. `ToGlobalPixels((100, 200), d)` is the exact inverse of case 12 for P, R, L, A.
  14. Fractional: `ToLocalDips((1921, 1), R)` → (0.666…, 0.666…); `ToLocalDips((1, −1439), A)` → (0.8, 0.8) (1e-9).
  15. Round trip: for every monitor in `all` and the points `Bounds` top-left, (Right, Bottom),
      centre, and (Left + 0.5, Top + 0.25), `ToGlobalPixels(ToLocalDips(p, d), d) ≈ p` (1e-9).
  16. Rect conversion: `ToLocalDips((2070, 300, 300, 150), R)` → (100, 200, 200, 100);
      `ToLocalDips((−3640, −140, 400, 200), L)` → (100, 200, 200, 100); negative-size
      (2370, 450, −300, −150) on R → (100, 200, 200, 100); `ToGlobalPixels` inverts each.
  17. `DipSize`: (1800, 500, 120, 100) on P → 120 × 100; (1920, 100, 100, 200) on R → 66.666… ×
      133.333… (1e-9); (−200, −500, 200, 1400) on L → 100 × 700; (100, −100, 100, 100) on A → 80 × 80.
  18. `RoundToGlobalPixels`: A (10, 10, 101, 31) DIPs → edges 12.5→13, 138.75→139, 12.5→13,
      51.25→51 → (13, −1427, 126, 38); R (0, 0, 1920, 1080) → R.Bounds; L (0.3, 0.3, 10, 10) →
      (−3839, −539, 20, 20); P (10.5, 10.5, 1, 1) → (11, 11, 1, 1); S168 (0, 0, 1, 1) → (0, 0, 2, 2)
      and S168 (1, 0, 1, 1) → (2, 0, 2, 2) (adjacent DIP rects tile without gap or overlap).
  19. `PrimaryDisplay`: [R, P] → P; [R, P with `IsPrimary = false`] → that P (zero origin);
      [R, A] → R (first); [] → null.
  20. `DisplayContaining` on `all`: (0, 0) → P; (1919, 500) → P; (1919.5, 500) → P; (1920, 500) → R;
      (1920, 0) → R (corner shared by P, R, A); (−0.5, 10) → L; (−1, −1) → L; (−3840, −540) → L;
      (100, −1) → A; (0, −1) → A; (2000, −1) → A.
  21. Closed fallback: (4800, 500) → R; (100, 1080) → P; (4801, 500) → null; (100, 1081) → null;
      (2600, −1) → null (above R, right of A); `[]` → null.
  22. `NearestDisplay`: (2600, −1) → R (distance² 1 vs 1600 to A); (4801, 500) → R; (100, 1081) → P;
      (−1, −541) → L for `[P, R, L, A]` and → A for `[P, R, A, L]` (equal distance² 1: stable tie);
      a contained point returns `DisplayContaining`'s answer; `[]` → null.
  23. `SquaredDistance`: inside → 0; on the closed right edge (1920, 10) to P → 0; (1925, 1090) to P → 125.
  24. `NormalizedRect`: (10, 20)–(110, 70), (110, 70)–(10, 20), (110, 20)–(10, 70) all → (10, 20, 100, 50);
      (5, 5)–(5, 5) → (5, 5, 0, 0).
  25. `Clamp(point)` to P: (2000, 1100) → (1920, 1080); (−5, −5) → (0, 0); (100, 100) unchanged.
  26. `Clamp(rect)` to P: (1800, 1000, 300, 300) → (1800, 1000, 120, 80); (3000, 3000, 10, 10) →
      (1920, 1080, 0, 0); (1920, 0, 10, 10) → (1920, 0, 0, 10); (100, 100, 0, 0) unchanged;
      (2100, 1300, −300, −300) → (1800, 1000, 120, 80). To L: (−4000, −600, 300, 100) → (−3840, −540, 140, 40).
  27. `DragRect` across mixed-DPI monitors: start (1800, 500) on P, current (2500, 600) over R →
      (1800, 500, 120, 100); start (2020, 100) on R, current (1900, 300) over P → (1920, 100, 100, 200).
  28. `DragRect` from negative-origin monitors: L start (−200, −500), current (300, 900) over P →
      (−200, −500, 200, 1400); A start (100, −100), current (200, 300) over P → (100, −100, 100, 100);
      A start (2000, −50), current (3000, −60) (uncovered area) → (2000, −60, 560, 10).
  29. `DragRect` edge cases: P start (100, 100), current (−5000, −5000) → (0, 0, 100, 100);
      start == current → zero-size rect at that point; P start (−5, −5), current (100, 100) → (0, 0, 100, 100).
  30. `PixelAligned` at 100%: P (10.25, 20.75, 100.5, 50) → (10, 20, 101, 51); (10, 20, 100, 50) unchanged.
  31. `PixelAligned`, negative origin: L (−3839.6, −539.4, 50, 50) → (−3840, −540, 51, 51).
  32. `PixelAligned`, float noise: R (1920 + 1e−10, 0, 100, 50) and (1920 − 1e−10, 0, 100, 50) →
      (1920, 0, 100, 50).
  33. `PixelAligned` of a 125% DIP drag: `ToGlobalPixels` of A-local DIP points (10.4, 20.8) and
      (90.4, 60.8), `NormalizedRect`, then `PixelAligned` on A → (13, −1414, 100, 50).
  34. `PixelAligned`, zero size: P (10.25, 10.6, 0, 0) → (10, 11, 0, 0); P (10.25, 10.25, 0, 1) → (10, 10, 0, 2).
  35. `PixelAligned` stays on the monitor: P (1919.8, 1079.8, 0.4, 0.4) → (1919, 1079, 1, 1);
      P (2000, 2000, 0, 0) → (1920, 1080, 0, 0).
  36. `CaptureRect`, same DIP size on different DPIs: a 200 × 100 DIP selection at local DIP
      (100, 100) on P → (100, 100, 200, 100), size 200 × 100; on R (via `ToGlobalPixels`) →
      (2070, 150, 300, 150), size 300 × 150; on L → (−3640, −340, 400, 200), size 400 × 200; on A →
      (125, −1315, 250, 125), size 250 × 125.
  37. `CaptureRect`, clamped and standardized: P (1800, 1000, 300, 300) → (1800, 1000, 120, 80);
      A (1000, −1000, −500, −300) → (500, −1300, 500, 300); P (5000, 5000, 10, 10) → (1920, 1080, 0, 0).
  38. `CaptureRect` of each monitor's `Bounds` == `Bounds` (R 2880 × 1620, L 3840 × 2160, A 2560 × 1440:
      native pixels) and `ToMonitorLocal` of it == (0, 0, W, H).
  39. `ToMonitorLocal`: (−3640, −140, 400, 200) on L → (200, 400, 400, 200); (2070, 150, 300, 150) on
      R → (150, 150, 300, 150); (125, −1315, 250, 125) on A → (125, 125, 250, 125); out of bounds
      (−10, 0, 20, 20) on P → (−10, 0, 20, 20) (no clamping).
  40. `DisplayOverlapping` on `all`: (2000, 100, 800, 600) → R; (1500, 100, 1000, 500) → R
      (580 vs 420 px wide); (1000, 100, 1000, 500) → P; tie (1420, 100, 1000, 500) → P for `all` and
      → R for `[R, P, L, A]`; (5000, 5000, 10, 10) → null; zero-size (100, 100, 0, 0) → null.

  **`WindowDisplayRuleTests.cs`** (port of `SnapperTests/WindowDisplayRuleTests.swift`)
  41. Fully on R (2000, 100, 800, 600) → R; fully on L (−3000, 0, 400, 300) → L.
  42. Spanning P/R: (1500, 100, 1000, 500) → R; (1000, 100, 1000, 500) → P.
  43. Pixel area, not DIP area: (1421, 100, 1000, 500) (P 499 px wide, R 501 px wide) → R, even
      though R's part is only 334 DIPs wide.
  44. Negative origins: (−600, −300, 1000, 200) → L (120 000 vs 80 000 px² on A);
      (−400, −300, 1000, 200) → A.
  45. Three monitors: (−100, −100, 400, 400) → P (90 000 vs L 40 000 vs A 30 000).
  46. Exact tie (1420, 100, 1000, 500) → P for `all`, R for `[R, P, L, A]`.
  47. No intersection: (2600, −300, 100, 100) → A (centre distance² 8 100 vs 62 500 to R);
      (5000, 500, 100, 100) → R; (−32000, −32000, 160, 28) → L (nearest to the minimized-window
      position); zero-size (100, 100, 0, 0) → P; `[]` → null.
  48. Distance tie is stable: with `[P, P2]` where `P2` has P's bounds and another name, window
      (3000, 100, 100, 100) → P; with `[P2, P]` → P2.

  **`DisplayConfigurationChangeTests.cs`** (port of `SnapperTests/DisplayConfigurationChangeTests.swift`)
  49. Unchanged `[P, R]` vs `[P, R]` → false; reordered `[R, P]` → false; `[]` vs `[]` → false.
  50. Connect `[P]` → `[P, R]`, disconnect `[P, R]` → `[P]`, `[P]` → `[]`, `[]` → `[P]` → true.
  51. Replacement: R replaced by a monitor with R's bounds/DPI and name `\\.\DISPLAY5` → true.
  52. Arrangement: R moved to (1920, 100) → true; L moved to (−3840, −500) → true.
  53. Resolution: R bounds (1920, 0, 1920, 1080) at 144 DPI → true.
  54. DPI only: R at 120 DPI with the same bounds → true; P at 144 DPI → true.
  55. Primary swap: P with `IsPrimary = false` and R with `IsPrimary = true` (same bounds) → true.
  56. Work area only: P with work (0, 0, 1920, 1080) (taskbar auto-hidden) and R with work
      (1920, 48, 2880, 1572) (taskbar moved to the top) → **false**.

- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  succeed with the whole suite green (both run on macOS).
- **Manual verification** (Windows 11 machine; these paths can't be unit-tested). Run a Debug build
  from Visual Studio or with Sysinternals DebugView capturing `Debug.WriteLine` output:
  1. With two monitors at different scales (e.g. 100% and 150%), launch Snapper. Expected: the app
     starts with its tray icon as after 0025; no "Displays changed" line at startup.
  2. Settings › System › Display: change the second monitor's scale (150% → 125%). Expected: exactly
     one "Displays changed" line whose list shows that monitor's new DPI (120) and unchanged bounds.
  3. Drag the second monitor to the other side of the primary in the arrangement and Apply. Expected:
     one line showing the new bounds; a monitor left of the primary has a negative X; one placed
     above has a negative Y.
  4. Change the second monitor's resolution. Expected: one line with the new bounds size.
  5. Disconnect the second monitor, then reconnect it. Expected: one line each time, listing one and
     then two monitors; the primary is listed first both times.
  6. Right-click the taskbar › Taskbar settings › turn on "Automatically hide the taskbar", then turn
     it off. Expected: **no** "Displays changed" line.
  7. Change the Windows theme (light/dark). Expected: no "Displays changed" line.
  8. Choose Exit Snapper. Expected: the process exits cleanly (no exception from the disposed
     watcher in the Output window).

## Out of scope

- Selection overlays, the rectangle tracker, the 4 × 4 DIP threshold, the readout text, and
  cancelling the overlay on `Changed`: 0030 (first consumer of the watcher, and caller of `Recheck`
  from overlay `WM_DPICHANGED`).
- Creating the `GraphicsCaptureItem` and cropping the captured texture (`CaptureCrop`): 0031.
- Window candidates, hit-testing, extended frame bounds: 0032.
- Stopping a recording when its monitor goes away (`RecordingLifecycle`): 0034; pill placement in
  the work area and pill `WM_DPICHANGED`: 0035; window tracking across monitors: 0036.
- Annotation canvas transforms and window sizing (80% of the work area): 0040.
- The macOS Y-flip helpers, `primaryHeight`, `displayLocalTopLeftRect`, and `pixelSize(of:scale:)`:
  not needed on Windows (no Y flip; global coordinates are already pixels).
- Selections spanning several monitors: a non-goal (docs/PRD.md §2 line 36).
