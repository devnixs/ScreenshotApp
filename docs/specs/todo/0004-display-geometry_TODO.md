# 0004 — Add pure display geometry helpers for coordinate, display and pixel math

> Source: docs/PRD.md §5 (lines 62–77), §6 (lines 79–87), §12 multi-display criterion (line 196); AGENTS.md "Architecture rules" (coordinates)
> Depends on: 0001-xcodegen-app-shell

## Context

No app code exists yet beyond what 0001 creates (project shell, status item, `AppCoordinator`).
Every capture feature after this one needs the same coordinate math:

- AppKit (`NSScreen.frame`, `NSEvent.mouseLocation`, window frames) uses **global coordinates with
  a bottom-left origin**, anchored at the bottom-left of the primary display (the screen whose
  `frame.origin == .zero`, i.e. the one with the menu bar). Y grows **up**.
- CoreGraphics (`CGWindowList…` info, `SCWindow.frame`, `SCDisplay.frame`, `CGDisplayBounds`) uses
  **global coordinates with a top-left origin**, anchored at the top-left of the same primary
  display. Y grows **down**.
- ScreenCaptureKit's `SCStreamConfiguration.sourceRect` (used by both `SCScreenshotManager` in 0008
  and `SCStream` in 0011) is a rect in **points, local to the captured display, top-left origin**.
- Output size (`SCStreamConfiguration.width/height`) is in **pixels**: PRD §6 requires "PNG at
  native pixel resolution (2× on Retina)" (docs/PRD.md:83).

PRD §5 says "A rectangle is confined to the display where the drag started" (docs/PRD.md:68), and
§12 requires "With multiple displays, the overlay covers all of them and the capture comes from the
display where the drag started" (docs/PRD.md:196). §2 lists "Selections that span more than one
display" as a non-goal (docs/PRD.md:34).

AGENTS.md calls the AppKit ↔ CG/SCK conversion "the most common source of bugs" and requires it to
be covered by tests. This spec builds that math once, as a pure, heavily tested type, so 0007
(overlay), 0008 (screenshot), 0009 (window mode), 0011–0013 (recording, pill, window tracking) all
call one implementation instead of re-deriving flips inline. **No feature code may flip Y by hand
after this spec; it goes through `DisplayGeometry`.**

## User story

As a Snapper user with one or more displays (Retina or not, arranged left/right/above/below the
main one), the area I select is exactly the area that gets captured, at full pixel resolution, from
the display where I started dragging, with crisp (pixel-aligned) edges.

## Scope & changes

### Pure logic — `Snapper/Capture/DisplayGeometry.swift` (imports `CoreGraphics` / `Foundation` only, no AppKit)

All types are value types and `Sendable`. All functions are `static`, non-isolated, and side-effect
free. Rects are standard (non-negative width/height) unless stated otherwise.

1. **`struct DisplayInfo: Sendable, Equatable, Hashable`**
   - `displayID: CGDirectDisplayID`
   - `frame: CGRect` — the display's frame in **AppKit global points** (bottom-left origin), exactly
     as `NSScreen.frame` reports it.
   - `backingScale: CGFloat` — `NSScreen.backingScaleFactor` (1 or 2 in practice; any value > 0
     must work).

2. **`struct PixelSize: Sendable, Equatable { let width: Int; let height: Int }`**

3. **`struct CaptureRegion: Sendable, Equatable`** — everything a capturer needs for one display:
   - `displayID: CGDirectDisplayID`
   - `appKitRect: CGRect` — the final (clamped, pixel-aligned) selection in AppKit global points
     (used by overlays/pill/border).
   - `sourceRect: CGRect` — the same area in **display-local, top-left-origin points** (assign to
     `SCStreamConfiguration.sourceRect`).
   - `pixelSize: PixelSize` — output size in pixels (assign to `SCStreamConfiguration.width/height`).

4. **`enum DisplayGeometry`** (namespace) with:

   **Primary display**
   - `static func primaryDisplay(in displays: [DisplayInfo]) -> DisplayInfo?` — the first display
     whose `frame.origin == .zero`; if none has a zero origin (should not happen), the first element;
     `nil` for an empty array.
   - `static func primaryHeight(in displays: [DisplayInfo]) -> CGFloat?` — `primaryDisplay(in:)?.frame.height`.

   **AppKit ↔ CoreGraphics global conversion** (both directions are the same reflection about
   `y = primaryHeight / 2`; provide both names for readability at call sites)
   - `static func cgPoint(fromAppKit p: CGPoint, primaryHeight h: CGFloat) -> CGPoint` →
     `(p.x, h - p.y)`.
   - `static func appKitPoint(fromCG p: CGPoint, primaryHeight h: CGFloat) -> CGPoint` →
     `(p.x, h - p.y)`.
   - `static func cgRect(fromAppKit r: CGRect, primaryHeight h: CGFloat) -> CGRect` →
     `CGRect(x: r.minX, y: h - r.maxY, width: r.width, height: r.height)` (uses the rect's
     *top* edge, `maxY`, not its origin).
   - `static func appKitRect(fromCG r: CGRect, primaryHeight h: CGFloat) -> CGRect` →
     `CGRect(x: r.minX, y: h - r.maxY, width: r.width, height: r.height)`.
   - Input rects are standardized first (`r.standardized`) so a negative-size rect cannot produce a
     wrong flip.

   **Display lookup**
   - `static func display(containing p: CGPoint, in displays: [DisplayInfo]) -> DisplayInfo?`
     (AppKit global point). Deterministic rule:
     1. **Half-open containment first:** a display contains `p` iff
        `frame.minX <= p.x < frame.maxX && frame.minY <= p.y < frame.maxY`. Displays don't
        overlap, so at most one matches. Consequence: a point exactly on a shared vertical edge
        belongs to the display on the **right** (the one whose `minX` equals that x); a point
        exactly on a shared horizontal edge belongs to the display **above** (the one whose `minY`
        equals that y). A shared corner follows both rules (the display whose min-corner it is).
     2. **Closed fallback:** if no display matched, return the **first display in array order**
        whose closed frame (`minX <= x <= maxX && minY <= y <= maxY`) contains `p`. This covers the
        outer `maxX`/`maxY` edges of the desktop (AppKit can report `NSEvent.mouseLocation.y ==
        frame.maxY` at the very top of a screen).
     3. Otherwise `nil` (point in a gap between displays or off the desktop).

   **Clamping and drag rectangles** (all AppKit global points)
   - `static func clamp(_ p: CGPoint, to frame: CGRect) -> CGPoint` — each coordinate clamped to the
     **closed** range `[min, max]` (so the result may lie on `maxX`/`maxY`; a selection reaching the
     display's right/top edge must include the last pixel column/row).
   - `static func clamp(_ r: CGRect, to frame: CGRect) -> CGRect` — defined edge-by-edge, **not** via
     `CGRect.intersection` (which returns `.null` for empty inputs): with `r = r.standardized`,
     `minX' = clamp(r.minX)`, `maxX' = clamp(r.maxX)` into the closed `[frame.minX, frame.maxX]`,
     same for Y; result `CGRect(x: minX', y: minY', width: maxX' − minX', height: maxY' − minY')`.
     A disjoint rect therefore collapses to a degenerate rect on the nearest frame edge/corner; a
     zero-size rect stays zero-size. Never returns `CGRect.null` or infinite values.
   - `static func normalizedRect(from a: CGPoint, to b: CGPoint) -> CGRect` —
     `CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: |a.x − b.x|, height: |a.y − b.y|)`. Equal
     points give a zero-size rect at that point. Order of `a`/`b` doesn't matter.
   - `static func dragRect(from start: CGPoint, to current: CGPoint, on display: DisplayInfo) -> CGRect`
     — `normalizedRect(from: clamp(start, to: display.frame), to: clamp(current, to: display.frame))`.
     This is the rule that confines a selection to the start display (PRD §5): when the drag leaves
     the display, the rect stops at its edge. 0007 calls this on every mouse-dragged event with the
     display of the overlay panel that received the mouse-down.

   **Display-local (ScreenCaptureKit) coordinates**
   - `static func displayLocalTopLeftRect(_ r: CGRect, on display: DisplayInfo) -> CGRect` —
     `CGRect(x: r.minX − display.frame.minX, y: display.frame.maxY − r.maxY, width: r.width, height: r.height)`.
     Independent of the primary display; this is what SCK `sourceRect` wants. Caller must clamp
     first (no clamping inside).

   **Points → pixels and pixel-grid alignment**
   - `static func pixelAligned(_ r: CGRect, on display: DisplayInfo) -> CGRect` — snaps each edge of
     `r` (AppKit global points) to the pixel grid of `display`, measured **relative to the display's
     origin** (`local = r − display.frame.origin`; result shifted back). Rule, with
     `s = display.backingScale` and `ε = 1e-6`:
     - `minX' = floor(local.minX * s + ε) / s`, `maxX' = ceil(local.maxX * s − ε) / s`, same for Y.
       That is, **round outward** to whole pixels, tolerating floating-point noise (so
       `100.0000001 * 2` does not grow by a pixel).
     - A dimension of size 0 (`width <= ε / s`, same for height) stays 0: its edge snaps to the
       **nearest** pixel boundary (`(local.minX * s).rounded(.toNearestOrAwayFromZero) / s`).
     - The result is then clamped to `display.frame` (`clamp(_:to:)`).
   - `static func pixelSize(of r: CGRect, scale: CGFloat) -> PixelSize` —
     `PixelSize(width: Int((r.width * scale).rounded()), height: Int((r.height * scale).rounded()))`.
     Intended for an already pixel-aligned rect, where the product is integral up to float noise;
     rounding to nearest only removes that noise.
   - **Why outward rounding:** the output must have no blurry or half-covered edge pixels. If an
     edge falls mid-pixel, SCK must either resample (blur) or drop a partially selected pixel.
     Snapping edges to whole pixels makes `sourceRect × scale` exactly equal to the output size, so
     SCK copies pixels 1:1. Outward (rather than nearest) never drops anything the user visibly
     included inside the selection; the cost is at most one extra pixel per edge, which is invisible
     at Retina density. On a 1× display AppKit mouse coordinates are already integral and on 2× they
     land on 0.5-point steps, so in practice alignment rarely changes anything — it is a safety net
     for fractional window frames (0009/0013) and scaled modes.

   **One-call convenience used by 0008/0011**
   - `static func captureRegion(for r: CGRect, on display: DisplayInfo) -> CaptureRegion` —
     `aligned = pixelAligned(clamp(r, to: display.frame), on: display)`; returns
     `CaptureRegion(displayID: display.displayID, appKitRect: aligned,
     sourceRect: displayLocalTopLeftRect(aligned, on: display),
     pixelSize: pixelSize(of: aligned, scale: display.backingScale))`.
     Uses only the display's own `backingScale`, so mixed 1×/2× setups are handled per display (a
     selection is never on two displays).

   Explicitly **not** in this file: the 4 × 4 pt minimum-drag rule and the W × H readout formatting
   (0007), pill placement (0012), window hit-testing (0009).

### Platform glue — `Snapper/Capture/DisplayInfo+NSScreen.swift` (AppKit adapter, untested)

- `extension DisplayInfo` with:
  - `@MainActor static func from(_ screen: NSScreen) -> DisplayInfo?` — reads
    `screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber` →
    `CGDirectDisplayID(truncating:)`; returns `nil` if missing. `frame = screen.frame`,
    `backingScale = screen.backingScaleFactor`.
  - `@MainActor static func allCurrent() -> [DisplayInfo]` — `NSScreen.screens.compactMap(from)`,
    preserving `NSScreen.screens` order (primary first).
- Keep this file to these few lines of glue; no logic. Matching a `DisplayInfo` to an `SCDisplay`
  is by `displayID` (done in 0008/0011), never by frame.

### Project

- No `project.yml` changes are expected if 0001 globs `Snapper/` and `SnapperTests/` as sources;
  run `xcodegen generate` after adding the files either way.
- No PRD update: this spec adds no product behavior.

## Acceptance criteria

- [ ] `DisplayGeometry`, `DisplayInfo`, `PixelSize`, `CaptureRegion` exist in
      `Snapper/Capture/DisplayGeometry.swift`, compile under Swift 6 strict concurrency, and import
      no AppKit.
- [ ] `DisplayInfo.from(_:)` / `allCurrent()` exist in `Snapper/Capture/DisplayInfo+NSScreen.swift`,
      `@MainActor`.
- [ ] All functions behave exactly as specified above, including the half-open/closed edge rules,
      outward pixel rounding with ε, and zero-size handling.
- [ ] `SnapperTests/DisplayGeometryTests.swift` covers every case in the test plan and passes.
- [ ] The app still builds, launches as before (0001 behavior unchanged), and the full test suite
      passes.

## Edge cases

- **Display left of the primary:** negative AppKit (and CG) x. Local x must subtract the negative
  origin (e.g. `-1900 − (−1920) = 20`).
- **Display below the primary:** negative AppKit y, positive CG y (`> primaryHeight`).
- **Display above the primary:** AppKit y ≥ primaryHeight, negative CG y.
- **Displays of different heights side by side:** a secondary taller than the primary extends above
  it; points above the primary's top but within the secondary's x-range must resolve to the
  secondary.
- **Mixed scales:** each computation uses only the start display's `backingScale`; never the main
  screen's or `NSScreen.main`'s.
- **Point exactly on a shared edge or corner:** resolved by the half-open rule (right / above wins).
- **Point on the desktop's outer max edge** (e.g. `y == primary.maxY` with nothing above): resolved by
  the closed fallback.
- **Point in a gap between displays or off-desktop:** `nil`; callers must handle it (0007 uses the
  panel's own display, so it never needs a lookup for mouse-down).
- **Drag ending outside the start display** (on another display, in a gap, or past the outer edge):
  clamped to the start display's closed frame.
- **Zero-size and negative-size rects:** standardized; zero sizes stay zero through clamping,
  alignment, and pixel size (`PixelSize(0, 0)`), and never become NaN, infinite, or `CGRect.null`.
- **Floating-point noise** (`100.0000001`): must not add a pixel (ε tolerance).
- **Scaled ("More Space") display modes:** pixel size uses `backingScaleFactor` (backing-store
  pixels), which is what ScreenCaptureKit and the macOS screenshot tool produce. This matches PRD §6
  "native pixel resolution (2× on Retina)".
- **Display with no `NSScreenNumber`** (should not happen): skipped by `allCurrent()`.

## Test plan

- **Unit tests** (`SnapperTests/DisplayGeometryTests.swift`, using the test framework set up by
  0001). Use exactly representable values so equality is exact (no accuracy needed except where
  noted). Fixture layouts (AppKit frames):
  - `single1x`: P = (0, 0, 1920, 1080) @1×.
  - `single2x`: P = (0, 0, 1512, 982) @2×.
  - `right`: P = (0, 0, 1440, 900) @2×; R = (1440, 0, 1920, 1080) @1× (taller than P).
  - `left`: P = (0, 0, 1440, 900) @2×; L = (−1920, 0, 1920, 1080) @1×.
  - `above`: P = (0, 0, 1440, 900) @2×; A = (−240, 900, 1920, 1080) @1×.
  - `below`: P = (0, 0, 1440, 900) @2×; B = (0, −1080, 1920, 1080) @1×.
  - `mixed`: P = (0, 0, 1512, 982) @2×; E = (1512, 0, 2560, 1440) @1×.

  Cases:
  1. **Primary detection:** `primaryDisplay(in: [R, P])` returns P (zero origin, not first);
     `primaryHeight(in: right)` = 900; empty array → `nil`; no zero-origin display → first element.
  2. **Point conversion:** h = 900: AppKit (100, 200) → CG (100, 700); AppKit (−500, −300) → CG
     (−500, 1200); CG → AppKit inverse of both.
  3. **Rect conversion, single 1×:** h = 1080: AppKit (100, 200, 300, 400) → CG (100, 480, 300, 400).
  4. **Rect conversion, displays as rects:** h = 900: `below` B → CG (0, 900, 1920, 1080); `above` A
     → CG (−240, −1080, 1920, 1080); `left` L → CG (−1920, −180, 1920, 1080); `right` R → CG
     (1440, −180, 1920, 1080).
  5. **Round-trip identity:** for every frame in every fixture and for sample rects (including a
     zero-size rect and a rect with negative origin), `appKitRect(fromCG: cgRect(fromAppKit: r, h), h) == r`;
     same for points.
  6. **Negative-size input:** `cgRect(fromAppKit: CGRect(x: 400, y: 600, width: −300, height: −400), 900)`
     equals the result for (100, 200, 300, 400).
  7. **display(containing:), single:** `single1x` (10, 10) → P; (1920, 500) → P (closed fallback,
     outer edge); (100, 1080) → P (top edge fallback); (−1, 0) → `nil`.
  8. **display(containing:), right (shared vertical edge):** (1440, 500) → R; (1439.5, 500) → P;
     (1440, 0) → R (corner); (1000, 950) → `nil` (above P, left of R: gap); (1500, 950) → R (above
     P's top, inside taller R); (3360, 500) → R (outer edge fallback); (5000, 5000) → `nil`.
  9. **display(containing:), left:** (−1, 10) → L; (0, 10) → P; (−1920, 0) → L.
  10. **display(containing:), above (shared horizontal edge):** (100, 900) → A; (100, 899.5) → P;
      (−240, 1500) → A.
  11. **display(containing:), below:** (100, 0) → P; (100, −0.5) → B; (100, −1080) → B.
  12. **normalizedRect:** (10, 20)–(110, 70) and (110, 70)–(10, 20) both → (10, 20, 100, 50);
      (110, 20)–(10, 70) → (10, 20, 100, 50); equal points (5, 5)–(5, 5) → (5, 5, 0, 0).
  13. **clamp point:** to P(0,0,1440,900): (2000, 950) → (1440, 900); (−5, −5) → (0, 0); inside
      unchanged.
  14. **clamp rect:** to P(0,0,1440,900): (1400, 850, 200, 200) → (1400, 850, 40, 50); disjoint
      (2000, 2000, 10, 10) → (1440, 900, 0, 0); touching-only (1440, 0, 10, 10) → (1440, 0, 0, 10);
      zero-size inside (100, 100, 0, 0) → unchanged; result is never `.null`.
  15. **dragRect, drag ending on another display (`right`):** start (1000, 500) on P, current
      (2000, 950) (on R) → (1000, 500, 440, 400).
  16. **dragRect, `left`:** start (−100, 500) on L, current (300, −50) → (−100, 0, 100, 500).
  17. **dragRect, `below`:** start (100, −100) on B, current (200, 300) (on P) → (100, −100, 100, 100).
  18. **dragRect zero-size:** start == current → zero-size rect at that point.
  19. **displayLocalTopLeftRect:** `single2x` (100, 100, 200, 50) → (100, 832, 200, 50); `right` R
      (1500, 1000, 100, 50) → (60, 30, 100, 50); `left` L (−1900, 0, 100, 100) → (20, 980, 100, 100);
      `below` B (10, −1070, 100, 100) → (10, 970, 100, 100); `above` A (−240, 1880, 100, 100) →
      (0, 0, 100, 100); a rect equal to the display frame → (0, 0, w, h) for every fixture display.
  20. **pixelAligned, 2×:** `single2x` (10.25, 20.75, 100.5, 50) → (10, 20.5, 101, 50.5);
      already aligned (10, 20, 100, 50) unchanged; (10.5, 20.5, 1, 1) unchanged.
  21. **pixelAligned, 1×:** `single1x` (10.25, 20.75, 100.5, 50) → (10, 20, 101, 51).
  22. **pixelAligned, negative origin:** `left` L @1× (−1900.4, 10.6, 50, 50) → (−1901, 10, 51, 51).
  23. **pixelAligned, float noise:** (10.0000000001, 20, 100, 50) @2× → (10, 20, 100, 50).
  24. **pixelAligned, zero size:** (10.25, 10.25, 0, 0) @2× → (10.5, 10.5, 0, 0); stays within the
      display.
  25. **pixelAligned stays on display:** P @2× (1439.8, 899.8, 0.2, 0.2) → (1439.5, 899.5, 0.5, 0.5),
      never exceeding `maxX`/`maxY`.
  26. **pixelSize:** (0, 0, 100, 50) scale 2 → 200 × 100; scale 1 → 100 × 50; (0, 0, 100.5, 50.5)
      scale 2 → 201 × 101; zero rect → 0 × 0.
  27. **captureRegion, mixed 1×/2×:** `mixed` P @2× rect (100, 100, 200, 100) → displayID P,
      sourceRect (100, 782, 200, 100), pixelSize 400 × 200; E @1× rect (1612, 100, 200, 100) →
      displayID E, sourceRect (100, 1240, 200, 100), pixelSize 200 × 100. Same point size, different
      pixel sizes.
  28. **captureRegion, clamped:** `right` P @2× rect (1400, 850, 200, 200) → appKitRect
      (1400, 850, 40, 50), sourceRect (1400, 0, 40, 50), pixelSize 80 × 100.
  29. **captureRegion, full display:** for each fixture display, `captureRegion(for: frame, on: d)`
      → sourceRect (0, 0, w, h), pixelSize (w·scale) × (h·scale) (e.g. `single2x` → 3024 × 1964).
- **Build check:** `xcodegen generate`, then
  `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test`
  succeed with the whole suite green.
- **Manual verification:** minimal by design. This spec adds pure math plus a ~10-line AppKit
  adapter, and no code path calls them yet, so there is nothing user-visible to check. The real
  multi-display checks (selection confined to start display, capture at 2×, correct area on
  left/above/below displays) are the manual steps of 0007 and 0008, which are the first consumers.
  The only manual step here:
  1. Run the built app and confirm it still launches with the `camera.viewfinder` menu bar icon and
     its menu works as in 0001 (no regression).

## Out of scope

- Selection overlay panels, the 4 × 4 pt click threshold, W × H readout — 0007.
- Calling ScreenCaptureKit, matching `SCDisplay` by `displayID`, `SCContentFilter` exclusion of our
  own app — 0008 (screenshots) and 0011 (recording).
- Window hit-testing and converting `SCWindow.frame` (CG) to AppKit — 0009 (uses this spec's
  `appKitRect(fromCG:)`).
- Recording pill placement — 0012; window frame tracking — 0013.
- Reacting to display configuration changes (cancel overlay / stop recording) — 0007, 0011.
- Annotation canvas coordinate transforms (`CanvasTransform`) — 0017.
