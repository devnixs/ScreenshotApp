# 0015 — Add the pure annotation shape model (rectangle, arrow, text box geometry)

> Source: docs/PRD.md §8.2 (rectangle, arrow, stroke width, resize handles), §8.3 (text size), §8.4 (image coordinates); AGENTS.md "Architecture rules"
> Depends on: 0003-app-settings (`RGBAColor`, `StrokeWidth`)

## Context

Not yet implemented. No app code exists; `Snapper/Annotate/Model/` will contain only
`RGBAColor.swift` and `StrokeWidth.swift` once 0003 is done.

The annotation editor (0017–0020) needs a shape model that it can render, hit-test, move and
resize without any AppKit dependency. AGENTS.md requires that the annotation model is pure Swift
with unit tests, that "views only render the model and forward input to it", and that "annotation
shapes are stored in **image pixel coordinates**". The PRD behavior this model must support:

- §8.2: *Rectangle* — "Outline-only rectangle. Shift constrains it to a square."
- §8.2: *Arrow* — "Straight line with a filled arrowhead. Shift snaps the angle to 45°."
- §8.2: *Stroke width* — "S / M / L = 2 / 4 / 8 image points. The arrowhead scales with the stroke width."
- §8.2: "Resize handles: the four corners of a rectangle and the two endpoints of an arrow."
- §8.2: *Select/Move* — "Click a shape to select it, drag the shape to move it, drag its handles to resize it."
- §8.3: text "size follows the stroke width: S = 16pt, M = 24pt, L = 36pt, in image points."
- §8.4: "Shapes are stored in image coordinates, so window zoom never changes the output."

This spec delivers only the value types and pure geometry. It adds no UI, so the app's behavior is
unchanged after it lands.

## User story

As the developer building the annotation editor, I can create, hit-test, move and resize rectangle,
arrow and text shapes in image pixel coordinates through a pure, fully unit-tested model, so the
canvas, undo stack and exporter all share one definition of shape geometry.

## Scope & changes

All of the following is **pure Swift** (Foundation + CoreGraphics value types `CGPoint`, `CGSize`,
`CGRect`, `CGFloat` only). **No `import AppKit`, no `NSColor`, no `NSFont`, no CoreText, no
drawing calls.** All files live in `Snapper/Annotate/Model/`. All types are `Equatable` and
`Sendable` (no `Codable` needed; nothing is persisted).

### Units and coordinate space (binding for 0016–0020)

- **Image pixel space**: origin at the image's **top-left**, x grows right, **y grows down**
  (the same row order as the captured `CGImage`). All stored geometry (`CGRect`, `CGPoint`,
  `CGSize`) is in image pixels. `ShapeRenderer` / `CanvasTransform` (0017) are responsible for
  flipping into AppKit's bottom-left view space or a CGContext; the model never flips.
- **`pixelsPerPoint: CGFloat`**: image pixels per image point (the capture's backing scale: 1 on
  a non-Retina capture, 2 on Retina). It is owned by `AnnotationDocument` (0016) and passed into
  every operation where a width matters. Stroke and text sizes are defined in image *points*
  (PRD §8.2/§8.3), so:
  - `strokePixels = strokeWidth.<line width in points> × pixelsPerPoint` (2/4/8 pt → 4/8/16 px at 2×)
  - `fontPixelSize = strokeWidth.<text size in points> × pixelsPerPoint` (16/24/36 pt → 32/48/72 px at 2×)
  Use the property names 0003 actually defines on `StrokeWidth` (e.g. `lineWidthPoints` /
  `textPointSize`); do not duplicate the 2/4/8 and 16/24/36 tables here.
- **Caller-supplied tolerances** (`slop`, handle `radius`, `minimumDrag`) are passed in **image
  pixels**. The canvas (0017) converts a fixed number of *view* points into image pixels at the
  current zoom, so hit areas feel the same at every zoom level. The model only provides the
  recommended view-point constants (below) for 0017 to use.

### Files and types

1. **`ShapeGeometry.swift`** — `enum ShapeGeometry` (namespace of static pure helpers):
   - `normalizedRect(from a: CGPoint, to b: CGPoint) -> CGRect` — standardized rect spanning two points.
   - `squareCorner(anchor: CGPoint, toward p: CGPoint) -> CGPoint` — returns the point
     `anchor + (sx·d, sy·d)` where `d = max(|dx|, |dy|)` and `sx`, `sy` are the signs of `dx`, `dy`
     (sign of 0 is treated as `+1`). **Why max, not min:** the square always reaches the cursor on
     its dominant axis, so it grows smoothly with the pointer in every direction instead of stalling
     when the cursor moves along one axis (matches Preview/Keynote Shift-drag behavior). Keeping the
     signs keeps the square in the quadrant the user is dragging into.
   - `snappedEndpoint(fixed: CGPoint, moving p: CGPoint) -> CGPoint` — 45° snap: with
     `θ = atan2(dy, dx)` and `L = hypot(dx, dy)`, returns `fixed + L·(cos θ', sin θ')` where
     `θ' = (θ / (π/4)).rounded() · (π/4)` (`.toNearestOrAwayFromZero`). **Length is preserved.**
     If `L == 0`, returns `fixed`. Since the snap set is symmetric, the y-down orientation does not
     change results.
   - `distance(from p: CGPoint, toSegment a: CGPoint, _ b: CGPoint) -> CGFloat` — Euclidean distance
     to the closed segment (projection parameter clamped to `[0, 1]`; degenerate `a == b` → distance to `a`).
   - `distance(from p: CGPoint, toOutlineOf r: CGRect) -> CGFloat` — distance to the rectangle's
     perimeter: outside → distance to the rect; inside → `min(x − minX, maxX − x, y − minY, maxY − y)`.
   - `triangleContains(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint, _ c: CGPoint) -> Bool` — inclusive
     of edges (sign-of-cross-product test).

2. **`ArrowGeometry.swift`** — `struct ArrowGeometry: Equatable, Sendable` with
   `tip`, `baseCenter`, `baseLeft`, `baseRight`, `shaftStart` (all `CGPoint`) and
   `strokePixels: CGFloat`; built by
   `static func make(start: CGPoint, end: CGPoint, strokeWidth: StrokeWidth, pixelsPerPoint: CGFloat) -> ArrowGeometry`.
   This is the single source of arrow geometry: `bounds`, hit-testing and `ShapeRenderer` (0017)
   all use it, so the screen and the export (0020) draw the same arrowhead.
   - Let `s = strokePixels`, `L = |end − start|`, `u = (end − start)/L`, `n = (−u.y, u.x)`.
   - **Arrowhead formula** (in pixels): `headLength = 3·s + 8·pixelsPerPoint`,
     `headWidth = 0.8 · headLength` (full width of the base).
     In image points this gives S: 14 × 11.2, M: 20 × 16, L: 32 × 25.6.
     **Justification:** the head grows linearly with the stroke, as §8.2 requires; the constant
     8 pt term keeps the S head clearly visible (a pure `k·s` head at 2 pt stroke would be a tiny
     6–8 pt nub), and the 0.8 aspect gives a ~43° tip angle, a conventional "pointy but not needle"
     annotation arrowhead. The base is always ≥ 3.2× the shaft width (the minimum ratio is
     reached at L), so the shaft never visibly pokes out of the head.
   - **Short arrows:** if `L < headLength`, clamp `headLength' = L` and `headWidth' = 0.8·L`
     (the whole arrow is the head). If `L == 0`, every point equals `start`.
   - `tip = end`; `baseCenter = end − u·headLength'`; `baseLeft/Right = baseCenter ± n·headWidth'/2`;
     `shaftStart = start`. The shaft is the segment `shaftStart → baseCenter`, stroked with width
     `s` and a **butt** cap; the head is the filled triangle `tip, baseLeft, baseRight` with no
     stroke. (0017's renderer must use exactly these: butt cap on the shaft, unstroked filled head.)

3. **`TextBox.swift`** — `struct TextBox: Equatable, Sendable { var origin: CGPoint; var string: String; var measuredSize: CGSize }`.
   - `origin` is the **top-left** of the text's layout box in image pixels.
   - **Measurement seam:** measuring text needs AppKit/CoreText (bold system font at
     `fontPixelSize`, plus the thin outline from §8.3), which the pure model may not import.
     So `measuredSize` (image pixels, including the outline) is **supplied by the view layer**
     (0019) and stored on the box. The model never computes it and treats it as authoritative for
     bounds and hit-testing. Whenever the string or the stroke width (hence font size) of a text
     shape changes, the caller must re-measure and set `measuredSize` (via `withMeasuredSize(_:)`
     on `AnnotationShape`, below) in the same edit. Document this contract in a doc comment on
     `measuredSize`.

4. **`ShapeHandle.swift`** — `enum ShapeHandle: CaseIterable, Equatable, Sendable { case topLeft, topRight, bottomLeft, bottomRight, start, end }`
   - `var opposite: ShapeHandle` — `topLeft↔bottomRight`, `topRight↔bottomLeft`, `start↔end`.
   - Corner names refer to the y-down image space (`topLeft = (minX, minY)`).

5. **`AnnotationShape.swift`** — `struct AnnotationShape: Identifiable, Equatable, Sendable`:
   - `let id: UUID` (default `UUID()` in init; preserved by every operation below, so 0016's undo
     and selection can track a shape across edits).
   - `var kind: Kind` with `enum Kind: Equatable, Sendable { case rectangle(CGRect); case arrow(start: CGPoint, end: CGPoint); case text(TextBox) }`.
   - `var color: RGBAColor`, `var strokeWidth: StrokeWidth`.
   - **Invariant:** a `.rectangle` rect is always standardized (non-negative width/height). The
     initializer and every operation standardize it.
   - Size helpers: `strokePixels(pixelsPerPoint:) -> CGFloat`, `fontPixelSize(pixelsPerPoint:) -> CGFloat`.
   - `arrowGeometry(pixelsPerPoint:) -> ArrowGeometry?` (non-nil only for `.arrow`).
   - **`bounds(pixelsPerPoint:) -> CGRect`** — the painted area, used for redraw invalidation and
     selection outlines:
     - rectangle: `rect.insetBy(dx: −s/2, dy: −s/2)` (stroke centered on the outline; exact for the
       default miter join at 90°, which 0017 must use);
     - arrow: union of the shaft's box (`shaftStart`/`baseCenter` segment widened by `s/2`
       perpendicular to it, butt caps → no extension along the axis) and the bounding box of the
       three head points;
     - text: `CGRect(origin: origin, size: measuredSize)`.
   - **`hitTest(_ p: CGPoint, pixelsPerPoint: CGFloat, slop: CGFloat) -> Bool`**:
     - rectangle: **outline only** — `distance(p, toOutlineOf: rect) ≤ s/2 + slop`. Clicking the
       empty interior does **not** hit (the rectangle is outline-only per §8.2, so the user can click
       through it to shapes or to start a new shape inside it). See Open question 2.
     - arrow: `distance(p, toSegment: start, end) ≤ s/2 + slop` **or** `p` is inside the head
       triangle (the head is wider than the shaft).
     - text: `p` inside `CGRect(origin, measuredSize)` (edges inclusive); `slop` is not applied —
       the box is already a large target.
     - **Why `s/2 + slop` (not `max`)**: anywhere on the painted stroke always hits, plus a constant
       on-screen margin; with `max`, thick strokes would get no margin beyond their own edge.
     - Recommended slop: `AnnotationShape.hitSlopViewPoints = 4` view points, converted by 0017 to
       image pixels at the current zoom (at 100% zoom on a 2× image that is 8 px).
   - **`handles` → `[(ShapeHandle, CGPoint)]`** (or a small `struct HandlePosition`): rectangle →
     the four corners in the order `topLeft, topRight, bottomLeft, bottomRight`; arrow → `start`,
     `end`; text → empty (§8.2 lists handles only for rectangles and arrows).
   - **`handleHitTest(_ p: CGPoint, radius: CGFloat) -> ShapeHandle?`** — the handle whose position
     is within Euclidean distance `≤ radius` of `p`; if several qualify (tiny shapes), the nearest
     wins, ties broken by the `handles` order. Recommended `AnnotationShape.handleRadiusViewPoints = 6`.
     (Giving handles priority over body hits is 0018's job.)
   - **`moved(by delta: CGVector) -> AnnotationShape`** — translates rect origin / both arrow
     endpoints / text origin. No clamping to the image bounds (shapes may extend past the image;
     export clips naturally). Keeps id, color, strokeWidth.
   - **`resized(handle: ShapeHandle, to p: CGPoint, constrained: Bool) -> (shape: AnnotationShape, handle: ShapeHandle)`**:
     - rectangle (corner handles): `fixed` = the position of `handle.opposite` on the current rect;
       `corner = constrained ? squareCorner(anchor: fixed, toward: p) : p`;
       new rect = `normalizedRect(from: fixed, to: corner)`.
       **Flip rule:** the returned handle is the corner of the new rect that sits at `corner`,
       determined by which side of `fixed` the corner lies on (`x < fixed.x` → left, `y < fixed.y`
       → top; equal counts as right/bottom). The caller must use the returned handle for the next
       drag event of the same gesture; because its opposite is then the same `fixed` point, the
       anchor never moves during a gesture, no matter how many times the user crosses it.
       Resizing has no minimum size (a 0-width rect is allowed mid-gesture; undo covers mistakes).
     - arrow (`.start`/`.end`): the other endpoint is fixed; the moving endpoint becomes
       `constrained ? snappedEndpoint(fixed: other, moving: p) : p`. Handle is returned unchanged.
     - Any handle that does not belong to the shape's kind (a corner on an arrow, any handle on
       text) returns `(self, handle)` unchanged. Document as a programmer error but don't trap.
   - **Creation helpers** (static; used by the Rectangle and Arrow tools in 0017):
     - `rectangleFromDrag(start: CGPoint, current: CGPoint, constrained: Bool) -> CGRect` —
       `normalizedRect(from: start, to: constrained ? squareCorner(anchor: start, toward: current) : current)`.
     - `arrowFromDrag(start: CGPoint, current: CGPoint, constrained: Bool) -> (start: CGPoint, end: CGPoint)` —
       end is `snappedEndpoint` when constrained.
     - `makeRectangle(dragFrom:to:constrained:color:strokeWidth:minimumDrag:) -> AnnotationShape?`
       and `makeArrow(dragFrom:to:constrained:color:strokeWidth:minimumDrag:) -> AnnotationShape?`
       return `nil` when the drag is too small to be intentional: rectangle if its width **or**
       height (after the constraint) is `< minimumDrag`; arrow if its length is `< minimumDrag`.
       A click without a drag therefore creates nothing. Recommended
       `AnnotationShape.minimumDragViewPoints = 3` view points, converted by 0017 at the current
       zoom. See Open question 1.
     - `makeText(origin:color:strokeWidth:) -> AnnotationShape` — empty string, `measuredSize = .zero`
       (0019 sets it while editing).
   - Convenience mutators returning a copy (used by 0016/0018/0019): `withColor(_:)`,
     `withStrokeWidth(_:)`, `withText(_:)` and `withMeasuredSize(_:)` (no-ops on non-text kinds).

### Project

- Add the files above under `Snapper/Annotate/Model/` and `SnapperTests/AnnotationShapeTests.swift`,
  then run `xcodegen generate`. No `project.yml` changes beyond what directory-based sources pick up
  (if 0001 lists sources explicitly, add them there).
- No PRD change in this spec (the decisions below that touch product behavior are listed as open
  questions for the orchestrator to confirm and record in `docs/PRD.md §8.2`).

## Acceptance criteria

- [ ] `Snapper/Annotate/Model/` contains `AnnotationShape`, `ShapeHandle`, `TextBox`,
      `ArrowGeometry`, `ShapeGeometry`, none of which import AppKit, SwiftUI or CoreText.
- [ ] All geometry is stored in image pixels (top-left origin, y down); stroke and text sizes are
      derived from `StrokeWidth` × `pixelsPerPoint`, so S/M/L = 2/4/8 image points and text 16/24/36
      image points hold at both 1× and 2×.
- [ ] Rectangle: Shift-constrained creation and resize yield a square in all four quadrants.
- [ ] Arrow: Shift-constrained creation and endpoint drag snap to the nearest multiple of 45°,
      preserving the drag length.
- [ ] The arrowhead's size scales with the stroke width using the formula above.
- [ ] Rectangles expose 4 corner handles, arrows 2 endpoint handles, text none.
- [ ] Resizing a rectangle past its opposite corner flips correctly and returns the new handle identity.
- [ ] Every operation preserves the shape's `id`, `color` and `strokeWidth`.
- [ ] `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` pass.

## Edge cases

- Zero-length arrow / zero-size rect: `bounds`, `hitTest`, `handles`, `arrowGeometry` must not
  divide by zero or produce NaN (arrow with `L == 0` → all head points equal `start`).
- Arrow shorter than its head length → head clamped to the arrow length (still a valid triangle).
- Square constraint when `dx == 0` or `dy == 0`: the zero component takes the `+` sign, so the
  square still has side `max(|dx|, |dy|)`.
- 45° snap exactly at a tie (22.5°, 67.5°, …): rounds away from zero (documented; tests avoid exact ties).
- Negative coordinates and points outside the image are valid (moving shapes off the edge).
- Text with `measuredSize == .zero` (just placed, not yet measured): bounds is an empty rect at
  `origin`; hit-test only matches `origin` exactly. 0019 measures before display.
- Resizing on a 1× vs 2× document: handles and resize are in pixels and independent of
  `pixelsPerPoint`; only stroke-dependent results (bounds, hit-test, arrowhead) change with it.

## Test plan

- **Unit tests** (`SnapperTests/AnnotationShapeTests.swift`, XCTest or Swift Testing — match what
  0001 set up). Compare `CGFloat`s with an accuracy of `1e-9` unless stated. Unless noted,
  `strokeWidth = .m`, `color = .red` (0003 preset).
  - **Units:** `.m` at `pixelsPerPoint` 1 → `strokePixels == 4`, `fontPixelSize == 24`; at 2 → 8 and 48;
    `.s`/`.l` at 2 → 4/16 and 32/72.
  - **ShapeGeometry.normalizedRect:** `(10,20)→(4,5)` gives `(4,5,6,15)`.
  - **Square constraint, four quadrants** (anchor `(100,100)`): `(130,110)→(100,100,30,30)`;
    `(70,110)→(70,100,30,30)`; `(130,90)→(100,70,30,30)`; `(70,90)→(70,70,30,30)`;
    dy dominant `(110,140)→(100,100,40,40)`; `dx == 0`: `(100,130)→(100,100,30,30)`.
  - **45° snap** (fixed `(0,0)`, moving point at length 100 and angle θ, y-down math angle):
    θ = 0→0, 22→0, 23→45, 44→45, 46→45, 90→90, 180→180 (or −180; assert the point `(−100, 0)`),
    −135→−135, −22→0, −23→−45; assert each snapped point's length is 100 and its angle is the
    expected one. Also a non-origin fixed point `(50,50)` with θ=40 → `(50+100cos45°, 50+100sin45°)`.
    Zero length → returns the fixed point.
  - **distance to segment:** segment `(0,0)–(100,0)`: `(50,6)` → 6; beyond the start `(−6,0)` → 6;
    `(−3,4)` → 5; beyond the end `(103,4)` → 5; degenerate segment `(5,5)–(5,5)` with `(8,9)` → 5.
  - **ArrowGeometry:** `(0,0)→(100,0)`, M, ppp 1 → headLength 20, headWidth 16: tip `(100,0)`,
    baseCenter `(80,0)`, base points `(80,±8)`. ppp 2 → headLength 40, width 32. S at ppp 1 → 14 × 11.2;
    L at ppp 1 → 32 × 25.6. Short arrow `(0,0)→(10,0)` M ppp 1 → headLength clamped to 10, width 8,
    baseCenter `(0,0)`. Zero-length → all points equal start, no NaN. Diagonal `(0,0)→(30,40)` →
    baseCenter `(18,24)` (u = (0.6,0.8), 20 back from the tip).
  - **bounds including stroke:** rectangle `(100,100,200,100)` M ppp 1 → `(98,98,204,104)`; ppp 2 →
    `(96,96,208,108)`. Arrow `(0,0)→(100,0)` M ppp 1 → `(0,−8,100,16)`; ppp 2 → `(0,−16,100,32)`.
    Text origin `(10,20)` measured `(50,30)` → `(10,20,50,30)`.
  - **Rectangle hit-test, tolerance at 1× vs 2×** (rect `(100,100,200,100)`, slop = 4 × ppp):
    ppp 1 (threshold 6): `(150,106)` hit, `(150,107)` miss, `(94,150)` hit, `(93,150)` miss,
    `(150,94)` hit (outside, above the top edge), centre `(200,150)` **miss** (interior);
    ppp 2 (threshold 12): `(150,112)` hit, `(150,113)` miss.
  - **Arrow hit-test** (`(0,0)→(100,0)`, M, ppp 1, slop 4 → threshold 6): `(50,6)` hit, `(50,7)` miss;
    beyond start `(−6,0)` hit, `(−7,0)` miss, `(−5,−4)` miss (distance ≈ 6.4); beyond end `(106,0)` hit;
    inside the head `(82,7)` hit (segment distance 7 > 6, but inside the triangle); `(85,7)` miss
    (head half-width at x = 85 is 6). At ppp 2, slop 8: `(50,12)` hit, `(50,13)` miss.
  - **Text hit-test:** box origin `(10,20)` measured `(50,30)`: `(10,20)`, `(60,50)`, `(35,35)` hit;
    `(9,35)`, `(61,35)`, `(35,51)` miss; with `measuredSize = .zero`, `(35,35)` miss.
  - **handles:** rectangle `(100,100,200,100)` → TL `(100,100)`, TR `(300,100)`, BL `(100,200)`,
    BR `(300,200)` in that order; arrow → start/end; text → empty.
  - **handleHitTest:** within radius 6 of BR `(304,203)` → `.bottomRight`; `(310,200)` → nil;
    tiny rect `(0,0,4,4)` with `p = (3,3)`, radius 6 → `.bottomRight` (nearest).
  - **moved(by:):** rect, arrow and text each translated by `(10,−5)`; id/color/strokeWidth unchanged.
  - **Resize, unconstrained:** rect `(100,100,100,100)`:
    `.topRight → (250,50)` → `(100,50,150,150)`, handle `.topRight`;
    `.bottomRight → (50,60)` (full flip) → `(50,60,50,40)`, handle `.topLeft`;
    then continuing the gesture with `.topLeft → (150,250)` → `(100,100,50,150)`, handle `.bottomRight`
    (anchor `(100,100)` unchanged);
    `.bottomRight → (50,300)` (horizontal flip only) → `(50,100,50,200)`, handle `.bottomLeft`;
    `.bottomRight → (100,150)` (zero width) → `(100,100,0,50)`, no crash.
  - **Resize, constrained:** rect `(100,100,100,100)`, `.bottomRight → (260,180)` → `(100,100,160,160)`;
    `.bottomRight → (40,80)` → square of side 60 at `(40,40)`, handle `.topLeft`.
  - **Arrow resize:** `(0,0)→(100,0)`: `.end → (50,60)` unconstrained → end `(50,60)`, start fixed;
    `.start → (30,65)` constrained → the start is snapped around the fixed end `(100,0)`: the
    angle from end to new start is 135° and its length is `hypot(70,65)` (≈ `(32.46, 67.54)`);
    handle returned unchanged. Corner handle on an arrow, any handle on text → shape unchanged.
  - **Creation:** `makeRectangle` with `minimumDrag 3`: drag `(10,10)→(10,10)` → nil; `(10,10)→(12,40)`
    → nil (width 2); `(10,10)→(40,40)` → rect `(10,10,30,30)`; constrained `(10,10)→(40,12)` →
    `(10,10,30,30)` (not nil — square side 30). `makeArrow`: click → nil; length 2 → nil;
    `(0,0)→(30,40)` → arrow; constrained `(0,0)→(100,10)` → end `(≈100.5, 0)` (length preserved, angle 0).
    `makeText` → empty string, `.zero` size, given color/width.
  - **Identity:** every operation above preserves `id`; `withColor`/`withStrokeWidth` change only
    that field; `withText`/`withMeasuredSize` are no-ops on non-text shapes.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build`
  and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification:** N/A. This spec adds only pure value types with no UI, hotkeys,
  permissions, capture or windows; nothing is wired into the running app yet, so there is no
  user-visible behavior to check beyond the app still building and launching as before. The
  visible behavior (drawing, Shift constraints, handles, hit areas) is verified manually in 0017
  and 0018.

## Open questions

1. **Minimum drag to create a shape (PRD gap).** §5's "4 × 4 points counts as a click" covers the
   selection overlay only; §8.2 doesn't say what a click or tiny drag does with the Rectangle or
   Arrow tool. Recommended: a drag smaller than **3 view points** (rectangle: width *or* height
   below it; arrow: length below it) creates nothing, so a click without a drag never leaves an
   invisible shape. The threshold is in view points (converted at the current zoom) so it feels
   the same at any zoom. If confirmed, add a line to `docs/PRD.md §8.2`.
2. **Clicking inside a rectangle's empty interior (PRD gap).** §8.2 says "click a shape to select
   it" but doesn't say whether the interior of an outline-only rectangle counts. Recommended:
   **outline only** (within half the stroke + 4 view points), so you can click through a rectangle to
   select or draw over what's inside it, the way Preview's outline shapes behave. If confirmed,
   add a line to `docs/PRD.md §8.2`.
3. **Arrowhead proportions (detail, low risk).** The PRD only says the arrowhead "scales with the
   stroke width". This spec fixes `headLength = 3·stroke + 8 pt`, `headWidth = 0.8·headLength`
   (S 14 × 11.2 pt, M 20 × 16 pt, L 32 × 25.6 pt). Confirm, or tweak the constants — they live in
   one place (`ArrowGeometry`).

## Out of scope

- `RGBAColor` presets and `StrokeWidth` values/tables — **0003-app-settings**.
- The shape list, z-order / topmost hit-testing across shapes, selection, undo/redo, dirty flag,
  `pixelsPerPoint` ownership — **0016-annotation-document-undo**.
- Rendering (`ShapeRenderer`), `CanvasTransform` (image ↔ view conversion, the y-flip, converting
  view-point slop/radius/min-drag into image pixels), the Rectangle and Arrow tools and Shift
  handling in the view — **0017-annotation-window-canvas**.
- Selecting, moving, resizing and deleting via the mouse, handle priority over body hits, color
  and width changes on the selection — **0018-annotation-editing**.
- Text measurement (`measuredSize`), editing, fonts, the contrasting outline and its luminance
  rule — **0019-annotation-text-tool**.
- Flattened full-resolution export — **0020-annotation-export**.
- Any other shape types (blur, highlighter, crop, numbered steps) — PRD §2 non-goals.
