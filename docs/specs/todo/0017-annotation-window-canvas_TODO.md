# 0017 — Open ⌘⇧6 captures in a resizable annotation window and draw rectangles and arrows

> Source: docs/PRD.md §8.1, §8.2 (Select/Rectangle/Arrow/Text rows, "active tool stays selected"), §8.4, §5, §6
> Depends on: 0001-xcodegen-app-shell, 0002-file-naming-save-folder, 0003-app-settings, 0004-display-geometry, 0005-global-shortcuts-app-mode, 0007-selection-overlay-rectangle, 0008-screenshot-rectangle-capture, 0009-window-selection-mode, 0015-annotation-shape-model, 0016-annotation-document-undo

## Context

No application code exists yet; everything below is created by the earlier specs in this backlog
or by this one. When this spec starts:

- `AppCoordinator.startAnnotate()` (0001, wired to ⌘⇧6 and the menu by 0005, gated on permission
  by 0006) runs the selection overlay with `CapturePurpose.annotate` (0007/0009), but nothing
  happens with the result yet (or it is a stub).
- `ScreenshotCapturer` (0008) turns a `SelectionResult` (`.rect` from 0007, `.window` from 0009)
  into a `CapturedImage` (a `CGImage` at native pixel resolution plus its capture metadata).
  0008 also provides `ShutterSound` and `ErrorAlerts`.
- `AnnotationShape`, `ShapeHandle`, the creation helpers with Shift constraints (square / 45° snap)
  and the arrowhead geometry exist as pure types (0015). `AnnotationDocument` holds the image pixel
  size, `pixelsPerPoint`, the shapes, the selection, undo/redo and the dirty flag (0016).
- `AppSettings.annotationColor` / `annotationStrokeWidth` exist with defaults red / M (0003);
  `RGBAColor` and `StrokeWidth` (2/4/8 image points) exist (0003).

PRD behavior this spec delivers:

- §8.1: "The capture works exactly as in section 6, but instead of being saved or copied, the image
  opens in a new annotation window." The window "is a normal, resizable window with a toolbar on
  top; initially scales the image to fit about 80% of the screen and never upscales it; can be open
  in several copies at once."
- §8.2: tools Select/Move (V), Rectangle (R, Shift → square), Arrow (A, Shift → 45°), Text (T).
  "The active tool stays selected after each shape is drawn."
- §8.4: "Shapes are stored in image coordinates, so window zoom never changes the output."

### Decisions taken in this spec (interpretations of the PRD, no new product behavior)

1. **Shutter sound plays** for ⌘⇧6 when *Play shutter sound* is on, because §8.1 says the capture
   works "exactly as in section 6" and §6 step 4 is part of that capture. The *Screenshot
   destination* setting is **ignored** (§8.1: "instead of being saved or copied"). Errors follow §6
   step 5 (generic alert via `ErrorAlerts`; missing permission is already routed to onboarding by
   0006's gate).
2. **"80% of the screen"** means 80% of the `visibleFrame` of the screen the image was captured on
   (the selection's display). Fallback: `NSScreen.main`, then the first screen.
3. **"Never upscales"** holds both at open and on resize: the image is shown at most at its natural
   point size (1 image point = 1 view point). Enlarging the window beyond that leaves the image at
   100% and centers it with neutral margins (letterboxing). Shrinking the window scales the image
   down to fit, preserving aspect ratio.
4. **Activation policy stays `.accessory`.** Switching to `.regular` would show a Dock icon and an
   app menu bar, which contradicts §3/§12 ("menu bar app only, with no Dock icon"). The cost is that
   annotation windows don't appear in ⌘Tab; acceptable for v1. The window is brought to front with
   `NSApp.activate()` + `makeKeyAndOrderFront(nil)`.
5. **Toolbar = `NSToolbar` with `toolbarStyle = .unified`.** It is the native "toolbar on top" for a
   normal titled window, gives correct title-bar integration and keyboard/VoiceOver behavior for
   free, and later specs just add item identifiers. A custom top bar view would have to re-create
   all of that.
6. **Split with 0018:** this spec adds only the **tool** group to the toolbar. The Color and Stroke
   width controls (swatches, "+" color panel, S/M/L, apply-to-selection, persisting the last-used
   values) are entirely 0018; Save/Copy are 0020. Until 0018 lands, new shapes use the color and
   stroke width **read from `AppSettings` when the window opens** (default red / M). This spec never
   writes those settings.
7. **Text tool:** the Text segment is present but **disabled**, and the T key is ignored, until 0019
   enables it. A disabled control is honest; a silent no-op would look broken.
8. **Select tool (V)** is selectable here, but clicking/dragging with it does nothing until 0018
   adds select/move/resize.
9. Drags are confined to the image: a mouse-down in the letterbox margin with a drawing tool does
   nothing; during a drag the current point is clamped to the image bounds. A "click" (pointer moved
   less than 3 view points between mouse-down and mouse-up) creates no shape and no undo step.

## User story

As a Snapper user, I press ⌘⇧6, select an area or a window, and the capture opens in front of me in
a resizable window sized to fit my screen, where I can draw rectangles and arrows (Shift for squares
and 45° angles), switch tools with V/R/A, and open several such windows at once.

## Scope & changes

### Pure logic (unit-tested, no AppKit import)

- **`Snapper/Annotate/CanvasTransform.swift`** — `struct CanvasTransform: Equatable, Sendable`.
  - Inputs: `imagePixelSize: CGSize`, `pixelsPerPoint: CGFloat` (> 0), `viewSize: CGSize` (the
    canvas bounds size; the canvas is **flipped**, origin top-left, y down — same orientation as
    image pixel space).
  - Computed: `scale` = view points per image pixel =
    `min(viewW / imageW, viewH / imageH, 1 / pixelsPerPoint)`; `imageRectInView` = image rect of
    size `imagePixelSize * scale`, centered in the view (`offset.x = (viewW − w)/2`,
    `offset.y = (viewH − h)/2`). Degenerate inputs (zero/negative view or image size) yield
    `scale = 0` handling that never divides by zero: conversions to image space return `nil`
    (or the transform is `nil` — pick one and test it).
  - `imagePoint(fromView:) -> CGPoint` = `(p − offset) / scale`; `viewPoint(fromImage:)` = `p·scale + offset`;
    `viewRect(fromImage:)`, `imageRect(fromView:)`; `viewLength(fromImage:)` (used by 0018 for
    handle sizes); `isInsideImage(viewPoint:)`; `clampedImagePoint(fromView:)` (clamps to
    `0…imageW`, `0…imageH`).
  - Must not use `DisplayGeometry`: this is view↔image space, not screen space. No y-flip is needed
    because both spaces are top-left/y-down; state that in a doc comment.
- **`Snapper/Annotate/AnnotationWindowSizing.swift`** — `enum AnnotationWindowSizing` with a static
  function, e.g.
  `frame(imagePixelSize:pixelsPerPoint:screenVisibleFrame:chromeHeight:minContentSize:) -> (contentSize: CGSize, windowFrame: CGRect)`.
  - `imagePointSize = imagePixelSize / pixelsPerPoint` (a 2× capture of 800×600 px is 400×300 pt).
  - Budget: `maxW = 0.8 · visibleFrame.width`, `maxH = 0.8 · visibleFrame.height − chromeHeight`.
  - `fit = min(1, maxW / imagePointW, maxH / imagePointH)`; image area = `imagePointSize · fit`
    (never upscaled).
  - `contentSize = max(imageArea, minContentSize)` per dimension (the image is then letterboxed by
    `CanvasTransform`, never upscaled), but never larger than `visibleFrame` minus chrome.
  - `windowFrame`: size `(contentW, contentH + chromeHeight)`, centered in `visibleFrame`, in AppKit
    global (bottom-left) coordinates — `visibleFrame` comes from `NSScreen`, so no CG conversion is
    involved. Round to whole points.
  - A second helper `cascaded(_ frame: CGRect, avoiding existingOrigins: [CGPoint], within visibleFrame: CGRect) -> CGRect`:
    while the frame's top-left equals an existing window's top-left, shift by (+24, −24) points;
    stop shifting once it would leave `visibleFrame`. Keeps multiple editors from stacking exactly.
- **`Snapper/Annotate/AnnotationTool.swift`** — `enum AnnotationTool: CaseIterable, Sendable { case select, rectangle, arrow, text }`.
  - `static func forShortcut(characters: String, hasCommandOptionOrControl: Bool, textToolEnabled: Bool) -> AnnotationTool?`:
    case-insensitive `v/r/a/t`; returns `nil` when Command, Option or Control is held, for other
    characters, and for `t` when `textToolEnabled == false`. (Shift is allowed so Caps Lock/Shift
    don't break shortcuts.) `segmentIndex` / `init?(segmentIndex:)` for the toolbar control, in
    order Select, Rectangle, Arrow, Text.
  - `var createsShapesByDragging: Bool` (`true` for rectangle and arrow).
  - Default tool when a window opens: **Rectangle** (most common first action; PRD is silent and
    this is not product-visible beyond the initial selected segment — noted in the report).

### Rendering (CoreGraphics only, shared with export in 0020)

- **`Snapper/Annotate/ShapeRenderer.swift`** — `enum ShapeRenderer` (no AppKit; imports CoreGraphics).
  Contract: the `CGContext` passed in has a CTM that maps **image pixel space, origin top-left,
  y down** to the destination. Both the canvas and the 0020 exporter set that up and then call:
  - `drawImage(_ image: CGImage, in context: CGContext)` — draws the image at
    `(0, 0, width, height)` in image pixels, compensating for CG's bottom-up image drawing
    (save, translate `(0, h)`, scale `(1, −1)`, draw, restore). Sets interpolation `.high`.
  - `draw(_ shapes: [AnnotationShape], pixelsPerPoint: CGFloat, in context: CGContext)` and
    `draw(_ shape: AnnotationShape, pixelsPerPoint:in:)` (for the drag preview).
    - Line width in pixels = `shape.strokeWidth` image points × `pixelsPerPoint`.
    - Rectangle: outline only, stroked path centered on the shape rect, miter join, color from
      `RGBAColor` → `CGColor` (sRGB).
    - Arrow: shaft from start to the arrowhead base (so the shaft never pokes through the tip),
      round line cap at the start; arrowhead = filled triangle using 0015's arrowhead geometry
      (scales with stroke width per §8.2).
    - Text shapes are skipped here; 0019 adds text drawing.
  - Selection handles are **not** drawn here (they're UI only, 0018), so export never contains them.

### Platform glue (AppKit, `@MainActor`, manually verified)

- **`Snapper/Annotate/AnnotationCanvasView.swift`** — `final class AnnotationCanvasView: NSView`.
  - `isFlipped = true`, `acceptsFirstResponder = true`, `wantsLayer = true`, redraw policy
    `.onSetNeedsDisplay`; autoresizes with the window's content view.
  - Holds a reference to the controller's `AnnotationDocument` (or to a small state object owned by
    the controller), the `CGImage`, the current `AnnotationTool`, the current color and stroke width,
    and an optional transient `previewShape`.
  - `draw(_:)`: fill bounds with a neutral background (`NSColor.underPageBackgroundColor` or
    `windowBackgroundColor`), build `CanvasTransform` from `bounds.size`, get
    `NSGraphicsContext.current!.cgContext` (invariant inside `draw(_:)`; comment it),
    `translateBy(offset)`, `scaleBy(scale, scale)`, clip to the image rect, then
    `ShapeRenderer.drawImage`, `ShapeRenderer.draw(document.shapes…)`, then the preview shape.
  - Mouse with Rectangle/Arrow tool:
    - `mouseDown`: if the point is outside the image rect, ignore. Otherwise record the start in
      image pixels.
    - `mouseDragged`: current point = `clampedImagePoint`; build the preview with 0015's creation
      helpers (`constrainToSquare` / `snapTo45` = `event.modifierFlags.contains(.shift)`), using the
      current color and stroke width; `needsDisplay = true`.
    - `flagsChanged` during a drag: rebuild the preview with the new Shift state at the last
      pointer position, so pressing/releasing Shift updates the shape without moving the mouse.
    - `mouseUp`: if the pointer moved < 3 view points in total, discard (no shape). Otherwise add
      the final shape to the document as **exactly one undo step** (via 0016's add-shape operation,
      or its begin/commit grouping if that is how 0016 exposes it). Clear the preview. The tool stays
      the same (§8.2).
    - With the Select tool (until 0018) and the disabled Text tool, mouse events do nothing.
  - `keyDown`: map through `AnnotationTool.forShortcut(...)` (Text disabled until 0019); on a match,
    tell the controller to switch tool (which updates the toolbar segment). Unmatched keys go to
    `super` (so ⌘-shortcuts still reach the menu).
  - Cursor: crosshair over the image while a drawing tool is active (`resetCursorRects` /
    tracking area), arrow otherwise.
- **`Snapper/Annotate/AnnotationWindowController.swift`** — `@MainActor final class AnnotationWindowController: NSWindowController, NSWindowDelegate, NSToolbarDelegate`.
  - Init with a `CapturedImage` and the target `NSScreen`. Creates the `AnnotationDocument`
    (image pixel size from the `CGImage`, `pixelsPerPoint` from the capture — the display's
    `backingScale` from `DisplayInfo` if `CapturedImage` doesn't carry it directly).
  - Window: `NSWindow` with `[.titled, .closable, .miniaturizable, .resizable]`,
    `isReleasedWhenClosed = false`, `contentMinSize = 480 × 300` points (fits the toolbar that 0018
    and 0020 complete; they may raise it), `tabbingMode = .disallowed`, content view = the canvas.
  - Title: the screenshot file name for the capture date **without extension** and without a
    collision suffix, from `FileNamer` (0002), e.g. `Screenshot 2026-11-02 at 19.18.04`. If
    `FileNamer` only returns names with an extension, strip it with `deletingPathExtension`.
  - Toolbar: `NSToolbar(identifier: "AnnotationToolbar")`, `displayMode = .iconOnly`,
    `allowsUserCustomization = false`, `window.toolbarStyle = .unified`. Item identifiers are kept in
    one ordered list in the controller so 0018/0020 append theirs (color, stroke, flexible space,
    copy, save). This spec provides:
    - `tools`: an `NSToolbarItem` whose view is an `NSSegmentedControl` (`trackingMode = .selectOne`)
      with SF Symbols `cursorarrow` (Select), `rectangle` (Rectangle), `arrow.up.right` (Arrow),
      `textformat` (Text, `setEnabled(false, forSegment: 3)`); tooltips include the shortcut, e.g.
      "Rectangle (R)"; accessibility labels set.
  - `currentTool` property: setting it updates the canvas and the segmented control; the segmented
    control's action sets it.
  - Current color / stroke width: read once from `AppSettings` in init (0018 makes them editable).
  - Placement: measure `chromeHeight` as `window.frameRect(forContentRect: r).height − r.height`
    after the toolbar is attached; compute the frame with `AnnotationWindowSizing.frame(...)` for the
    target screen's `visibleFrame`, then `cascaded(...)` against the manager's open windows; set it
    with `setFrame(_:display:)`.
  - `windowWillClose(_:)`: notify the manager (closure or delegate) so it drops the controller.
    No "Discard changes?" yet — that is 0020.
  - Makes the canvas the window's `initialFirstResponder` so V/R/A work immediately.
- **`Snapper/Annotate/AnnotationWindowManager.swift`** — `@MainActor final class AnnotationWindowManager`,
  owned by `AppCoordinator`.
  - `func open(_ captured: CapturedImage, displayID: CGDirectDisplayID?)`: resolve the target
    `NSScreen` (match `deviceDescription["NSScreenNumber"]` to the display ID; fallback
    `NSScreen.main`, then `NSScreen.screens.first`), create the controller, append it to
    `private(set) var controllers: [AnnotationWindowController]` (0020 uses this list for the quit
    prompt), `showWindow(nil)`, `window.makeKeyAndOrderFront(nil)`, `NSApp.activate()`.
  - Removes a controller from the list when its window closes; nothing else retains it, so it is
    released.
  - Activation policy is not changed (stays `.accessory`, decision 4).
- **`Snapper/App/AppCoordinator.swift`** — ⌘⇧6 path: when the overlay returns a `SelectionResult`
  for `CapturePurpose.annotate`, call `ScreenshotCapturer` exactly as the screenshot path does
  (same filter excluding our own app, same `.rect` / `.window` handling, no shadow for windows),
  then on the main actor: play `ShutterSound` if `AppSettings.playShutterSound`, and
  `annotationWindowManager.open(captured, displayID:)`. Do **not** write a file or touch the
  clipboard, regardless of *Screenshot destination*. On failure: `ErrorAlerts` generic alert (§6
  step 5). The app mode returns to idle after the overlay closes (0005); open editors don't block
  any shortcut. Reuse the screenshot path's capture code rather than duplicating it (e.g. a shared
  `capture(_ selection:) async throws -> CapturedImage` on the coordinator or capturer).
- **Edit menu / key routing:** nothing new. ⌘Z/⌘C/⌘S wiring is 0018/0020.

### Project

- New files go under `Snapper/Annotate/` and `SnapperTests/`; run `xcodegen generate`.
- No PRD change in this spec. Decisions 1–3 and 9 above interpret existing PRD text; if the user
  confirms them, the coordinator may record them in docs/PRD.md §8.1.

## Acceptance criteria

- [ ] ⌘⇧6 (or menu → Annotate) shows the selection overlay; selecting a rectangle or a window opens
      a new annotation window in front of other apps, key, even though Snapper has no Dock icon.
- [ ] The capture is identical to a ⌘⇧4 capture of the same selection (native pixels, window
      without shadow and without covering windows), and our overlay is not in it.
- [ ] No file is written and the clipboard is unchanged by ⌘⇧6, whatever the destination setting.
      The shutter sound plays iff *Play shutter sound* is on.
- [ ] The window opens on the capture's screen, centered, with the image at most 80% of that
      screen's visible frame in each dimension (minus chrome) and never larger than its natural
      point size; small captures sit at 100% in a window of at least the minimum size.
- [ ] The window is titled like `Screenshot 2026-11-02 at 19.18.04`, is resizable, and has a
      unified toolbar with Select/Rectangle/Arrow/Text (Text disabled).
- [ ] Several annotation windows can be open at once; each new one is cascaded if it would exactly
      overlap another; closing one doesn't affect the others.
- [ ] Rectangle tool: drag draws an outline rectangle in the current color/width (red/M by default);
      holding Shift makes it a square, also when Shift is pressed mid-drag without moving.
- [ ] Arrow tool: drag draws a straight line with a filled arrowhead at the end; Shift snaps to
      multiples of 45°.
- [ ] After drawing, the same tool stays active. A click without a drag creates nothing.
- [ ] V, R, A switch tools when the canvas is focused and the toolbar reflects it; T does nothing yet.
- [ ] Resizing the window rescales image and shapes together (shapes stay on the same image
      content); enlarging beyond 100% doesn't upscale.
- [ ] Each completed shape is one undo step in the document (verified by unit tests in 0016 and by
      0018's ⌘Z once wired).

## Edge cases

- Capture on a non-Retina external display (pixelsPerPoint 1) vs a Retina built-in (2): window size
  uses points, stroke widths are image points × pixelsPerPoint, so a 2× capture's stroke is 8 px for M.
- Displays with negative origins (left of / below the main one): sizing uses that `NSScreen`'s
  `visibleFrame` in AppKit global coordinates directly; the window must land on that display.
- The capture display disconnected between capture and window open: fall back to the main screen.
- Very wide (e.g. 5000×200 pt) or very tall captures: fit is limited by the one exceeding dimension;
  content height/width still respects `contentMinSize`, image letterboxed.
- Tiny captures (e.g. 10×10 pt): window at `contentMinSize`, image drawn at 100% in the center.
- Dragging outside the window/image: end point clamped to the image edge; mouse-down in the
  letterbox margin starts nothing.
- Window resized during a drag (not possible with a mouse drag in progress) — no special handling.
- Window moved to a screen with another backing scale: rendering stays correct because the canvas
  draws in view points through `CanvasTransform`; the model is untouched.
- Very large captures (e.g. 6K display, 2×): drawing the full `CGImage` scaled every frame must stay
  responsive; if not, cache the scaled image per view size (implementer's choice, no behavior change).
- Shortcuts fired while an editor is key: ⌘⇧4/⌘⇧6 open the overlay normally; the overlay must
  still appear above the editor.
- Capture failure (e.g. window closed between selection and capture): generic alert, no window.

## Test plan

- **Unit tests**
  - `SnapperTests/CanvasTransformTests.swift`
    - Exact fit, 1×: image 400×300 px, ppp 1, view 400×300 → scale 1, offset 0; round-trip
      view→image→view of corners and center is identity (accuracy 1e-9).
    - Landscape letterbox: image 1600×900 px, ppp 2, view 400×400 → scale 0.25, image rect
      `(0, 87.5, 400, 225)`; view point `(200, 200)` → image `(800, 450)`; view `(0, 87.5)` → `(0, 0)`.
    - Portrait letterbox: image 600×1200 px, ppp 1, view 600×300 → scale 0.25, image rect x offset
      225, y 0.
    - No upscale: image 200×100 px, ppp 2, view 800×800 → scale 0.5 (= 1/ppp), image rect
      `(350, 375, 100, 50)` centered.
    - 1× no upscale: image 200×100, ppp 1, view 1000×1000 → scale 1, centered.
    - `isInsideImage` true inside, false in margins; `clampedImagePoint` clamps a margin point to
      the nearest image edge (both axes, including negative values).
    - `viewRect(fromImage:)` / `imageRect(fromView:)` round-trip; `viewLength(fromImage: 8)` with
      scale 0.25 → 2.
    - Degenerate view size (0×0) and zero image size behave as defined (no NaN/inf, conversions
      return `nil` or transform is `nil`).
  - `SnapperTests/AnnotationWindowSizingTests.swift` (use `chromeHeight` 52, `minContentSize` 480×300)
    - Large landscape image capped at 80%: 5120×2880 px @2× (2560×1440 pt), visibleFrame
      `(0, 0, 1440, 875)` → maxW 1152, maxH 700 − 52 = 648, fit 0.45 → content 1152×648.
      Then 5120×2000 px @2× on the same screen → width-limited (fit 0.45) → content 1152×450,
      aspect ratio preserved (±1 pt rounding).
    - Small image not upscaled: 800×600 px @2× → content 480×300 (min) — image would be 400×300 pt,
      so assert content == `max(400×300, 480×300)` = 480×300; and 1200×800 px @1× on a 2560×1415
      screen → content exactly 1200×800.
    - 2× uses point size: 2000×1000 px @2× on a large screen → content 1000×500, not 2000×1000.
    - Portrait: 1000×3000 px @1× on 1440×875 → height-limited, fit 0.216 → image area 216×648;
      content 480×648 (width raised to the minimum, image letterboxed).
    - Window frame = content + chrome height, centered in visibleFrame, including a visibleFrame with
      negative origin `(-1920, -200, 1920, 1055)`: frame lies entirely inside it.
    - `cascaded`: no existing origins → unchanged; one identical top-left → shifted (+24, −24);
      two stacked → shifted twice; shifting stops before leaving visibleFrame.
  - `SnapperTests/AnnotationToolTests.swift`
    - `v/r/a` and `V/R/A` map to select/rectangle/arrow; `t` → `nil` when `textToolEnabled` is false
      and `.text` when true; `x`, `""`, `"rr"` → `nil`; any key with Command/Option/Control → `nil`.
    - `segmentIndex` ↔ `init?(segmentIndex:)` round-trip for all cases; out-of-range → `nil`.
  - `SnapperTests/ShapeRendererTests.swift` (CoreGraphics bitmap contexts, no window needed)
    - Orientation: create a 4×4 RGBA image whose top two rows are red and bottom two blue; make a
      4×4 bitmap context, set the CTM to y-down image space (translate `(0, 4)`, scale `(1, −1)`),
      call `drawImage`; read back the buffer and assert the image's top rows are red in the same
      place the source had them (catches the classic flipped-image bug).
    - Rectangle: 100×100 transparent context in y-down image space; draw a red rectangle shape
      `(10, 10, 50, 30)` with stroke S at ppp 1 (2 px); assert a pixel on the top edge near
      `(30, 10)` is opaque red, the center `(35, 25)` is transparent, and that the shape appears near
      the **top** of the image (row ~10 from the top in the buffer).
    - Stroke width scales with pixelsPerPoint: same rectangle at ppp 2 with M → edge 8 px thick
      (pixel 3 px outside the path line is filled at ppp 2, not at ppp 1 with S).
    - Arrow: horizontal arrow `(10, 50) → (90, 50)`, color blue, M; assert a pixel on the shaft is
      blue, a pixel inside the arrowhead triangle just before the tip is blue, and a pixel past the
      tip `(95, 50)` is transparent.
- **Build check:** `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`,
  `xcodebuild -scheme Snapper test` all succeed.
- **Manual verification** (real GUI session, Screen Recording permission granted):
  1. Click another app (e.g. Finder) so it's frontmost. Press ⌘⇧6, drag a ~600×400 pt rectangle.
     Expected: shutter sound (with the setting on), an annotation window titled
     `Screenshot <date> at <time>` appears in front and is key; no new file in the save folder; the
     clipboard is unchanged (paste in TextEdit shows the previous contents).
  2. Turn *Play shutter sound* off in Settings; repeat: no sound. Set destination to *Save to file*:
     still no file written.
  3. ⌘⇧6, press Space, click a partially covered window. Expected: the editor shows only that
     window, no shadow, nothing covering it.
  4. ⌘⇧6 and select (almost) the whole screen. Expected: the window is about 80% of the screen and
     centered on it; the image is scaled down to fit, aspect preserved.
  5. ⌘⇧6 and select a ~100×100 pt area. Expected: window at its minimum size, the image at 100%
     (not blurry/upscaled) centered with margins.
  6. With two displays, capture on the secondary one (also try one with a different scale or
     arranged left of/below the main one). Expected: the editor opens on that display.
  7. Without closing, do two more ⌘⇧6 captures. Expected: three independent windows, offset from
     each other; drawing in one doesn't affect the others; closing one leaves the others working.
  8. Rectangle tool (selected by default): drag → red medium outline rectangle. Drag again holding
     Shift → square; press/release Shift mid-drag without moving → preview switches square/free.
     Tool stays Rectangle after each shape.
  9. Press A, drag → arrow with filled head at the end; with Shift the angle snaps to 0/45/90°.
     Press V → Select segment highlighted; press R → Rectangle. Press T → nothing; the Text
     segment is greyed out. A single click with Rectangle/Arrow creates nothing.
  10. Drag starting in the grey margin → nothing. Drag from inside the image to outside the window →
      shape ends at the image edge.
  11. Resize the window smaller and larger. Expected: image and shapes scale together and stay
      aligned with the same image content; enlarging past the natural size leaves the image at 100%
      centered.
  12. Confirm there is still no Dock icon and the app doesn't appear in ⌘Tab (expected with
      `.accessory`).

## Out of scope

- Selecting, moving, resizing, deleting shapes; selection handles; color swatches / color panel;
  S/M/L control; applying color/width to the selection; remembering last-used color/width; ⌘Z/⇧⌘Z
  menu wiring; Esc deselect — **0018-annotation-editing**.
- Text tool behavior and text rendering — **0019-annotation-text-tool**.
- Flattened export, ⌘S save panel, ⌘C copy, "Discard changes?" on close, quit prompt —
  **0020-annotation-export** (it reuses `ShapeRenderer` and `AnnotationWindowManager.controllers`).
- Shape model, constraints, arrowhead geometry, hit-testing — **0015**; document/undo/dirty — **0016**.
- Capture itself, shutter sound, alerts — **0008**; window mode/hit-testing — **0009**.
- Zoom controls (pinch, ⌘+/⌘−) — not in the PRD.
