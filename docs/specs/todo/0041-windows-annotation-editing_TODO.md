# 0041 — Windows: select, move, resize, delete, restyle and undo annotation shapes

> Source: docs/PRD.md §8.2 (Select/Move, Color, Stroke width, Undo/Redo, Delete, and the bullets on rectangle outline hits, Esc, apply-to-selection, and resize handles), §8.4 (shapes in image coordinates), §12 ("all tools, shortcuts, undo/redo, delete, and recoloring work"), §13.2 (Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z, Delete or Backspace, points → DIPs), §13.7 (section 8 applies; tool keys and Esc work the same)
> Depends on: 0025-windows-solution-tray-shell, 0027-windows-app-settings, 0028-windows-display-geometry, 0038-windows-annotation-shape-model, 0039-windows-annotation-document-undo, 0040-windows-annotation-window-canvas

## Context

The `windows/` app doesn't exist yet in this checkout. When this spec is implemented, the lower-numbered Windows specs provide:

- **0027**: `Snapper.Core.Settings.AppSettings` with the last-used annotation color and stroke width, plus `Annotate/Model/RgbaColor` (8 presets in the order red, orange, yellow, green, blue, purple, black, white) and `Annotate/Model/StrokeWidth` (S/M/L = 2/4/8 image points). `Support/KeyChord` defines the modifier set (Ctrl/Alt/Shift/Win) and virtual-key representation used below.
- **0038**: the pure shape model in `Snapper.Core.Annotate.Model`: `AnnotationShape` (rectangle / arrow / text, `Id`, `Color`, `StrokeWidth`), `ShapeHandle` (TopLeft/TopRight/BottomLeft/BottomRight/Start/End + `Opposite`), `Handles`, `HandleHitTest(point, radius)`, `HitTest(point, pixelsPerDip, slop)` (rectangles hit only near the outline), `Moved`, `Resized(handle, point, constrained)`, `WithColor`, `WithStrokeWidth`, and **`ShapeDragSession`** (port of `Snapper/Annotate/Model/ShapeDragSession.swift:3-27`). The table assigns drag sessions to 0038.
- **0039**: `AnnotationDocument` (ordered shapes, last = topmost, `SelectedShapeId`/`SelectedShape`, `TopmostShape(at, tolerance)`, undoable `Add`/`Delete`/`Replace`, `Select` that is not an undo step, `Undo`/`Redo`, `CanUndo`/`CanRedo`, `IsDirty`, `MarkClean`), port of `Snapper/Annotate/Model/AnnotationDocument.swift:6-143`.
- **0040**: `AnnotationWindow`, `AnnotationCanvas`, `ShapeRenderer` (shared with export), `AnnotationWindowManager`, Core `Annotate/CanvasTransform` (image pixels ↔ canvas DIPs; `Scale` = DIPs per image pixel, never upscaling), `Annotate/AnnotationTool` (Select/Rectangle/Arrow/Text, keys V/R/A/T), the drawing gesture state, the color swatches, the "+" button with `ChooseColor`, the S/M/L control, remembering the last color/width in `AppSettings`, and **`ToolbarStyleState`** (port of `Snapper/Annotate/Model/ToolbarStyleState.swift:1-51`, assigned to 0040 as "toolbar style"). In 0040 the Select tool can be chosen but does nothing on the canvas.

### macOS reference (port the behavior, don't redesign it)

- `Snapper/Annotate/CanvasHitTester.swift:3-25` and `SnapperTests/CanvasHitTesterTests.swift:5-28`: hit order handles of the selected shape → topmost shape → none, with radii fixed in view points (handle 6, shape slop 4).
- `Snapper/Annotate/CanvasKeyCommand.swift:3-13` and `SnapperTests/CanvasKeyCommandTests.swift:5-16`: Delete/Backspace → delete selection, Esc → deselect, nothing with Cmd/Option/Control held.
- `Snapper/Annotate/Model/ShapeDragSession.swift:3-27` and `SnapperTests/ShapeDragSessionTests.swift:5-68`: preview is computed from the original + total delta (no drift), the point is clamped to the image, Shift constrains resize only, and a click at the start point is never an edit.
- `Snapper/Annotate/Model/ToolbarStyleState.swift:1-51` and `SnapperTests/ToolbarStyleStateTests.swift:4-49`: the toolbar shows the selected shape's style, and an explicit pick applies to the selection (only when the value differs), updates the current style, and persists.
- `Snapper/Annotate/AnnotationCanvasView.swift:62-82` (selection adornments), `:97-111` (Select-tool mouse down), `:116-140` (drag preview and one commit on mouse up), `:141-194` (keys, Esc, delete, undo/redo guards, and deselect after undo when the tool isn't Select), and `:8-16` (switching tools commits text, aborts drags, and clears the selection).
- `Snapper/Annotate/AnnotationWindowController.swift:138-164` (`refreshStyle` and `pick`: picks are ignored during a drag, apply-to-selection is one `Replace`, and settings are written).
- `docs/specs/done/0018-annotation-editing_COMPLETED.md`, "Design decisions" 1–10. They all apply on Windows with the changes below.

### Windows differences from 0018

- "View points" are **canvas DIPs** (§13.2). Handle radius 6 DIPs, shape slop 4 DIPs, adornments 8×8 DIPs.
- There is no Edit menu or responder chain. Keys are handled by the annotation window (see "Keyboard routing"), and undo/redo/delete availability is a Core query instead of `validateMenuItem`.
- The color dialog is `ChooseColor` (0040). It's modal and returns once, so one OK is exactly one pick and one undo step. There is no panel retargeting between windows (0018 decision 8 and the `NSColorPanel` items don't apply).
- There's no `flagsChanged`. Shift changes mid-drag come from `KeyDown`/`KeyUp` of `LeftShift`/`RightShift`.
- **Hover cursors** are a Windows addition (macOS uses the arrow cursor only). They're a UI detail and don't change product behavior.
- There's no Y flip. Canvas DIPs and image pixels are both top-left, y-down.

### PRD behavior this spec delivers (§8.2 with §13.2)

- Select/Move (V): "Click a shape to select it, drag the shape to move it, drag its handles to resize it."
- "Rectangles are selected only near their outline (within half the stroke width plus 4 view points); their empty interior is not a hit."
- Undo / Redo (Ctrl+Z / Ctrl+Y, and Ctrl+Shift+Z): "Covers every edit, including moves, resizes, color changes, and text edits."
- Delete (Delete or Backspace): "Deletes the selected shape."
- "Esc, when no text box is being edited, deselects the selected shape. It never closes the window."
- "Changing the color or stroke width while a shape is selected applies the change to that shape."
- "Resize handles: the four corners of a rectangle and the two endpoints of an arrow."

## User story

As a Snapper user on Windows annotating a capture, I can press V, click a shape to select it, drag it or its handles to move or resize it, recolor it or change its stroke from the toolbar, delete it with Delete or Backspace, deselect it with Esc, and undo or redo any of those steps with Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z).

## Design decisions

These are the 0018 decisions, restated for Windows. They're UI details below the PRD's level.

1. **Selection exists only in the Select tool.** Switching to Rectangle, Arrow or Text aborts any drag and clears the selection. A newly drawn shape is not selected.
2. **The toolbar shows the selected shape's style** (`ToolbarStyleState.DisplayedStyle`). With no selection, it shows the window's current style.
3. **An explicit pick** (swatch, `ChooseColor` OK, S/M/L) applies that one attribute to the selected shape (one undo step, only if it differs), updates the window's current style, and writes `AppSettings`. Selecting a shape never changes the current style or settings. Undo/redo never reverts the current style or the setting.
4. **Windows are independent.** Each window has its own editor state, selection, history and current style.
5. **Shift during resize** applies the 0038 constraints (square anchored at the opposite corner; 45° snap around the fixed arrow endpoint). Shift while moving does nothing. Pressing or releasing Shift mid-drag updates the preview immediately.
6. **One drag = one undo step.** The canvas previews without touching the document, then commits one `Replace` on release. A drag that ends with unchanged geometry commits nothing and doesn't set the dirty flag.
7. **Drag points are clamped to the image bounds** in image pixels (inside `ShapeDragSession`), including points outside the window while the mouse is captured.
8. **Hit-testing uses the unclamped image point**, so a handle on the image edge can be grabbed from the margin just outside it.
9. **Esc during a move or resize** aborts the drag (pre-drag geometry, nothing committed) and deselects. **Losing mouse capture** mid-drag (Alt+Tab, another window taking capture, a modal dialog) aborts the drag the same way but keeps the selection.
10. **During a drag**, undo, redo, delete and style picks do nothing.
11. **After undo/redo**, if the current tool isn't Select, the selection is cleared (history snapshots carry the selection; `AnnotationCanvasView.swift:175-186`).

## Scope & changes

### Pure logic in `Snapper.Core` (xUnit-tested; no WPF, WinForms, WinRT or interop types)

#### 1. `windows/src/Snapper.Core/Annotate/CanvasHitTester.cs`: port of `CanvasHitTester.swift`

```csharp
public abstract record CanvasHit
{
    public sealed record Handle(Guid ShapeId, ShapeHandle Which) : CanvasHit;
    public sealed record Shape(Guid ShapeId) : CanvasHit;
    public sealed record None : CanvasHit;
}

public static class CanvasHitTester
{
    public const double HandleHitRadiusDips = 6;   // adornments are drawn 8×8 DIPs
    public const double ShapeHitToleranceDips = 4; // PRD §8.2: half the stroke + 4 view points

    /// DIPs → image pixels with CanvasTransform.Scale (DIPs per image pixel). 0 when Scale <= 0.
    public static double ImagePixels(double dips, CanvasTransform transform);

    /// Order: (1) handles of the selected shape, (2) topmost shape (document.TopmostShape with
    /// the converted slop), (3) None. Returns None when transform.Scale <= 0.
    public static CanvasHit HitTest(PointD imagePoint, AnnotationDocument document, CanvasTransform transform);
}
```

Use the `ShapeId` type that 0038 uses for `AnnotationShape.Id` (`Guid` above is a placeholder). If 0038's `HitTest`/`HandleHitTest` don't accept a tolerance, add an overload in 0038's file and keep the existing default.

#### 2. `windows/src/Snapper.Core/Annotate/CanvasKeyCommand.cs`: Windows key mapping

```csharp
public enum CanvasKeyCommand { DeleteSelection, Deselect, Undo, Redo }

public static class CanvasKeyMap
{
    /// virtualKey is a Win32 VK code; modifiers use 0027's KeyChord modifier flags.
    public static CanvasKeyCommand? Map(int virtualKey, KeyModifiers modifiers);
}
```

Rules:
- `VK_DELETE` (0x2E) and `VK_BACK` (0x08) → `DeleteSelection`. `VK_ESCAPE` (0x1B) → `Deselect`. Each returns null if Ctrl, Alt or Win is held. Shift is ignored.
- `Z` (0x5A) with exactly Ctrl → `Undo`. `Z` with exactly Ctrl+Shift → `Redo`. `Y` (0x59) with exactly Ctrl → `Redo`. Any combination with Alt or Win → null. Ctrl+Shift+Y → null.
- Everything else → null (tool keys V/R/A/T stay with 0040's mapping).

If 0040 already added a key-mapping type for tool keys, add these cases to it instead of creating a second type, and keep the file and tests named above.

#### 3. `windows/src/Snapper.Core/Annotate/CanvasCursor.cs`: hover cursor decision (Windows addition)

```csharp
public enum CanvasCursorKind { Arrow, Crosshair, Move, ResizeNwse, ResizeNesw, ResizeEndpoint }

public static class CanvasCursor
{
    public static CanvasCursorKind For(AnnotationTool tool, bool insideImage, CanvasHit hit, bool isDragging, CanvasHit? dragTarget);
}
```

- Rectangle/Arrow tool: `Crosshair` inside the image, `Arrow` outside (keep whatever 0040 does; this function just centralizes it).
- Text tool: whatever 0040 shows (`Arrow` until 0042 changes it).
- Select tool: `Handle` TopLeft/BottomRight → `ResizeNwse`; TopRight/BottomLeft → `ResizeNesw`; Start/End → `ResizeEndpoint`; `Shape` → `Move`; `None` → `Arrow`.
- While dragging, the cursor stays the one chosen at press time (`dragTarget`), so it doesn't flicker when the pointer passes over other shapes. After a rectangle flips past its opposite corner, the cursor still comes from the press-time handle. That's acceptable.

#### 4. `windows/src/Snapper.Core/Annotate/SelectionEditor.cs`: Select-tool state machine

This extracts the Select-tool logic that macOS keeps in `AnnotationCanvasView.swift:97-194`, so it's unit-testable. One instance per annotation window. It works on the window's `AnnotationDocument` instance. If 0039 made the document a value type, the window passes it by `ref`, and the signatures change accordingly.

```csharp
public sealed class SelectionEditor
{
    public SelectionEditor(AnnotationDocument document);

    public bool IsDragging { get; }
    public AnnotationShape? DragPreview { get; }       // rendered in place of the shape with the same id
    public CanvasHit? DragTarget { get; }              // the press-time hit, for the cursor

    /// Select tool only. Hit-tests the unclamped image point:
    /// Handle → start a Resize session on the selected shape;
    /// Shape → select it (not an undo step) and start a Move session;
    /// None → clear the selection. Returns true if a drag session started (the view captures the mouse).
    public bool PointerPressed(PointD imagePoint, CanvasTransform transform);

    /// Updates DragPreview from session.Preview(point, shift). No document change.
    public void PointerMoved(PointD imagePoint, bool shiftHeld);
    public void ShiftChanged(bool shiftHeld);          // re-previews at the last pointer point

    /// Commits session.Finish(...) with one document.Replace if non-null; then clears the session.
    public void PointerReleased(PointD imagePoint, bool shiftHeld);

    /// Drops the session and preview without committing (Esc, lost capture, tool switch).
    public void AbortDrag();

    /// Applies a key command. Returns true if it was handled (the view marks the key handled).
    /// DeleteSelection: document.Delete(selected) if a shape is selected and not dragging.
    /// Deselect: AbortDrag() + clear selection. Always handled (Esc never reaches the window).
    /// Undo/Redo: no-op while dragging; else document.Undo()/Redo(), then clear the selection
    /// if currentTool != Select (decision 11). Always handled.
    public bool Execute(CanvasKeyCommand command, AnnotationTool currentTool);

    public bool CanUndo { get; }    // !IsDragging && document.CanUndo
    public bool CanRedo { get; }    // !IsDragging && document.CanRedo
    public bool CanDelete { get; }  // !IsDragging && document.SelectedShape != null

    /// Called on every tool change: AbortDrag(); clear the selection unless the new tool is Select.
    public void ToolChanged(AnnotationTool tool);

    /// Executes the apply-to-selection part of ToolbarStyleEffects as ONE document.Replace.
    /// No-op while dragging or with no selection or when both apply fields are null.
    /// The stroke change goes through a delegate so 0042 can supply the measured text overload:
    public void ApplyStyle(ToolbarStyleEffects effects, Func<AnnotationShape, StrokeWidth, AnnotationShape> withStrokeWidth);
}
```

`PointerMoved` keeps the last image point (unclamped; the session clamps it). `PointerReleased` with no session does nothing. Until 0042, the `withStrokeWidth` delegate is `(s, w) => s.WithStrokeWidth(w)`.

#### 5. `ShapeDragSession` and `ToolbarStyleState` (owned by 0038 and 0040)

This spec only uses them. If, when implementing, `ShapeDragSession` doesn't exist in Core, port it here as `windows/src/Snapper.Core/Annotate/Model/ShapeDragSession.cs` with the tests from `SnapperTests/ShapeDragSessionTests.swift:8-68`. If `ToolbarStyleState` lacks `SelectedShapeStyleChanged` or the `ApplyColorToSelection`/`ApplyStrokeWidthToSelection` effects, add them with the tests from `SnapperTests/ToolbarStyleStateTests.swift:6-49`. Don't create a second copy of either type.

### Platform glue in `Snapper.Windows` (manually verified)

#### 6. Select tool on `AnnotationCanvas` (`windows/src/Snapper.Windows/Annotate/AnnotationCanvas.cs`)

- **MouseLeftButtonDown** (Select tool): `Focus()` the canvas, convert `e.GetPosition(this)` (DIPs) to an image point with `CanvasTransform`, without clamping, and call `editor.PointerPressed`. If it returns true, `CaptureMouse()`. Then `InvalidateVisual()` and raise the selection-changed notification (item 8). Mark the event handled. Right and middle buttons are ignored.
- **MouseMove**: while dragging, `editor.PointerMoved(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))`, then `InvalidateVisual()`. While not dragging, update `Cursor` from `CanvasCursor.For(...)` using a fresh hit test (only in the Select tool; otherwise 0040's cursor rule).
- **MouseLeftButtonUp**: `editor.PointerReleased(...)`, `ReleaseMouseCapture()`, invalidate, and raise the document-changed notification.
- **LostMouseCapture**: if `editor.IsDragging`, `editor.AbortDrag()` and invalidate (decision 9). This must not fire a commit when the release in `MouseLeftButtonUp` triggers it: call `PointerReleased` before `ReleaseMouseCapture`, so the session is already gone.
- **Rendering** (`OnRender`, after 0040's image and shapes): draw the shapes list with `editor.DragPreview` substituted for the shape with the same id. Then, only if the tool is Select and a shape is selected, draw adornments in canvas DIP space for `DragPreview ?? SelectedShape`:
  - rectangle: 8×8 DIP white squares with a 1 DIP accent border at the 4 corners;
  - arrow: 8 DIP white circles with the same border at both endpoints;
  - text (once 0042 exists): a 1 DIP dashed bounding box and no handles.
  
  The accent color is `SystemParameters.WindowGlassBrush`, falling back to `SystemColors.HighlightBrush`. Adornments are never drawn by `ShapeRenderer`, so they never reach export (0043). Snap the adornment rectangles to device pixels with `GuidelineSet` or `SnapsToDevicePixels` so they stay crisp at 150%.
- **Cursors**: map `CanvasCursorKind` to `Cursors.Arrow`, `Cursors.Cross`, `Cursors.SizeAll`, `Cursors.SizeNWSE`, `Cursors.SizeNESW`, and `Cursors.Hand` (endpoint). During a drag, keep the press-time cursor (set with the capture, so it stays when the pointer leaves the canvas).
- **Tool changes** (0040's tool selector or key): call `editor.ToolChanged(tool)` before 0040's existing handling, then release capture if held.

#### 7. Keyboard routing (`windows/src/Snapper.Windows/Annotate/AnnotationWindow.xaml(.cs)`)

The goal is that Delete, Backspace, Esc, Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z always reach the editor and never activate, toggle or get swallowed by a toolbar control.

- Handle keys in the window's **`PreviewKeyDown`** (tunnelling, so a focused toolbar control can't swallow them first). Compute `var key = e.Key == Key.System ? e.SystemKey : e.Key`, convert with `KeyInterop.VirtualKeyFromKey(key)`, build the modifiers from `Keyboard.Modifiers` (`ModifierKeys.Windows` → Win), and call `CanvasKeyMap.Map`. If it returns a command, call `editor.Execute(command, currentTool)` and set `e.Handled = true` when it returns true. Then invalidate the canvas and raise the notifications from item 8.
- **Text editing guard (hook for 0042):** if `Keyboard.FocusedElement` is a `TextBoxBase`, don't map any key here. 0042 decides how Backspace, Esc and Ctrl+Z route while a text box is edited. Until 0042, no text box exists, so this never triggers.
- Also handle `LeftShift`/`RightShift` in `PreviewKeyDown`/`PreviewKeyUp` while dragging: `editor.ShiftChanged(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))` and invalidate. Don't mark Shift handled.
- **Toolbar controls never take keyboard focus**: set `Focusable="False"` and `IsTabStop="False"` on every tool toggle, swatch, "+" button and S/M/L control (and on 0040's toolbar container, for example `ToolBar`'s `FocusManager.IsFocusScope` setting). After every toolbar click, return focus to the canvas (`canvas.Focus()`), as macOS does (`AnnotationWindowController.swift:84`, `:163`). Space and Enter therefore never re-activate a toolbar button.
- **Esc never closes the window**: no button in the window has `IsCancel="True"`, and the window has no `Esc` → `Close` binding. Esc is always marked handled (decision 9 and §8.2). Ctrl+W and Alt+F4 remain the ways to close (Ctrl+W wiring and the "Discard changes?" prompt are 0043's; Alt+F4 is the system's).
- Before 0042, the T tool exists, but nothing on the canvas creates text. Keys behave the same in every tool: Delete/Backspace with no selection do nothing, Esc does nothing visible.

#### 8. Toolbar style wiring (`AnnotationWindow`)

- After every document change (commit, delete, undo, redo) and every selection change, send `ToolbarStyleEvent.SelectedShapeStyleChanged(document.SelectedShape?.Style)` to the window's `ToolbarStyleState` (0040) and refresh the swatch highlight and S/M/L from `DisplayedStyle`. This is the port of `refreshStyle` (`AnnotationWindowController.swift:138-148`).
- In 0040's pick handler (swatch click, `ChooseColor` OK, S/M/L click): ignore the pick if `editor.IsDragging`; otherwise reduce the event, call `editor.ApplyStyle(effects, …)` (one undo step at most), write `AppSettings` from the persist effects (0040 already does this), refresh, and return focus to the canvas. Port of `pick` (`AnnotationWindowController.swift:149-164`) without the text-editing branch, which 0042 adds.
- New shapes keep reading `ToolbarStyleState.CurrentStyle` (0040).

#### 9. No project, package or `NativeMethods.txt` changes

This spec only adds source files in existing folders and test files. It needs no new Win32 APIs: keys and cursors use WPF, and `ChooseColor` is already in 0040's `NativeMethods.txt`.

No PRD update is needed. The design decisions and hover cursors are UI details below the PRD's level.

## Acceptance criteria

- [ ] With the Select tool (V), clicking a shape selects it and shows its handles (4 corners for a rectangle, 2 endpoints for an arrow). Clicking empty canvas, or the empty interior of a rectangle, deselects.
- [ ] A rectangle is hit within half its stroke width plus 4 DIPs of its outline, at any window size; its interior is not a hit.
- [ ] Dragging a shape's body moves it. Dragging a handle resizes it. Shift while resizing makes a rectangle square or snaps an arrow to 45°. Pressing or releasing Shift mid-drag updates the preview without moving the mouse.
- [ ] Handles have the same on-screen size and grab radius at every window size, on 100%, 150% and 200% monitors.
- [ ] Each move or resize is one undo step. A click without movement creates no undo step and doesn't mark the window as changed.
- [ ] Delete and Backspace delete the selected shape. This is undoable.
- [ ] A swatch click, a `ChooseColor` pick, or S/M/L while a shape is selected changes that shape (one undo step per pick, none if the value is unchanged) and becomes the style for new shapes. With no selection, they only change the style for new shapes.
- [ ] Selecting a shape makes the toolbar show its color and stroke width; deselecting shows the current style again.
- [ ] Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z undo and redo moves, resizes, color and stroke changes, deletes, and drawn shapes.
- [ ] Esc deselects and aborts an in-progress drag. It never closes the window.
- [ ] Delete, Backspace, Esc and the undo/redo shortcuts work right after clicking any toolbar control, and never toggle or activate that control.
- [ ] Hover cursors: move cursor over a shape, diagonal resize cursors over rectangle corners, and the hand cursor over arrow endpoints (Select tool only).
- [ ] Two annotation windows keep independent selections, drags, histories and current styles.
- [ ] Selection adornments never appear in exported output (checked with 0043; the code keeps them out of `ShapeRenderer`).

## Edge cases

- **Overlapping shapes:** a click picks the topmost one. A handle of the *selected* shape wins over a higher shape's body.
- **Tiny shapes** where handles overlap the body: handles win.
- **Handles on the image edge:** hit-testable from the margin, because hit-testing uses the unclamped point (decision 8).
- **Mixed DPI:** the canvas works in DIPs, so the hit radii stay 6 / 4 DIPs whatever the monitor scale. What changes is `CanvasTransform.Scale`: for an unzoomed capture from a 150% monitor, Scale = 1/1.5, so the handle radius is 9 image pixels; from a 200% monitor, 12 image pixels. Moving the window to a monitor with a different DPI changes nothing in DIPs (WPF rescales the window); the hit radius in image pixels only follows `Scale`.
- **Negative-origin monitors:** not relevant. All editing coordinates are canvas-local DIPs and image pixels, never virtual-screen pixels.
- **Dragging across monitors or outside the window:** with mouse capture, `MouseMove` keeps arriving with canvas-relative DIPs outside the canvas (including negative values, or positions on another monitor). The session clamps them to the image, so the grabbed point stays on the image edge.
- **A window spanning two monitors with different DPIs:** WPF renders it at one DPI (its current monitor's). DIP positions stay consistent, so no special handling is needed.
- **Resizing past the opposite corner:** 0038's `Resized` normalizes. Preview and commit use the same function.
- **Undo removing the selected shape:** the document clears the selection (0039's invariant), and the toolbar refreshes.
- **Undo while the Rectangle/Arrow tool is active** restores a snapshot whose selection is cleared (decision 11), so no adornments appear outside the Select tool.
- **Lost capture mid-drag** (Alt+Tab, the Win key, another window taking focus): the drag aborts, nothing is committed, and the selection stays. Toolbar clicks, and so `ChooseColor`, can't happen during a drag because the canvas holds the mouse capture.
- **Tool key (R) pressed mid-drag:** the drag aborts, the selection clears, and capture is released.
- **Delete/Backspace with Ctrl or Alt held** (Ctrl+Backspace, Alt+Backspace): not handled. Ctrl+Shift+Y: not handled. Shift+Delete: deletes (Shift is ignored).
- **Delete with nothing selected, or during a drag:** does nothing. Esc with nothing selected: does nothing visible, and the window stays open.
- **Key auto-repeat** on Ctrl+Z: each repeat undoes one more step until the history is empty, matching macOS.
- **Picking the same color as the selected shape:** no undo step, but the setting is still written.
- **Double-click on a shape in the Select tool:** two presses. Same as a click (0042 adds double-click text re-edit).

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`, xUnit):
  - `CanvasHitTesterTests.cs` (port of `SnapperTests/CanvasHitTesterTests.swift:5-28`, adapted to DIPs):
    - `ImagePixels(6, t)` = 6 / scale for scale 0.25, 0.5, 1 and 2, where `t` is a `CanvasTransform` for a 400×400 image shown at that scale.
    - Mixed DPI: unzoomed transforms for captures from 100%, 150% and 200% monitors (pixels per DIP 1, 1.5, 2, view size = image size / pixels per DIP) → handle radius 6, 9 and 12 image pixels, shape slop 4, 6 and 8.
    - Two overlapping rectangles (lower 100,100 150×150; upper 100,100 170×170): at (100,100) with nothing selected → `Shape(upper)`. With `lower` selected, a point 5 DIPs left of its top-left corner → `Handle(lower, TopLeft)` at scales 0.25, 0.5, 1, 2. A point 7 DIPs away → not a `Handle`.
    - Handles of an unselected shape aren't hit: a rectangle with stroke S (outline threshold 1 + 4 = 5 px at scale 1) and a point 5.5 DIPs left of its top-left corner → `Handle` when it's selected, `None` when nothing is selected.
    - Outline-only: a 200×200 rectangle with stroke M (4 image points) at pixels per DIP 1 and scale 1: its center → `None`; a point 2 + 4 = 6 px outside the edge → `Shape`; 7 px outside → `None`; 6 px inside the edge → `Shape`; 7 px inside → `None`. Repeat at scale 0.5 (slop becomes 8 px: 10 px from the edge → `Shape`, 11 px → `None`).
    - An arrow is hit on its shaft and its head; a point far from both → `None`.
    - `Scale` 0 (empty view) → `None` for every point.
  - `CanvasKeyCommandTests.cs` (port of `SnapperTests/CanvasKeyCommandTests.swift:5-16`):
    - `VK_DELETE` and `VK_BACK` with no modifiers → `DeleteSelection`; with Shift → `DeleteSelection`.
    - `VK_ESCAPE` → `Deselect`.
    - `VK_DELETE`, `VK_BACK` and `VK_ESCAPE` with each of Ctrl, Alt and Win → null.
    - Ctrl+Z → `Undo`; Ctrl+Shift+Z → `Redo`; Ctrl+Y → `Redo`.
    - Z with no modifiers, Alt+Z, Ctrl+Alt+Z, Win+Z, Ctrl+Shift+Y and Ctrl+Win+Z → null.
    - `V` (0x56) with no modifiers → null (left to the tool mapping).
  - `CanvasCursorTests.cs`:
    - Select tool: `Handle` TopLeft and BottomRight → `ResizeNwse`; TopRight and BottomLeft → `ResizeNesw`; Start and End → `ResizeEndpoint`; `Shape` → `Move`; `None` → `Arrow`.
    - While dragging with a press-time `Handle(TopLeft)` and a current hit of `None` → `ResizeNwse`.
    - Rectangle and Arrow tools: inside the image → `Crosshair`, outside → `Arrow`, whatever the hit.
  - `SelectionEditorTests.cs` (behavior from `AnnotationCanvasView.swift:97-194` and `ShapeDragSessionTests.swift:54-68`):
    - Pressing on a shape selects it and starts a drag (`PointerPressed` returns true, `IsDragging`). Selection alone doesn't change `IsDirty` and adds no undo step.
    - Pressing on empty canvas clears the selection and returns false.
    - Move: press at (20,30) on a rectangle at (20,20,40,30), move to (30,25) → `DragPreview` is at (30,15,40,30), and the document is unchanged. Release → one `Replace`; `Undo` restores the original, `Redo` the moved one.
    - 100 `PointerMoved` calls followed by a release → exactly one undo step.
    - Press and release at the same point → no undo step, `IsDirty` unchanged from before.
    - Resize: with the rectangle selected, press on its BottomRight handle, move with Shift → the preview is square and TopLeft is fixed. `ShiftChanged(false)` without moving → the preview is free again.
    - A move to (-5000, 9000) (outside the window, as with mouse capture on another monitor) on a 100×100 image → the grabbed point is clamped to (0, 100).
    - `AbortDrag` mid-drag → no `DragPreview`, document unchanged, selection kept.
    - `Execute(Deselect)` mid-drag → drag aborted, selection cleared, returns true. With nothing selected → still returns true (Esc is always consumed).
    - `Execute(DeleteSelection)` with a selection → shape removed, selection cleared, one undo step; with no selection → no change; mid-drag → no change.
    - `Execute(Undo)` mid-drag → no change; `CanUndo`, `CanRedo` and `CanDelete` are false while dragging.
    - `Execute(Undo)` with the current tool Rectangle after a selection was recorded in history → selection is null afterwards. With the Select tool → the snapshot's selection is kept (if 0039 records it).
    - `ToolChanged(Rectangle)` mid-drag → drag aborted, selection cleared; `ToolChanged(Select)` keeps the selection.
    - `ApplyStyle` with `ApplyColorToSelection = green` on a selected red shape → one undo step, the shape is green, and its geometry is unchanged. With both apply fields null → no undo step. Color and stroke together → one undo step. Mid-drag → no change.
    - Two editors on two documents → a press, delete or undo in one doesn't change the other.
  - Only if missing from 0038/0040 at implementation time: `ShapeDragSessionTests.cs` and `ToolbarStyleStateTests.cs`, ported case for case from the Swift files (see item 5).
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed with all existing and new tests green.
- **Manual verification** (Windows 11; can't be automated). Use two monitors at different scales (for example 100% and 150%) where possible.
  1. Press Ctrl+Shift+6 and capture an area. Draw two overlapping rectangles (R) and an arrow (A). Expected: no shape shows handles after it is drawn.
  2. Press V. Click the first rectangle's outline. Expected: 4 corner handles. Click inside its empty interior. Expected: deselected (unless the click lands near the other rectangle's outline). Click the arrow. Expected: 2 endpoint handles.
  3. Click where both rectangles' outlines overlap. Expected: the one drawn last is selected.
  4. Hover a shape: the move cursor appears. Hover a selected rectangle's corners: diagonal resize cursors. Hover an arrow endpoint: the hand cursor. Hover empty canvas: the arrow cursor.
  5. Drag a rectangle's body. Expected: it moves smoothly. Ctrl+Z → it jumps back. Ctrl+Y → it moves again. Ctrl+Z, then Ctrl+Shift+Z → same.
  6. Drag each corner handle. Expected: the opposite corner stays put. Four Ctrl+Z presses undo all four resizes.
  7. Drag a corner with Shift held. Expected: a square. Release Shift without moving the mouse. Expected: a free rectangle again. Drag an arrow endpoint with Shift. Expected: 45° steps around the other endpoint.
  8. Drag a shape far outside the window, onto the other monitor. Expected: it stops at the image edge and stays selectable. Release there. One Ctrl+Z restores it.
  9. Start dragging a shape and press Alt+Tab before releasing. Expected: the shape snaps back, nothing is committed, and returning to the window shows it still selected.
  10. Start dragging a shape and press Esc before releasing. Expected: it snaps back and is deselected. Press Esc with a shape selected: it deselects, and the window stays open. Press Esc again: nothing happens, and the window stays open.
  11. Click a shape without moving. Expected: Ctrl+Z still undoes the previous real edit. On a freshly opened window (no edits), clicking and then closing with Ctrl+W doesn't ask "Discard changes?" (once 0043 exists).
  12. Select a shape and press Delete. Expected: deleted; Ctrl+Z brings it back. Repeat with Backspace.
  13. Click the blue swatch, then press Backspace with a shape selected. Expected: the shape is deleted, and the swatch isn't toggled or re-activated. Click S/M/L, then press Space or Enter. Expected: the stroke control doesn't change.
  14. Select a red rectangle, click the blue swatch. Expected: blue, swatch highlighted. Ctrl+Z → red; Ctrl+Y → blue.
  15. With the shape selected, click "+" and pick a custom color in the Color dialog. Expected: one change; Ctrl+Z reverts exactly that pick, and the "+" button is highlighted.
  16. With a shape selected, click L. Expected: thicker stroke; Ctrl+Z reverts it.
  17. Select a green M shape, then a red S shape. Expected: the toolbar follows each selection. Press Esc. Expected: the toolbar shows the last style you *picked*.
  18. With nothing selected, pick yellow and S, then draw a rectangle (R). Expected: yellow and thin, not selected.
  19. Select a shape, then press R. Expected: the handles disappear. Draw a rectangle and press Ctrl+Z. Expected: the rectangle is removed, and no handles appear.
  20. Move the annotation window between the 100% and 150% monitors and resize it smaller and larger. Expected: handles stay the same visual size, are equally easy to grab, and are crisp.
  21. Open a second annotation window (Ctrl+Shift+6). Select and edit shapes in each. Expected: undo in one window doesn't affect the other, and each keeps its own selection.

## Resolved decisions

- Undo/Redo and Delete stay keyboard-only, with no toolbar buttons. Recorded in PRD §13.7.

## Out of scope

- The window, canvas, `CanvasTransform`, `ShapeRenderer`, tool selector and V/R/A/T keys, rectangle/arrow drawing, swatches, "+"/`ChooseColor`, S/M/L, `ToolbarStyleState`, and persisting the last color/width: **0040**.
- Shape geometry, hit-testing, move/resize, Shift constraints, `ShapeDragSession`: **0038**. The document, history, selection storage and dirty flag: **0039**.
- Text placement, editing and re-edit, double-click handling, the measured text stroke-width change, and key routing while a text box is edited: **0042**.
- Export, Ctrl+S, Ctrl+C, Ctrl+W, "Discard changes?", the quit confirmation, and the shutdown block reason: **0043**. Copy & Close (Ctrl+Enter): **0044**.
- Marquee or multi-selection, copy/paste or duplication of shapes, arrow-key nudging, z-order changes, a right-click context menu, and live color preview. None of these are in PRD §8.2.
