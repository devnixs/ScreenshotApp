# 0018 — Add shape selection, editing, color/stroke controls, undo/redo and delete to the annotation editor

> Source: docs/PRD.md §8.2 (Toolbar: Select/Move, Color, Stroke width, Undo/Redo, Delete, and the bullets below the table), §8.4 (shapes stored in image coordinates), §12 ("⌘⇧6 opens the annotation editor, where all tools, shortcuts, undo/redo, delete, and recoloring work")
> Depends on: 0001-xcodegen-app-shell, 0003-app-settings, 0015-annotation-shape-model, 0016-annotation-document-undo, 0017-annotation-window-canvas

## Context

No app code exists yet. When this spec is implemented, the lower-numbered siblings provide:

- **0001**: `NSApp.mainMenu` with a standard Edit menu (Undo `undo:` ⌘Z, Redo `redo:` ⇧⌘Z, Cut/Copy/Paste, Delete `delete:`, Select All), so key equivalents route through the responder chain of the key window.
- **0003**: `AppSettings` (typed, injected `UserDefaults`) with `annotationColor: RGBAColor` (default red) and `annotationStrokeWidth: StrokeWidth` (default `.m`); pure `Annotate/Model/RGBAColor.swift` with the 8 presets (red, orange, yellow, green, blue, purple, black, white) and `Annotate/Model/StrokeWidth.swift` (`.s/.m/.l` → 2/4/8 image points; text 16/24/36 pt).
- **0015**: `Annotate/Model/AnnotationShape.swift` (rectangle / arrow / text; `id`, `color`, `strokeWidth`), `ShapeHandle`, shape and handle hit-testing, move, resize by handle, Shift constraints (square, 45° snap). Pure.
- **0016**: `Annotate/Model/AnnotationDocument.swift` (image pixel size, `pixelsPerPoint`, ordered `shapes`, selection, undoable mutations, `undo()/redo()`, `canUndo/canRedo`, dirty flag). Pure; no Foundation `UndoManager`.
- **0017**: `AnnotationWindowController`, `AnnotationWindowManager`, `AnnotationCanvasView`, pure `CanvasTransform` (image pixels ↔ view points) and `AnnotationWindowSizing`, `AnnotationTool` (`.select/.rectangle/.arrow/.text`, shortcuts V/R/A/T), `ShapeRenderer` (draws shapes in image coordinates into a `CGContext`; reused by export), the toolbar with the tool selector, and the rectangle and arrow drawing tools.

### Boundary with 0017 (explicit)

The 0017 spec was being written in parallel and was not available when this spec was written. The boundary is therefore defined as follows. If 0017 as implemented differs, the implementer keeps this division and adapts names, without duplicating anything:

- **0017 owns:** the window, the canvas, `CanvasTransform`, `ShapeRenderer`, the tool selector (V/R/A/T, including the V button and shortcut), and drawing new rectangles and arrows. New shapes use a **per-window current style** (color + stroke width) that 0017 seeds from `AppSettings` when the window opens. Adding a shape goes through the document's undoable add operation. In 0017 the Select tool can be chosen but does nothing on the canvas.
- **0018 (this spec) owns:** everything the Select tool does on the canvas (selecting, moving, resizing, selection highlight and handles); the color swatches, the "+" color-panel button and the S/M/L stroke control in the toolbar; apply-to-selection; remembering color and stroke width in `AppSettings`; wiring Undo/Redo to the Edit menu; Delete/⌫; Esc to deselect.
- If 0017 already added color or stroke controls (without apply-to-selection), this spec **extends those controls** instead of adding a second set. If 0017 already wired `undo:`/`redo:`, this spec keeps that wiring and adds only the validation and drag guard described below.
- **0019** (text tool) and **0020** (export, ⌘S/⌘C, discard prompts) build on this spec. Text shapes, once they exist, are selectable, movable, recolorable and resizable by stroke width through the generic paths here. They have no resize handles.

### PRD behavior this spec delivers (§8.2)

- Select/Move (V): "Click a shape to select it, drag the shape to move it, drag its handles to resize it."
- Color: "Preset swatches (red, orange, yellow, green, blue, purple, black, white), plus a '+' button that opens the system color picker."
- Stroke width: "S / M / L = 2 / 4 / 8 image points."
- Undo / Redo ⌘Z / ⇧⌘Z: "Covers every edit, including moves, resizes, color changes, and text edits."
- Delete ⌫: "Deletes the selected shape."
- "The last-used color is remembered between sessions. The initial default is red." Same for stroke width, default M.
- "Esc, when no text box is being edited, deselects the selected shape. It never closes the window."
- "Changing the color or stroke width while a shape is selected applies the change to that shape."
- "Resize handles: the four corners of a rectangle and the two endpoints of an arrow."

## User story

As a Snapper user annotating a capture, I can press V, click a shape to select it, drag it or its handles to move or resize it, recolor it or change its stroke from the toolbar, delete it with ⌫, and undo or redo any of those steps with ⌘Z / ⇧⌘Z. My last color and stroke choice is there the next time I annotate.

## Design decisions

The PRD doesn't spell these out. They are UI details that follow from §8.2, not new product behavior. They are listed here so the implementer doesn't have to re-derive them.

1. **Selection exists only in the Select tool.** Switching to Rectangle, Arrow or Text clears the selection. A newly drawn shape is **not** selected. (Otherwise picking a new color to draw the *next* shape would recolor the shape you just drew, because changing the color applies to the selection.)
2. **The toolbar shows the selected shape's style.** While a shape is selected, the swatch highlight and the S/M/L control show that shape's color and stroke width. When nothing is selected, they show the window's current style (used for new shapes).
3. **An explicit pick always updates the current style.** Clicking a swatch, picking in the color panel, or clicking S/M/L (a) applies that one attribute to the selected shape, if there is one (one undo step), (b) updates that attribute of the window's current style, and (c) writes it to `AppSettings` (`annotationColor` / `annotationStrokeWidth`). Merely *selecting* a shape never changes the current style or the settings. Undo/redo changes shapes only. It never reverts the current style or the stored setting.
4. **Windows are independent.** Each annotation window keeps its own current style, seeded from `AppSettings` when it opens. A pick in one window updates the stored setting (so the *next* window starts with it) but does not change other open windows.
5. **Shift during resize applies the 0015 constraints**, the same as when drawing: a rectangle becomes a square anchored at the opposite corner, and an arrow's dragged endpoint snaps to 45° around the fixed endpoint. Shift while *moving* does nothing (no axis lock). Pressing or releasing Shift mid-drag updates the preview immediately (`flagsChanged`).
6. **One drag = one undo step.** During a move or resize, the canvas previews the change without touching the document. On mouse-up it commits **one** undoable update to the document. A drag that ends with the geometry unchanged (a plain click) commits nothing and doesn't set the dirty flag. This needs no coalescing support in 0016.
7. **Drag points are clamped to the image bounds** (in image pixels) before they are applied. The grabbed point therefore never leaves the image, so a moved shape always overlaps the image and stays selectable. This is the same rule 0017 uses when drawing, if it clamps there.
8. **The color panel is non-continuous.** `NSColorPanel.shared.isContinuous = false`, so the panel sends its action once per completed pick (mouse-up on the wheel or slider, Return in a field) and each pick is exactly one undo step. Alternatives were rejected: continuous updates would flood the undo stack, and a debounce timer adds timing-dependent logic that is hard to test. The trade-off is that there is no live preview while dragging inside the panel, which is acceptable for v1. `showsAlpha = false`, and picked colors get alpha 1.0 because shapes are opaque.
9. **No same-value steps.** A pick that equals the selected shape's existing value doesn't create an undo step. It still updates the current style and the setting.
10. **Esc during an active move or resize** aborts the drag (the shape keeps its pre-drag geometry, nothing is committed) and deselects.

## Scope & changes

### Pure logic (unit-tested, no AppKit drawing, no `@MainActor` requirement)

#### 1. `Snapper/Annotate/Model/ShapeStyle.swift` (pure)

```swift
struct ShapeStyle: Equatable, Sendable {
    var color: RGBAColor
    var strokeWidth: StrokeWidth
}
```

If 0017 already introduced an equivalent "current style" value type, reuse it instead and skip this file.

#### 2. `Snapper/Annotate/Model/ToolbarStyleState.swift` (pure reducer)

```swift
struct ToolbarStyleState: Equatable, Sendable {
    private(set) var currentStyle: ShapeStyle        // used for new shapes
    private(set) var selectedShapeStyle: ShapeStyle? // nil when nothing is selected

    init(currentStyle: ShapeStyle)

    /// What the toolbar shows: the selected shape's style, else the current style.
    var displayedStyle: ShapeStyle { get }

    /// Which swatch is highlighted: index into RGBAColor's preset list, or .custom.
    var swatchSelection: SwatchSelection { get }   // enum SwatchSelection { case preset(Int), custom }

    mutating func reduce(_ event: ToolbarStyleEvent) -> ToolbarStyleEffects
}

enum ToolbarStyleEvent: Equatable, Sendable {
    /// Sent after every selection change AND after every document change (edit, undo, redo),
    /// with the selected shape's style, or nil when nothing is selected.
    case selectedShapeStyleChanged(ShapeStyle?)
    case colorPicked(RGBAColor)        // swatch or color panel
    case strokeWidthPicked(StrokeWidth)
}

struct ToolbarStyleEffects: Equatable, Sendable {
    var applyColorToSelection: RGBAColor?          // non-nil → one undoable recolor of the selected shape
    var applyStrokeWidthToSelection: StrokeWidth?  // non-nil → one undoable stroke change of the selected shape
    var persistColor: RGBAColor?                   // non-nil → write AppSettings.annotationColor
    var persistStrokeWidth: StrokeWidth?           // non-nil → write AppSettings.annotationStrokeWidth
}
```

Rules (decisions 2, 3 and 9):
- `.selectedShapeStyleChanged(s)` sets `selectedShapeStyle = s`. It has no effects and never changes `currentStyle`.
- `.colorPicked(c)` sets `currentStyle.color = c` and `persistColor = c`. If a shape is selected and its color ≠ `c`, it sets `applyColorToSelection = c` and `selectedShapeStyle?.color = c`.
- `.strokeWidthPicked(w)` works the same way for the stroke width.
- `swatchSelection` is `.preset(i)` when `displayedStyle.color` is exactly equal to the i-th preset (in the order of 0003's preset list), otherwise `.custom`.

#### 3. `Snapper/Annotate/Model/ShapeDragSession.swift` (pure)

This is the preview-and-commit logic for a single move or resize drag (decisions 5–7).

```swift
struct ShapeDragSession: Sendable {
    enum Kind: Equatable, Sendable { case move, resize(ShapeHandle) }

    init(original: AnnotationShape, kind: Kind, startPoint: CGPoint, imageBounds: CGRect)

    /// Preview for the current pointer position (image pixels). Always computed from
    /// `original` + total delta (never cumulative, so there is no drift). The point is
    /// clamped to `imageBounds` first. `shiftHeld` applies the 0015 resize constraints;
    /// it is ignored for `.move`.
    func preview(at point: CGPoint, shiftHeld: Bool) -> AnnotationShape

    /// The shape to commit, or nil if the result equals `original` (no undo step).
    func finish(at point: CGPoint, shiftHeld: Bool) -> AnnotationShape?
}
```

It calls 0015's move and resize-by-handle functions. It doesn't re-implement the geometry.

#### 4. `Snapper/Annotate/CanvasHitTester.swift` (pure, next to `CanvasTransform`)

```swift
enum CanvasHit: Equatable { case handle(shapeID: AnnotationShape.ID, ShapeHandle), shape(AnnotationShape.ID), none }

enum CanvasHitTester {
    static let handleHitRadiusInViewPoints: CGFloat = 6   // handles are drawn 8×8 pt
    static let shapeHitToleranceInViewPoints: CGFloat = 4 // slop for thin outlines and arrows

    /// Converts a view-point distance to image pixels using CanvasTransform's scale,
    /// so handles keep the same on-screen size at any zoom.
    static func imagePixels(fromViewPoints: CGFloat, transform: CanvasTransform) -> CGFloat

    /// Order: (1) the handles of the currently selected shape, (2) the topmost shape
    /// containing the point (0015/0016 hit-testing, topmost = last in z-order), (3) .none.
    static func hitTest(_ imagePoint: CGPoint, in document: AnnotationDocument, transform: CanvasTransform) -> CanvasHit
}
```

The conversion divides by `CanvasTransform`'s view-points-per-image-pixel scale (whatever 0017 names it). If 0015's hit-tests take a tolerance parameter, pass the converted values. If they use a fixed tolerance, add an overload with a tolerance parameter in 0015's file, keeping the existing behavior as the default.

#### 5. `Snapper/Annotate/CanvasKeyCommand.swift` (pure key mapping)

```swift
enum CanvasKeyCommand: Equatable { case deleteSelection, deselect }

extension CanvasKeyCommand {
    /// keyCode 51 (⌫) and 117 (forward delete, including fn-⌫) → .deleteSelection;
    /// keyCode 53 (Esc) → .deselect. Returns nil if ⌘, ⌥ or ⌃ is held.
    /// Shift, fn and numericPad flags are ignored.
    init?(keyCode: UInt16, modifiers: NSEvent.ModifierFlags)
}
```

If 0017 already introduced a key-mapping type for V/R/A/T, add these cases to it instead of creating a new type. (`NSEvent.ModifierFlags` is a plain option set, so this stays testable.)

#### 6. `RGBAColor` ↔ `NSColor` bridge: `Snapper/Annotate/RGBAColor+AppKit.swift`

Add `init(nsColor:)`: convert with `usingColorSpace(.sRGB)`, take the components, and force alpha to 1.0. If the conversion fails (for example, a pattern color), fall back to the current color. Also add `var nsColor: NSColor` if 0017 doesn't already provide it. These use AppKit but are deterministic, so they are unit-tested.

### Platform glue (manually verified)

#### 7. Select tool on `AnnotationCanvasView` (`Snapper/Annotate/AnnotationCanvasView.swift`)

When the active tool is `.select`:
- **mouseDown:** convert the point to image pixels with `CanvasTransform`, then call `CanvasHitTester.hitTest`.
  - `.handle(id, h)`: start a `ShapeDragSession(kind: .resize(h))`.
  - `.shape(id)`: select it in the document (if it isn't already selected) and start a `.move` session.
  - `.none`: clear the selection.
  - Selection changes are not undo steps. That follows 0016's rule: if 0016 makes selection part of undo state, follow 0016.
- **mouseDragged / flagsChanged:** store `session.preview(...)` as a transient `dragPreview: AnnotationShape?`. While it is set, the canvas renders it in place of the document's shape with the same id. The document isn't touched during the drag.
- **mouseUp:** if `session.finish(...)` returns a shape, apply it with the document's single undoable update operation (0016 naming). Then clear the session and the preview.
- **Selection highlight** (drawn in view space after `ShapeRenderer` has drawn all shapes; never part of `ShapeRenderer` or the export):
  - Rectangles: 8×8 pt white squares with a 1 pt system-accent border at the four corners.
  - Arrows: 8 pt circles in the same style at both endpoints.
  - Text (once 0019 exists): a 1 pt dashed bounding box and no handles.
  - The sizes stay constant in view points at any zoom.
- Use the arrow cursor. No special hover cursors in v1.
- **Switching away from `.select`** clears the selection (decision 1) and aborts any drag.
- The canvas must be first responder while the window is key (`acceptsFirstResponder = true`). 0017 most likely already does this; verify it.

#### 8. Keys and Edit-menu actions (on `AnnotationCanvasView`, the first responder)

- `keyDown`: map the key with `CanvasKeyCommand`.
  - `.deleteSelection`: remove the selected shape through the document's undoable remove operation, and clear the selection. Does nothing if nothing is selected.
  - `.deselect`: abort any drag (decision 10) and clear the selection.
  - All other keys keep 0017's handling (tool shortcuts) or go to `super`.
- `cancelOperation(_:)`: same as `.deselect`. This guarantees Esc never closes the window, whatever the window or controller would do by default.
- `@objc func delete(_:)` (Edit ▸ Delete): same as `.deleteSelection`.
- `@objc func undo(_:)` and `@objc func redo(_:)`: call `document.undo()` / `document.redo()`. Then refresh the selection highlight, push `.selectedShapeStyleChanged` to the toolbar state, and redraw. Because these are implemented on the canvas, they are found before `NSWindow`'s own `undo:` in the responder chain. Foundation `UndoManager` is not used.
- `validateMenuItem(_:)` / `NSMenuItemValidation`:
  - `undo:` is enabled iff `document.canUndo` and no drag is in progress.
  - `redo:` is enabled iff `document.canRedo` and no drag is in progress.
  - `delete:` is enabled iff a shape is selected and no drag is in progress.
- ⌫/⌘Z pressed during a drag do nothing.
- While a text box is being edited (0019), the text view is first responder. 0019 decides how undo, ⌫ and Esc route then. This spec only ensures that the canvas versions don't act on shapes while a text edit is active, if 0019 exposes that state.

#### 9. Toolbar style controls (in 0017's toolbar, `AnnotationWindowController`)

- **Swatches:** 8 small circular buttons in the preset order from 0003 (red, orange, yellow, green, blue, purple, black, white). Each is filled with the exact `RGBAColor` preset and has a tooltip with the color name ("Red", …). The white swatch has a thin gray border so it is visible. The highlighted swatch follows `swatchSelection`.
- **"+" button:** shows a ring highlight when `swatchSelection == .custom`. Clicking it:
  1. sets `NSColorPanel.shared.showsAlpha = false` and `isContinuous = false`;
  2. sets the panel's color to `displayedStyle.color` while a flag suppresses the resulting action (if any);
  3. sets the panel's `target` to this window controller and its `action` to `colorPanelDidPick(_:)`;
  4. calls `orderFront(nil)`.
  
  `colorPanelDidPick` sends `.colorPicked(RGBAColor(nsColor: panel.color))`.
- **Color panel retargeting:** when an annotation window becomes key while the panel is visible, the panel is retargeted to that window's controller and its color is set to that window's `displayedStyle.color` (with the action suppressed). When a window closes and the panel targets its controller, the panel's target is set to `nil`.
- **Stroke control:** an `NSSegmentedControl` with the segments "S", "M", "L" (select-one). Its selection follows `displayedStyle.strokeWidth`. Changing it sends `.strokeWidthPicked`.
- **Effects:** the controller owns the `ToolbarStyleState` (seeded from `AppSettings` at window open, decision 4) and executes each `ToolbarStyleEffects`:
  - apply to the selected shape through the document's undoable update (one step per pick);
  - write `AppSettings.annotationColor` / `.annotationStrokeWidth`;
  - redraw;
  - refresh the controls from `displayedStyle`.
- New shapes drawn by 0017's tools read `toolbarStyleState.currentStyle`. If 0017 holds the current style elsewhere, replace that with this state so there is a single source.
- After every document change (edit, undo, redo) and every selection change, send `.selectedShapeStyleChanged(selectedShape?.style)`.

#### 10. No `project.yml` target changes

Only new source files in existing folders and new test files are added. Run `xcodegen generate` after adding them.

No PRD update is needed: this spec doesn't change product behavior. The design decisions above are UI details below the PRD's level.

## Acceptance criteria

- [ ] With the Select tool (V), clicking a shape selects it and shows its handles (4 corners for a rectangle, 2 endpoints for an arrow). Clicking empty canvas deselects.
- [ ] Dragging a selected or unselected shape's body moves it. Dragging a handle resizes it. Shift while resizing makes a rectangle square or snaps an arrow to 45°.
- [ ] Handles have the same on-screen size, and are equally easy to grab, at every window size or zoom.
- [ ] Each move or resize is exactly one undo step. A click without movement creates no undo step and doesn't mark the window as changed.
- [ ] ⌫, forward delete and Edit ▸ Delete delete the selected shape. This is undoable.
- [ ] Clicking a preset swatch, picking in the color panel ("+"), or clicking S/M/L while a shape is selected changes that shape (one undo step per pick) and becomes the style for new shapes.
- [ ] With no selection, those controls change only the style for new shapes.
- [ ] Selecting a shape makes the toolbar show its color and stroke width. Deselecting shows the current style again.
- [ ] ⌘Z / ⇧⌘Z (and Edit ▸ Undo/Redo) undo and redo moves, resizes, color changes, stroke changes, deletes and drawn shapes. The menu items are disabled when there is nothing to undo or redo.
- [ ] Esc deselects (and aborts an in-progress drag). It never closes the window.
- [ ] The last-used color and stroke width survive quitting and relaunching Snapper. A fresh install starts with red and M.
- [ ] Two annotation windows keep independent selections, undo histories and current styles.
- [ ] Selection highlights and handles never appear in exported output (verified with 0020 once it exists; the code path keeps them out of `ShapeRenderer`).

## Edge cases

- **Overlapping shapes:** a click picks the topmost one. A handle of the *selected* shape wins over a shape body above it.
- **Handles at the image edge or outside the visible area:** they are still hit-testable within the clamped image bounds.
- **Tiny shapes** where handles overlap the body: handles win (resize), per the hit order.
- **Very high or very low zoom:** the hit radius scales inversely with `CanvasTransform`'s scale. At a 0.25 zoom the radius is 24 image px, at 2× it is 3 image px.
- **Resizing past the opposite corner** (flipping a rectangle): 0015's resize rule decides normalization. The preview and the commit use the same function, so they agree.
- **Undo while a shape is selected** that removes or changes that shape: the selection follows 0016 (cleared if the shape no longer exists), and the toolbar refreshes from the new selection.
- **A color panel pick after its window closed:** the target is `nil`, so nothing happens. No crash.
- **A non-sRGB or pattern color from the panel:** converted to sRGB. If the conversion fails, the pick is ignored.
- **Picking the same color as the selected shape's color:** no undo step, but the setting is still written.
- **Shift pressed or released without mouse movement mid-resize:** the preview updates via `flagsChanged`.
- **Switching tools mid-drag** (for example, pressing R while dragging): the drag is aborted, nothing is committed, and the selection is cleared.
- **⌘⌫ or ⌥⌫:** not handled by the canvas (`CanvasKeyCommand` returns nil).

## Test plan

- **Unit tests**
  - `SnapperTests/ToolbarStyleStateTests.swift`
    - Initial `displayedStyle == currentStyle`. `swatchSelection == .preset(0)` for red.
    - `.selectedShapeStyleChanged(blue/L)` → `displayedStyle == blue/L`, `currentStyle` unchanged, effects empty.
    - `.selectedShapeStyleChanged(nil)` after that → `displayedStyle == currentStyle`.
    - `.colorPicked(green)` with no selection → `currentStyle.color == green`, `persistColor == green`, `applyColorToSelection == nil`.
    - `.colorPicked(green)` with a blue shape selected → `applyColorToSelection == green`, `persistColor == green`, `selectedShapeStyle.color == green`, `currentStyle.color == green`, stroke unchanged.
    - `.colorPicked(blue)` with a blue shape selected → `applyColorToSelection == nil`, `persistColor == blue`.
    - The same set of cases for `.strokeWidthPicked` (including no-op on an equal width). A stroke pick never changes the color, and the reverse.
    - `swatchSelection` is `.preset(i)` for each of the 8 presets in order, and `.custom` for a non-preset color (for example, r 0.5 g 0.5 b 0.5).
  - `SnapperTests/ShapeDragSessionTests.swift`
    - Moving a rectangle by (10, −5) → origin shifted by exactly that. The size is unchanged.
    - Moving an arrow → both endpoints shifted.
    - `finish` at the start point → `nil`. A move and a move back → `nil`.
    - Resizing a rectangle by each of the 4 corner handles → the opposite corner stays fixed.
    - Resize with `shiftHeld` → the result is square (anchored at the opposite corner).
    - Resizing an arrow endpoint with `shiftHeld` → the angle is a multiple of 45° and the other endpoint is fixed.
    - Toggling Shift between two previews at the same point gives constrained, then unconstrained geometry, both computed from `original` (no drift across 100 incremental previews).
    - A point outside `imageBounds` is clamped. For example, dragging a move to (−50, −50) on a 100×100 image leaves the grabbed point at (0, 0).
    - `.move` ignores `shiftHeld`.
  - `SnapperTests/CanvasHitTesterTests.swift`
    - `imagePixels(fromViewPoints: 6, transform:)` = 6 at scale 1, 12 at scale 0.5 (image shown at half size), and 3 at scale 2.
    - A point 5 view-pt from a selected rectangle's corner at scale 0.5 → `.handle(id, corner)`. 7 view-pt away → not a handle.
    - A handle of the selected shape beats an overlapping, higher-z shape body.
    - With two overlapping unselected shapes, the topmost (last) one is returned.
    - Handles of an *unselected* shape are not hit (only its body is).
    - Empty area → `.none`.
  - `SnapperTests/CanvasKeyCommandTests.swift`
    - Key code 51 → `.deleteSelection`. 117 with `.function` → `.deleteSelection`. 53 → `.deselect`.
    - 51 with `.command` or `.option` → nil. 51 with `.shift` → `.deleteSelection`. An unrelated key code (for example, 9 "V") → nil.
  - `SnapperTests/RGBAColorAppKitTests.swift`
    - Each preset round-trips `RGBAColor → nsColor → RGBAColor(nsColor:)` within 1e-6.
    - A Display P3 color converts to sRGB components (not equal to the raw P3 components). Alpha is always 1.0, even for an input with alpha 0.3.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` succeed with all existing and new tests green.
- **Manual verification** (needs a real GUI session with Screen Recording permission; can't be automated):
  1. Press ⌘⇧6 and capture an area. In the annotation window, draw two rectangles (R) and one arrow (A), with the second rectangle overlapping the first. Expected: no shape shows handles after it is drawn.
  2. Press V. Click the first rectangle. Expected: 4 corner handles. Click the arrow. Expected: 2 endpoint handles, and the rectangle's handles disappear. Click empty canvas. Expected: no handles.
  3. Click the overlap area of the two rectangles. Expected: the one drawn last is selected.
  4. Drag a rectangle's body. Expected: it moves smoothly. Press ⌘Z once. Expected: it jumps back to where it was. Press ⇧⌘Z. Expected: it moves again.
  5. Drag each of the 4 corner handles in turn. Expected: the opposite corner stays put. Undo all 4 with 4 presses of ⌘Z.
  6. Drag a corner handle while holding Shift. Expected: it stays a square. Release Shift mid-drag without moving. Expected: it snaps back to a free rectangle.
  7. Drag each arrow endpoint. Then drag one with Shift. Expected: the angle snaps to 0°/45°/90°… around the other endpoint.
  8. Resize the window much smaller and larger (or zoom, if 0017 supports it). Expected: handles stay the same on-screen size and are equally easy to grab.
  9. Click a shape without moving the mouse. Expected: Edit ▸ Undo still undoes the previous real edit (the click added no step).
  10. Select a shape and press ⌫. Expected: it is deleted. ⌘Z brings it back. Repeat with fn-⌫ (forward delete) and with Edit ▸ Delete.
  11. Select a red rectangle and click the blue swatch. Expected: the rectangle turns blue and the blue swatch is highlighted. ⌘Z → red again. ⇧⌘Z → blue.
  12. With the rectangle selected, click "+". The system color panel opens showing blue. Drag around the color wheel. Expected: the shape changes once, when you release the mouse. ⌘Z reverts exactly that one pick. The "+" button is highlighted (custom color).
  13. With a shape selected, click L. Expected: its stroke thickens and L is highlighted. ⌘Z reverts it.
  14. Select a green M shape, then a red S shape. Expected: the toolbar follows each selection. Deselect (Esc). Expected: the toolbar shows the last style you *picked*, not the selected shape's style.
  15. With nothing selected, pick yellow and S, then draw a rectangle. Expected: yellow, thin.
  16. Press Esc with a shape selected. Expected: it deselects and the window stays open. Press Esc again with nothing selected. Expected: nothing happens and the window stays open. Start dragging a shape and press Esc before releasing. Expected: the shape snaps back and is deselected.
  17. With nothing to undo (fresh window), open the Edit menu. Expected: Undo and Redo are disabled, and Delete is disabled with no selection.
  18. Open a second annotation window (⌘⇧6 again). Expected: it starts with the last-picked color and stroke. Select a shape in each window. Undo in one window doesn't affect the other. With the color panel open, click into the other window and pick a color. Expected: only that window's selected shape changes.
  19. Pick purple and L, then quit Snapper (menu ▸ Quit) and relaunch. Press ⌘⇧6. Expected: the toolbar shows purple and L, and a new rectangle is purple and thick.
  20. Fresh defaults check (optional): `defaults delete com.thera-engineering.snapper`, relaunch, press ⌘⇧6. Expected: red and M.

## Out of scope

- Window, canvas, `CanvasTransform`, `ShapeRenderer`, the tool selector, and rectangle/arrow drawing: **0017**.
- The shape model's geometry (hit-testing, move, resize, Shift constraints): **0015**. The document, the undo stack, selection storage and the dirty flag: **0016**. This spec only calls them.
- Text placement, editing, double-click re-edit, text undo granularity, and key routing while a text box is being edited: **0019**.
- Export, ⌘S, ⌘C, "Discard changes?" and the quit prompt: **0020**.
- Persisting the settings type itself and the preset definitions: **0003**.
- Marquee or multi-selection, copy/paste or duplication of shapes, arrow-key nudging, z-order changes, hover cursors, and live color preview while dragging in the color panel. None of these are in PRD §8.2.
