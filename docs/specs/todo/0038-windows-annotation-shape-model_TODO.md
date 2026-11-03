# 0038 — Port the pure annotation shape model to Snapper.Core (Windows)

> Source: docs/PRD.md §8.2 (lines 131–158: stroke widths, arrowhead, butt-capped shaft, rectangle outline hits, handles, Shift constraints, 3-view-point minimum), §8.3 (lines 160–165: text sizes, contrasting outline), §8.4 (line 169: image coordinates), §13.2 (lines 265–269: points → DIPs, backing scale → monitor scale factor, bold system font → Segoe UI Bold), §13.7 (line 313: one renderer, export at 96 DPI); AGENTS.md "Windows app (`windows/`)"
> Depends on: 0027-windows-app-settings (`RgbaColor`, `StrokeWidth`), 0028-windows-display-geometry (`PointD`, `SizeD`, `RectD`)

## Context

The `windows/` solution exists (0025). 0027 created a minimal `Snapper.Core.Annotate.Model.RgbaColor` and
`StrokeWidth` (S/M/L) for the last-used color/width settings, and 0028 created the Core geometry
primitives in `Snapper.Core.Geometry`. Nothing else exists under `windows/src/Snapper.Core/Annotate/`.

The annotation editor specs (0039 document/undo, 0040 window/canvas, 0041 editing, 0042 text, 0043
export) need one shape model that they can render, hit-test, move, and resize without WPF. This spec
ports the proven macOS model value types **verbatim in behavior** to C#. It adds no UI, so the
Windows app behaves exactly as before after it lands.

### macOS reference (port behavior and tests; don't redesign)

The macOS model lives in `Snapper/Annotate/Model/` and was specified by
`docs/specs/done/0015-annotation-shape-model_COMPLETED.md` (geometry rules, formulas, rationale; read
it, it is the detailed contract for items 1–5 below). Later macOS specs added more model files. The
table below says which macOS file each Windows spec ports. This spec follows the batch ownership
table, which deliberately pulls the pure drag session, contrasting outline, text metrics, and
measurement seam forward into this model spec (on macOS they arrived with 0018/0019).

| macOS file (introduced by) | Windows owner | Windows type(s) |
|---|---|---|
| `Model/ShapeGeometry.swift` (0015) | **0038** | `ShapeGeometry` |
| `Model/ArrowGeometry.swift` (0015) | **0038** | `ArrowGeometry` |
| `Model/ShapeHandle.swift` (0015) | **0038** | `ShapeHandle`, `ShapeHandleExtensions` |
| `Model/TextBox.swift` (0015, doc comment updated by 0019) | **0038** | `TextBox` |
| `Model/AnnotationShape.swift` (0015) | **0038** | `AnnotationShape`, `ShapeKind` |
| `Model/ShapeDragSession.swift` (0018) | **0038** | `ShapeDragSession`, `ShapeDragKind` |
| `Model/ContrastingOutline.swift` (0019) | **0038** | `ContrastingOutline` |
| `Model/TextMetrics.swift` (0019) | **0038** | `TextMetrics` |
| `Model/TextMeasuring.swift` (0019) | **0038** | `ITextMeasurer` + `AnnotationShape.WithStrokeWidth(width, pixelsPerDip, measurer)` |
| `Model/RGBAColor.swift` (0003) | 0027 (extended here only if members are missing, see below) | `RgbaColor`, `ColorPreset` |
| `Model/StrokeWidth.swift` (0003) | 0027 (extended here, see below) | `StrokeWidth` |
| `Model/AnnotationDocument.swift` (0016) | 0039 | `AnnotationDocument` + history types |
| `Model/ToolbarStyleState.swift` (0018: `ShapeStyle`, `SwatchSelection`, `ToolbarStyleEvent/Effects/State`, `AnnotationShape.style`) | 0040 (toolbar style) | — |
| `Model/TextCommit.swift` (0019: `TextEditDraft`, `TextCommitDecision`) | 0042 | — |
| `Model/TextInteractionDecision.swift` (0019) | 0042 | — |
| `Annotate/AnnotationDrawingGesture.swift` (0017: start-inside-image, edge clamping of Shift-constrained drags, view-point → pixel minimum drag) | 0040 | — |
| `Annotate/CanvasHitTester.swift` (0018: topmost hit, handle priority) | 0041 | — |
| `Annotate/AppKitTextMeasurer.swift` (0019) | 0042 (WPF `ITextMeasurer` implementation, Segoe UI Bold) | — |

Swift sources to port (all pure, Foundation/CoreGraphics only):

- `Snapper/Annotate/Model/ShapeGeometry.swift:6-53`
- `Snapper/Annotate/Model/ArrowGeometry.swift:13-47`
- `Snapper/Annotate/Model/ShapeHandle.swift:4-16`
- `Snapper/Annotate/Model/TextBox.swift:3-10`
- `Snapper/Annotate/Model/AnnotationShape.swift:6-183`
- `Snapper/Annotate/Model/ShapeDragSession.swift:3-27`
- `Snapper/Annotate/Model/ContrastingOutline.swift:3-17`
- `Snapper/Annotate/Model/TextMetrics.swift:3-12`
- `Snapper/Annotate/Model/TextMeasuring.swift:3-18`

Swift tests to port (every case):

- `SnapperTests/AnnotationShapeTests.swift:22-324` (18 tests)
- `SnapperTests/ShapeDragSessionTests.swift:8-53` (the first four tests; `previewsCommitOneHistoryStep`
  at `:54-68` needs `AnnotationDocument` and is ported by **0039**)
- `SnapperTests/ContrastingOutlineTests.swift:6-26`
- `SnapperTests/TextMetricsTests.swift:6-23`
- `SnapperTests/TextEditingUndoTests.swift:42-45` (the measurer half only: stroke-width change on a text
  shape remeasures through the injected measurer; the undo half is 0039/0042) and the fake measurer at
  `SnapperTests/TextCommitDecisionTests.swift:5-8`

### What differs on Windows (PRD §13.2)

- macOS "image points" are **DIPs** on Windows; macOS `pixelsPerPoint` (the capture's backing scale)
  is the capture monitor's **scale factor** (`DPI ÷ 96`: 1.0, 1.25, 1.5, 1.75, 2.0, …). The parameter is
  named `pixelsPerDip` in C#. Fractional scales are normal on Windows, so tests cover 1.5.
- Image pixel space is top-left origin, y down on both platforms. **There is no Y flip anywhere** on
  Windows (the macOS 0015 notes about `ShapeRenderer`/`CanvasTransform` flipping to AppKit's
  bottom-left space don't apply; WPF is y-down too).
- The font is **Segoe UI Bold** (macOS: bold system font). The model never touches fonts: measurement
  is injected through `ITextMeasurer`, implemented with WPF in 0042.
- The outline is drawn by WPF (0042/0043) by stroking the glyph geometry with a centered pen of
  `ContrastingOutline.PenThickness(fontPixelSize)` (12.5% of the font size, so 6.25% lands outside the
  glyph) and then filling on top. On macOS the same 12.5 value is the `NSAttributedString.strokeWidth`
  percentage. The constant and its meaning (visible outline = 6.25% of the font size) are unchanged.
- Negative-origin monitors and drags across monitors don't apply to this spec: every coordinate here
  is in **image pixel space** whose origin is the image's top-left corner. Converting screen/window
  positions into image pixels is 0040's `CanvasTransform`. Negative image coordinates are still valid
  (shapes moved partly off the image) and are tested.

## User story

As the developer building the Windows annotation editor, I can create, hit-test, move, and resize
rectangle, arrow, and text shapes in image pixel coordinates through a pure, fully unit-tested C#
model that behaves exactly like the macOS one, so the canvas, undo stack, and exporter share one
definition of shape geometry.

## Scope & changes

Everything below is **pure C# in `Snapper.Core`** (namespace `Snapper.Core.Annotate.Model`, folder
`windows/src/Snapper.Core/Annotate/Model/`). No WPF, WinForms, WinRT, `System.Drawing`, or interop
types. Use `double` throughout, `PointD`/`SizeD`/`RectD` from 0028, `Guid` for ids, and
`double.Hypot` for Swift `hypot`. Every type is immutable (records / readonly record structs) with
value equality, so 0039's undo can store snapshots.

### Prerequisites from 0027 and 0028 (check, don't duplicate)

- `RgbaColor` (0027): `Red`, `Green`, `Blue`, `Alpha` doubles in 0…1, a clamping constructor
  (non-finite → 0, non-finite alpha → 1), value equality, and the 8 presets
  (`RgbaColor.Red … White`, `RgbaColor.Presets`, `ColorPreset` with the macOS values from
  `Snapper/Annotate/Model/RGBAColor.swift:65-81`). If 0027 implemented `RgbaColor` without the
  presets or `ColorPreset`, add them to 0027's files in this spec and port
  `SnapperTests/RGBAColorTests.swift:7-24` (`presetOrder`, `presetValues`) into 0027's existing
  `RgbaColorTests.cs`. Don't create a second color type.
- `StrokeWidth` (0027): enum `S, M, L`. This spec adds two members in 0027's existing extension
  type (C# 14 extension properties if that's the form 0027 used, otherwise static extension
  methods). If 0027 already defines equivalents under other names, use those and don't duplicate:
  - `LineWidthDips` → `S = 2, M = 4, L = 8` (macOS `points`, `StrokeWidth.swift:6-12`)
  - `TextSizeDips` → `S = 16, M = 24, L = 36` (macOS `textPointSize`, `StrokeWidth.swift:14-20`)
- `PointD(X, Y)`, `SizeD(Width, Height)`, `RectD(X, Y, Width, Height)` with `Left/Top/Right/Bottom`
  (0028). `RectD` must be able to hold the zero-size rectangles used below. The model builds every
  rectangle through `ShapeGeometry.NormalizedRect`, so it never relies on `RectD` accepting negative
  sizes. If 0028's names differ, use 0028's.

### Units and coordinate space (binding for 0039–0043)

- **Image pixel space:** origin at the image's top-left, x right, y down, the captured bitmap's
  row order. All stored geometry is in image pixels (PRD §8.4).
- **`pixelsPerDip`:** image pixels per DIP, the capture monitor's scale factor, owned by 0039's
  `AnnotationDocument` and passed into every width-dependent operation:
  `strokePixels = width.LineWidthDips × pixelsPerDip` (2/4/8 DIP → 3/6/12 px at 150%, 4/8/16 px at 200%),
  `fontPixelSize = width.TextSizeDips × pixelsPerDip` (16/24/36 → 24/36/54 px at 150%).
- **Caller-supplied tolerances** (`slop`, handle `radius`, `minimumDrag`) are in image pixels. The
  model exposes the recommended view-DIP constants; 0040/0041 convert them at the current zoom so hit
  areas feel the same at every zoom (PRD §8.2 line 148: "at any zoom").

### Files and types

1. **`ShapeGeometry.cs`**: `public static class ShapeGeometry` (port `ShapeGeometry.swift:6-53`):
   - `RectD NormalizedRect(PointD a, PointD b)`: `(min x, min y, |dx|, |dy|)`.
   - `PointD SquareCorner(PointD anchor, PointD toward)`: side `max(|dx|, |dy|)`, signs of `dx`/`dy`
     kept, sign of 0 treated as `+` (rationale in macOS 0015).
   - `PointD SnappedEndpoint(PointD fixedPoint, PointD moving)`: 45° snap preserving length;
     `angle = Math.Round(Math.Atan2(dy, dx) / (π/4), MidpointRounding.AwayFromZero) × π/4` (Swift
     `.toNearestOrAwayFromZero`); zero length → `fixedPoint`.
   - `double DistanceToSegment(PointD p, PointD a, PointD b)`: clamped projection; degenerate → distance to `a`.
   - `double DistanceToOutline(PointD p, RectD r)`: outside → distance to the rect; inside → min distance to the four edges.
   - `bool TriangleContains(PointD p, PointD a, PointD b, PointD c)`: edge-inclusive sign test; a collapsed
     (zero-area) triangle contains only points on its closed edges (`ShapeGeometry.swift:45-50`).

2. **`ArrowGeometry.cs`**: `public readonly record struct ArrowGeometry(PointD Tip, PointD BaseCenter,
   PointD BaseLeft, PointD BaseRight, PointD ShaftStart, double StrokePixels)` with
   `static ArrowGeometry Make(PointD start, PointD end, StrokeWidth width, double pixelsPerDip)` and
   `RectD Bounds` (port `ArrowGeometry.swift:13-47` exactly):
   - `s = LineWidthDips × pixelsPerDip`, `L = |end − start|`, `u = (end − start)/L`.
   - `headLength = min(L, 3·s + 8·pixelsPerDip)`; `halfWidth = 0.4·headLength` (width = 80% of length, PRD §8.2 line 151).
   - `BaseCenter = end − u·headLength`; `BaseLeft = (base.x − u.y·halfWidth, base.y + u.x·halfWidth)`;
     `BaseRight = (base.x + u.y·halfWidth, base.y − u.x·halfWidth)`; `Tip = end`; `ShaftStart = start`.
   - `L == 0` → every point equals `start` (no NaN).
   - `Bounds`: bounding box of the three head points plus, when `ShaftStart != BaseCenter`, the shaft
     segment widened by `s/2` perpendicular to it (butt cap: no extension along the axis, PRD §8.2 line 150).
   - Doc comment: this is the single source of arrow geometry for bounds, hit-testing, and the shared
     renderer (0040): butt-capped shaft from `ShaftStart` to `BaseCenter`, unstroked filled head
     `Tip, BaseLeft, BaseRight`.

3. **`ShapeHandle.cs`**: `public enum ShapeHandle { TopLeft, TopRight, BottomLeft, BottomRight, Start, End }`
   plus `ShapeHandleExtensions.Opposite(this ShapeHandle)` (TL↔BR, TR↔BL, Start↔End). Corner names are in
   the y-down image space (`TopLeft = (Left, Top)`).

4. **`TextBox.cs`**: `public sealed record TextBox(PointD Origin, string Text, SizeD MeasuredSize)`.
   `Origin` is the top-left of the text layout box (top of the first line, left edge of the glyph run,
   not the baseline). Doc comment on `MeasuredSize` (port `TextBox.swift:6-8`): authoritative layout
   size in image pixels, **excluding the contrasting outline**; the UI layer must remeasure and update
   it in the same edit whenever the text or stroke width changes; the model never measures text.

5. **`AnnotationShape.cs`**: port `AnnotationShape.swift:6-183`.
   - `public abstract record ShapeKind` with a private constructor and three nested sealed records:
     `ShapeKind.Rectangle(RectD Rect)`, `ShapeKind.Arrow(PointD Start, PointD End)`,
     `ShapeKind.Text(TextBox Box)`. **Invariant:** `Rectangle.Rect` is always standardized; its `init`
     accessor normalizes (via `ShapeGeometry.NormalizedRect(rect.TopLeft, rect.BottomRight)` or an
     equivalent `Standardized` helper), so constructors and `with` expressions both keep the invariant.
   - `public sealed record AnnotationShape(Guid Id, ShapeKind Kind, RgbaColor Color, StrokeWidth StrokeWidth)`
     plus a convenience constructor `AnnotationShape(ShapeKind kind, RgbaColor color, StrokeWidth width)`
     that uses `Guid.NewGuid()`. Every operation below returns a new shape with the same `Id`.
   - Constants: `public const double HitSlopViewDips = 4, HandleRadiusViewDips = 6, MinimumDragViewDips = 3;`
     (macOS `hitSlopViewPoints`, `handleRadiusViewPoints`, `minimumDragViewPoints`).
   - `double StrokePixels(double pixelsPerDip)`, `double FontPixelSize(double pixelsPerDip)`.
   - `ArrowGeometry? GetArrowGeometry(double pixelsPerDip)` (null unless arrow).
   - `RectD Bounds(double pixelsPerDip)`: rectangle inflated by `s/2` on every side; arrow →
     `ArrowGeometry.Bounds`; text → `(Origin, MeasuredSize)`.
   - `bool HitTest(PointD p, double pixelsPerDip, double slop)`: threshold `s/2 + slop`; rectangle →
     **outline only** (`DistanceToOutline ≤ threshold`; the empty interior is not a hit, PRD §8.2 line 149);
     arrow → segment distance `start→end ≤ threshold` **or** inside the head triangle; text → inside
     `(Origin, MeasuredSize)` edge-inclusive, `slop` ignored.
   - `IReadOnlyList<(ShapeHandle Handle, PointD Position)> Handles`: rectangle → TL, TR, BL, BR in that
     order; arrow → Start, End; text → empty (PRD §8.2 line 156).
   - `ShapeHandle? HandleHitTest(PointD p, double radius)`: nearest handle within `radius`
     (distance `≤ radius`), ties keep the earlier handle in `Handles` order (strict `<`).
   - `AnnotationShape Moved(double dx, double dy)`: translates rect / both endpoints / text origin; no clamping.
   - `(AnnotationShape Shape, ShapeHandle Handle) Resized(ShapeHandle handle, PointD p, bool constrained)`:
     rectangle → fixed = position of `handle.Opposite()`; corner = constrained ? `SquareCorner(fixed, p)` : `p`;
     rect = `NormalizedRect(fixed, corner)`; returned handle per the flip rule (`corner.x < fixed.x` →
     left, `corner.y < fixed.y` → top, equal counts as right/bottom). Arrow (`Start`/`End`) → the other
     endpoint is fixed, moving endpoint snapped when constrained, handle returned unchanged. A handle
     that doesn't belong to the kind, and any handle on text → `(this, handle)` unchanged (doc comment:
     programmer error, never throws).
   - Static creation helpers: `RectD RectangleFromDrag(PointD start, PointD current, bool constrained)`,
     `(PointD Start, PointD End) ArrowFromDrag(PointD start, PointD current, bool constrained)`,
     `AnnotationShape? MakeRectangle(PointD dragFrom, PointD to, bool constrained, RgbaColor color,
     StrokeWidth width, double minimumDrag)` (null if width **or** height after the constraint
     `< minimumDrag`, or either is 0), `AnnotationShape? MakeArrow(...)` (null if length `< minimumDrag`
     or 0), `AnnotationShape MakeText(PointD origin, RgbaColor color, StrokeWidth width)` (empty text,
     `MeasuredSize` zero).
   - Copy mutators: `WithColor(RgbaColor)`, `WithStrokeWidth(StrokeWidth)`, `WithText(string)`,
     `WithMeasuredSize(SizeD)` (the last two are no-ops on non-text shapes and return an equal shape).
     Doc comment on `WithStrokeWidth(StrokeWidth)` and `WithText`: text callers must also supply a newly
     measured size in the same document edit.

6. **`ITextMeasurer.cs`**: port `TextMeasuring.swift:3-18`.
   ```csharp
   public interface ITextMeasurer
   {
       SizeD LayoutSize(string text, double fontPixelSize);   // image pixels, excludes the outline
       double LineHeight(double fontPixelSize);
   }
   ```
   Contract (doc comment; the WPF implementation in 0042 must satisfy it): measures **Segoe UI Bold** at
   `fontPixelSize` treating one image pixel as one DIP (96 DPI, `pixelsPerDip = 1` for `FormattedText`),
   so results don't depend on the monitor scale (PRD §13.7 line 313); width of the widest line × total
   line height; an empty string still has one line's height; a trailing newline adds a line.
   Plus the instance overload on `AnnotationShape`:
   `AnnotationShape WithStrokeWidth(StrokeWidth width, double pixelsPerDip, ITextMeasurer measurer)`:
   same as `WithStrokeWidth(width)`, and for text also sets `MeasuredSize =
   measurer.LayoutSize(Box.Text, TextMetrics.FontPixelSize(width, pixelsPerDip))`. Non-text shapes don't
   call the measurer.

7. **`TextMetrics.cs`**: `public static class TextMetrics` (port `TextMetrics.swift:3-12`):
   - `double FontPixelSize(StrokeWidth width, double pixelsPerDip)` = `TextSizeDips × pixelsPerDip` (PRD §8.3 line 164).
   - `double EditorFontSize(double fontPixelSize, double canvasScale)` = product. On Windows
     `canvasScale` is view DIPs per image pixel (0040's `CanvasTransform`), so the result is the WPF
     editor's `FontSize` in DIPs.
   - `PointD NewBoxOrigin(PointD click, double lineHeight, SizeD imageSize)`:
     `x = min(max(0, click.X), max(0, Math.BitDecrement(imageSize.Width)))`,
     `y = min(max(0, click.Y − lineHeight/2), max(0, imageSize.Height − lineHeight))`
     (Swift `nextDown` → `Math.BitDecrement`).

8. **`ContrastingOutline.cs`**: `public static class ContrastingOutline` (port `ContrastingOutline.swift:3-17`):
   - `const double OutlineFractionOfFontSize = 0.0625; const double StrokeWidthPercent = 12.5;`
   - `double RelativeLuminance(RgbaColor c)`: WCAG sRGB linearization (`c ≤ 0.04045 ? c/12.92 :
     ((c+0.055)/1.055)^2.4`, channel clamped to 0…1), `0.2126 R + 0.7152 G + 0.0722 B`.
   - `RgbaColor ColorFor(RgbaColor c)`: luminance `≥ 0.5` → black, else white; alpha = the text color's alpha
     (PRD §8.3 line 165; threshold rationale in `docs/specs/done/0019-annotation-text-tool_COMPLETED.md` item 7).
   - **Windows addition:** `double PenThickness(double fontPixelSize)` = `fontPixelSize × StrokeWidthPercent / 100`
     (the centered WPF pen width whose outer half is the visible 6.25% outline).

9. **`ShapeDragSession.cs`**: port `ShapeDragSession.swift:3-27`.
   - `public abstract record ShapeDragKind` (private ctor) with `ShapeDragKind.Move` (a singleton
     `ShapeDragKind.Move.Instance` or a parameterless sealed record) and `ShapeDragKind.Resize(ShapeHandle Handle)`.
   - `public sealed record ShapeDragSession(AnnotationShape Original, ShapeDragKind Kind, PointD StartPoint, RectD ImageBounds)`.
   - `AnnotationShape Preview(PointD point, bool shiftHeld)`: if `point == StartPoint` return `Original`
     unchanged (a click anywhere inside a handle's hit radius must not snap the handle to the pointer);
     otherwise clamp `point` into `ImageBounds` (inclusive of `Right`/`Bottom`, PRD §8.2 line 147), then
     Move → `Original.Moved(p − StartPoint)`; Resize → `Original.Resized(handle, p, shiftHeld).Shape`.
     Always computed from `Original` (never accumulates).
   - `AnnotationShape? Finish(PointD point, bool shiftHeld)`: the preview, or null if it equals `Original`.

### Project and build

- Add the files to `windows/src/Snapper.Core/Annotate/Model/` (SDK-style projects pick them up; no
  `.csproj` edit unless 0025 lists files explicitly).
- Add the tests under `windows/tests/Snapper.Core.Tests/` (files below) and a shared, `public`
  `windows/tests/Snapper.Core.Tests/Fakes/FakeTextMeasurer.cs` (port of
  `SnapperTests/TextCommitDecisionTests.swift:5-8`: width = `text.Length × fontPixelSize`, height =
  `fontPixelSize`, line height = `fontPixelSize`) so 0039/0042 can reuse it.
- No `Snapper.Windows` changes, no `NativeMethods.txt` changes, no new packages.
- No PRD or AGENTS.md changes: the behavior is already in PRD §8.2–8.4 and §13.2/13.7.

## Acceptance criteria

- [ ] `windows/src/Snapper.Core/Annotate/Model/` contains `ShapeGeometry`, `ArrowGeometry`, `ShapeHandle`,
      `TextBox`, `AnnotationShape`/`ShapeKind`, `ITextMeasurer`, `TextMetrics`, `ContrastingOutline`, and
      `ShapeDragSession`/`ShapeDragKind`, none of which reference WPF, WinForms, WinRT, `System.Drawing`, or interop.
- [ ] All geometry is stored in image pixels (top-left origin, y down); stroke and text sizes are
      `StrokeWidth × pixelsPerDip`, so S/M/L = 2/4/8 image DIPs and text 16/24/36 image DIPs hold at 100%, 150%, and 200%.
- [ ] Arrowhead length = 3 × stroke + 8 image DIPs, width 80% of it, clamped to the arrow length for short arrows.
- [ ] Rectangles hit only within `stroke/2 + slop` of their outline; their interior is not a hit.
- [ ] Shift-constrained rectangle creation and resize give a square in every quadrant; Shift-constrained
      arrow creation and endpoint drags snap to 45° multiples and preserve length.
- [ ] Rectangles expose 4 corner handles, arrows 2 endpoint handles, text none; a rectangle resized past its
      opposite corner flips and returns the new handle identity, keeping the anchor fixed for the rest of the gesture.
- [ ] Rectangle/arrow creation below the caller's minimum drag returns null.
- [ ] Drag-session previews clamp the pointer to the image bounds, never accumulate, and a click without movement yields no edit.
- [ ] The outline color is black for light colors (luminance ≥ 0.5) and white otherwise, with the text's alpha.
- [ ] A stroke-width change on a text shape through `ITextMeasurer` updates `MeasuredSize`; the model never measures text itself.
- [ ] Every operation preserves the shape's `Id`.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` pass (on macOS and Windows).

## Edge cases

- Zero-length arrow / zero-size rectangle: no NaN or infinity; arrow points all equal `start`; bounds of a
  zero-length arrow are the empty rect at `start`; a 0×0 rectangle has bounds of `s × s` and 4 handles.
- Arrow shorter than its head: the whole arrow is head, no shaft is painted, and bounds don't include a shaft.
- Collapsed head triangle: only points on its edges are inside (not the whole plane).
- Square constraint with `dx == 0` or `dy == 0`: the zero axis takes `+`.
- 45° snap at exact ties (22.5°, …) rounds away from zero; tests avoid exact ties.
- Fractional scale factors (1.25, 1.5, 1.75): all sizes are doubles and never rounded in the model. Pixel
  snapping for drawing is the renderer's business (0040).
- Negative coordinates and points outside the image are valid for `Moved` and stored shapes; only
  `ShapeDragSession` clamps the pointer (shapes themselves may extend past the image after a move).
- A handle grabbed off-centre (within its radius): `Preview` at the start point returns the original, so no snap.
- Text just placed with `MeasuredSize` zero: bounds empty at `Origin`; hit only at `Origin` exactly.
- `NewBoxOrigin` on an image shorter than one line: `y = 0`; a click at the image's right edge gives `x < width`.
- Out-of-range colors (`1.2`, `-1`): `RgbaColor` clamps, and luminance clamps each channel again.
- Mixed-DPI monitors, negative-origin monitors, and drags across monitors: not applicable to image-space
  geometry. They're tested where screen ↔ image conversion happens (0028 `DisplayGeometry`, 0040 `CanvasTransform`).

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`). Compare doubles with tolerance `1e-9`
  unless the Swift test compares exactly (then compare exactly; the arithmetic is the same). Unless noted,
  width `M`, color `RgbaColor.Red`. Fixtures as in Swift: `rectangle = (100,100,200,100)`,
  `arrow = (0,0)→(100,0)`, `text = origin (10,20), "Hello", measured (50,30)`. Use `[Theory]` with
  `[InlineData]`/`[MemberData]` for the scale loops. Every case below is a port of the cited Swift test;
  the cases marked **(new: 150%)** are the Windows mixed-DPI additions.

  **`AnnotationShapeTests.cs`** (port of `SnapperTests/AnnotationShapeTests.swift`):
  - `UnitsAndRecommendedTolerances` (`:22`): for S/M/L and `pixelsPerDip` ∈ {1, **1.5**, 2},
    `StrokePixels == {2,4,8}×scale`, `FontPixelSize == {16,24,36}×scale` (at 1.5: 3/6/12 and 24/36/54);
    constants `HitSlopViewDips == 4`, `HandleRadiusViewDips == 6`, `MinimumDragViewDips == 3`.
  - `NormalizationAndKindMutation` (`:35`): `NormalizedRect((10,20),(4,5)) == (4,5,6,15)`; a shape created with
    `ShapeKind.Rectangle` from that rect, and one produced by `shape with { Kind = new ShapeKind.Rectangle(...) }`,
    both hold the standardized rect. (If `RectD` can't represent negative sizes, build the input with the
    flipped corners via `RectD` from two points and assert the same result.)
  - `SquareConstraintsEveryQuadrantAndAxis` (`:44`): anchor (100,100): (130,110)→(100,100,30,30);
    (70,110)→(70,100,30,30); (130,90)→(100,70,30,30); (70,90)→(70,70,30,30); (110,140)→(100,100,40,40);
    (100,130)→(100,100,30,30); (130,100)→(100,100,30,30). Each holds both for `RectangleFromDrag(constrained: true)`
    and for `Resized(BottomRight, p, true)` on rect (100,100,100,100).
  - `AngleSnappingPreservesLength` (`:62`): fixed (0,0) and (50,50); length-100 point at θ → snapped θ':
    0→0, 22→0, 23→45, 44→45, 46→45, 90→90, 180→180, −135→−135, −22→0, −23→−45, 40→45; snapped point equals
    `fixed + 100(cos θ', sin θ')` and its length is 100; `SnappedEndpoint(fixed, fixed) == fixed`.
  - `SegmentAndOutlineDistances` (`:77`): segment (0,0)–(100,0): (50,6)→6, (−6,0)→6, (−3,4)→5, (103,4)→5;
    degenerate (5,5)–(5,5) with (8,9)→5; rect (10,20,20,30): (20,30)→10 (inside), (7,16)→5, (10,40)→0.
  - `TriangleEdgesWindingAndDegeneracy` (`:88`): (0,0),(5,0),(2,2),(5,5) inside triangle (0,0),(10,0),(0,10) in both
    windings; (8,8) outside; collapsed (0,0)×3: (50,50) outside, (0,0) inside; collinear (0,0),(5,0),(10,0): (20,0) outside.
  - `ArrowheadSizesScalesAndDirections` (`:99`): for (S,14), (M,20), (L,32) and scale ∈ {1, **1.5**, 2}:
    `Tip == (100,0)`, `ShaftStart == (0,0)`, `BaseCenter ≈ (100 − len·scale, 0)`,
    `BaseLeft ≈ (100 − len·scale, 0.4·len·scale)`, `BaseRight ≈ (100 − len·scale, −0.4·len·scale)`,
    `StrokePixels ≈ LineWidthDips·scale` (at 1.5, M: base center (70,0), base ±12). Short (0,0)→(10,0) at 1:
    base center (0,0), left (0,4), right (0,−4). Diagonal (0,0)→(30,40): base center ≈ (18,24). Reverse
    (100,0)→(0,0): base center ≈ (20,0). Rectangle and text → `GetArrowGeometry` null.
  - `PaintedBoundsIncludeStrokeAndHead` (`:123`): rectangle at 1 → (98,98,204,104); at **1.5 → (97,97,206,106)**;
    at 2 → (96,96,208,108). Arrow at 1 → (0,−8,100,16); at **1.5 → (0,−12,100,24)**; at 2 → (0,−16,100,32).
    Text → (10,20,50,30) at every scale. Diagonal (0,0)→(30,40) at 1: left ≈ −1.6, top ≈ −1.2, right ≈ 30, bottom ≈ 40.
    Arrow (0,0)→(1,0) at 1: top ≈ −0.4 (no shaft painted).
  - `RectangleOutlineHitsAtEveryScale` (`:139`): at 1, slop 4: (150,106) hit, (150,107) miss, (94,150) hit,
    (93,150) miss, (150,94) hit, (200,150) miss (interior). At 2, slop 8: (150,112) hit, (150,113) miss.
    **(new: 150%)** at 1.5, slop 6 (threshold 9): (150,109) hit, (150,110) miss, (91,150) hit, (90,150) miss.
  - `ArrowStrokeAndFilledHeadHits` (`:148`): at 1, slop 4: (50,6) hit, (50,7) miss, (−6,0) hit, (−7,0) miss,
    (−5,−4) miss, (106,0) hit, (82,7) hit (inside head), (85,7) miss. At 2, slop 8: (50,12) hit, (50,13) miss.
    **(new: 150%)** at 1.5, slop 6: (50,9) hit, (50,10) miss.
  - `AuthoritativeTextBoundsAndInclusiveHits` (`:158`): at 2, slop 100: (10,20), (60,50), (35,35) hit;
    (9,35), (61,35), (35,51) miss. `WithMeasuredSize(0×0)`: (10,20) hit, (35,35) miss, bounds (10,20,0,0).
  - `HandleOrderOppositesAndNearestHit` (`:169`): rectangle handles TL, TR, BL, BR at (100,100), (300,100),
    (100,200), (300,200); arrow Start (0,0), End (100,0); text empty; `Opposite().Opposite()` is identity for
    every handle; TL↔BR, TR↔BL, Start↔End. `HandleHitTest`: (304,203) r6 → BR; (306,200) → BR; (310,200) → null;
    tiny (0,0,4,4): (3,3) → BR, (2,2) → TL (tie keeps the earlier handle); text with radius 100 → null.
  - `MovementPreservesMetadataAndDoesNotClamp` (`:188`): for rectangle, arrow, text: `Moved(10,−5)` keeps
    `Id`, `Color`, `StrokeWidth`; bounds offset by (10,−5); moving back by (−10,5) equals the original.
    `arrow.Moved(−100,−100).Kind == Arrow((−100,−100),(0,−100))`.
  - `RectangleResizeFlipAndContinuedAnchor` (`:200`): rect (100,100,100,100):
    (TR,(250,50)) → (100,50,150,150), TR; (BR,(50,60)) → (50,60,50,40), TL; (BR,(50,300)) → (50,100,50,200), BL;
    (BR,(100,150)) → (100,100,0,50), BR; (BR,(150,50)) → (100,50,50,50), TR; each keeps Id/Color/StrokeWidth.
    Continued gesture: BR→(50,60) then returned handle →(150,250) gives (100,100,50,150), BR; then →(100,100),
    then constrained →(40,80) gives (40,40,60,60), TL. Constrained BR→(260,180) → (100,100,160,160).
  - `ArrowEndpointResizeAndInvalidHandles` (`:228`): End→(50,60) unconstrained → Arrow((0,0),(50,60)), End.
    Start→(30,65) constrained → start ≈ (100 − c, c) with `c = Hypot(70,65)/√2`, end stays (100,0), handle Start,
    Id/Color/StrokeWidth kept. Arrow with TL and rectangle with Start → unchanged shape and the same handle.
    Text with every handle → unchanged.
  - `CreationRejectsSmallDragsAfterConstraints` (`:251`): `MakeRectangle` from (10,10), min 3: (10,10) null,
    (12,40) null, (40,12) null, (40,40) → (10,10,30,30), constrained (40,12) → (10,10,30,30), (13,13) not null,
    (15,15) with min 6 null. `MakeArrow` from (0,0), min 3: (0,0) null, (2,0) null, (3,0) not null, (30,40) → Arrow((0,0),(30,40)),
    constrained (100,10) → start (0,0), end ≈ (Hypot(100,10), 0). `MakeText((10,20), Blue, L)` → Text box with
    empty string and zero size, color Blue, width L.
  - `CopyMutatorsAndStableIdentity` (`:280`): for rectangle, arrow, text: `WithColor(Blue)` changes only the color;
    `WithStrokeWidth(L)` changes only the width. For rectangle and arrow, `WithText("ignored")` and
    `WithMeasuredSize(0×0)` return an equal shape. `text.WithText("Updated\ntext").WithMeasuredSize(90×80)` keeps
    Id/Color/StrokeWidth and has the new box. Two shapes created from the same arguments have different `Id`s.
  - `ZeroSizeGeometryRemainsFiniteAndLocal` (`:307`): at scales {1, **1.5**, 2}: zero-length arrow at (−5,7): all
    five geometry points == (−5,7) and finite; bounds (−5,7,0,0); hit at (−5,7) with slop 0; no hit at (500,500)
    with slop 4; 2 handles. Rectangle (−5,7,0,0): bounds width == 4×scale; hit at (−5,7) with slop 0; 4 handles.
  - **New:** `WithStrokeWidthRemeasuresTextThroughInjectedMeasurer` (port of the measurer half of
    `SnapperTests/TextEditingUndoTests.swift:42-45`): text "Hello" (M) `.WithStrokeWidth(S, 2, FakeTextMeasurer)`
    → width S and `MeasuredSize == (160, 32)` (5 × 32, 32); at **1.5** → (120, 24). A rectangle
    `.WithStrokeWidth(L, 2, measurer)` equals `.WithStrokeWidth(L)`, and a counting fake records zero calls.

  **`ShapeDragSessionTests.cs`** (port of `SnapperTests/ShapeDragSessionTests.swift`; bounds (0,0,100,100),
  rectangle (20,20,40,30)):
  - `MovesClampAndNeverAccumulate` (`:8`): Move from (20,30): 100 previews at (30,25) all give (30,15,40,30);
    Shift doesn't change a move; `Finish(StartPoint)` null; preview at (−50,−50) clamps to (0,0) → (0,−10,40,30).
  - `EachCornerAnchorsOppositeAndShiftIsReversible` (`:18`): for each corner, Resize from that corner: unconstrained
    preview at (10,80) equals `Resized(handle, (10,80), false).Shape`; 100 constrained previews are squares that
    keep the opposite corner, and the unconstrained preview still equals the first one; `Finish(start)` null.
  - `OffCenterHandleClickDoesNotResize` (`:35`): Resize(TL) from (18,20): preview at the start equals the original;
    `Finish` null.
  - `ArrowMovementAndEndpointSnapping` (`:41`): arrow (20,20)→(50,50), S, Blue: Move from (20,20) to (30,15) → (30,15)→(60,45).
    For each endpoint, a Shift preview at (90,70) has an angle that is a multiple of 45° (±1e-8) and keeps the opposite endpoint.
  - **New:** `PreviewClampsToBottomRightEdgeInclusive`: Move from (20,30) to (500,500) → pointer clamped to (100,100),
    rect (100,90,40,30) (shape may extend past the image; only the pointer is clamped).
  - (`previewsCommitOneHistoryStep` `:54` is ported by 0039 together with `AnnotationDocument`.)

  **`ContrastingOutlineTests.cs`** (port of `SnapperTests/ContrastingOutlineTests.swift`):
  - `LuminanceAndPresets` (`:6`): black → 0, white → 1, (1,0,0) → 0.2126, (0,1,0) → 0.7152, (0,0,1) → 0.0722 (±1e-9);
    every `ColorPreset`: Yellow and White → black outline, the other six → white.
  - `ThresholdAlphaClampingAndThickness` (`:17`): gray with luminance 0.49999 → white; 0.50001 → black
    (gray channel = `1.055 × L^(1/2.4) − 0.055`); `ColorFor(new RgbaColor(1.2, −1, 0, 0.4)) == RgbaColor(1,1,1,0.4)`;
    `StrokeWidthPercent == 2 × OutlineFractionOfFontSize × 100`.
  - **New:** `PenThicknessIsTwiceTheVisibleOutline`: `PenThickness(48) == 6`; `PenThickness(24) == 3`;
    `PenThickness(f) / 2 == f × OutlineFractionOfFontSize`.

  **`TextMetricsTests.cs`** (port of `SnapperTests/TextMetricsTests.swift`):
  - `SizesUseCaptureScaleAndCanvasZoom` (`:6`): `FontPixelSize` for S/M/L at {1, **1.5**, 2} == {16,24,36} × scale;
    `EditorFontSize(48, 0.5) == 24`, `(48, 1) == 48`, `(48, 0.01) ≈ 0.48`.
  - `PlacementClampsTopBottomAndShortImages` (`:14`): image 100×80, line height 30: click (50,40) → (50,25);
    (99,2) → (99,0); (99,80) → (99,50); (100,80) → x < 100; (−1,−10) → (0,0); image 20×10, click (10,5) → y == 0.

  **`StrokeWidthTests.cs`** (extend 0027's file if it exists, otherwise create it; port of
  `SnapperTests/StrokeWidthTests.swift:6-7`): `LineWidthDips` for S, M, L == [2, 4, 8]; `TextSizeDips` == [16, 24, 36].

  **`RgbaColorTests.cs`**: only if this spec had to add presets (see Prerequisites): `PresetOrder` and
  `PresetValues` ported from `SnapperTests/RGBAColorTests.swift:7-24`.

- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  succeed with no warnings (warnings are errors).
- **Manual verification:** N/A. This spec adds only pure Core types. Nothing is wired into the
  running app, so there is no visible behavior to check on Windows 11 beyond the app still starting
  and showing its tray icon as before. Drawing, Shift constraints, hit areas, handles, and outlined
  text are verified manually in 0040, 0041, and 0042.

## Out of scope

- `RgbaColor`/`StrokeWidth` persistence and defaults: **0027** (this spec only adds missing members).
- Geometry primitives and screen ↔ DIP conversion: **0028**.
- `AnnotationDocument`, z-order/topmost hit-testing across shapes, selection, undo/redo, dirty flag, and
  ownership of `pixelsPerDip`; porting `ShapeDragSessionTests.previewsCommitOneHistoryStep`: **0039**.
- `CanvasTransform`, the drawing gesture (start inside the image, uniform shortening of Shift-constrained
  drags at the image edge, converting `MinimumDragViewDips` and slop/radius to image pixels), tools, the
  shared `ShapeRenderer`, and toolbar style state (`ShapeStyle`, swatch selection): **0040**.
- Selection, handle priority over body hits, moving/resizing with the mouse, delete, and recoloring the
  selection (including calling the measurer-aware `WithStrokeWidth`): **0041**.
- The WPF `ITextMeasurer` (Segoe UI Bold), the text editor, `TextEditDraft`/`TextCommitDecision`,
  `TextInteractionDecision`, and outline rendering: **0042**.
- Full-resolution export: **0043**.
- Other shape types (blur, highlighter, crop, numbered steps): PRD §2 non-goals.
