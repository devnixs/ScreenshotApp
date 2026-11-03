# 0040 — Open Ctrl+Shift+6 captures in a focused Windows annotation window and draw rectangles and arrows

> Source: docs/PRD.md §8.1 (lines 120–129), §8.2 (lines 131–158: tool rows, color, stroke width, "stays selected", drawing inside the image, minimum drag, remembered color/width), §8.4, §5 (focus bullet), §6, §13.1 (annotation windows appear in the taskbar and Alt+Tab), §13.2 (term mapping), §13.4 (focus), §13.5 (capture, shutter, errors), §13.7 (Annotate on Windows).
> macOS reference: docs/specs/done/0017-annotation-window-canvas_COMPLETED.md, the color/stroke part of docs/specs/done/0018-annotation-editing_COMPLETED.md, and docs/specs/todo/0022-annotation-window-focus_TODO.md.
> Depends on: 0031-windows-screenshot-rectangle-capture, 0032-windows-window-selection-mode, 0039-windows-annotation-document-undo (and, through them, 0025–0030, 0038).

## Context

When this spec starts, the Windows app (`windows/`) has:

- The tray shell and `App/AppCoordinator` (0025). `StartAnnotate()` is wired to Ctrl+Shift+6 and the tray menu (0029). It runs the selection overlay with `CapturePurpose.Annotate` (0030, 0032), but nothing useful happens with the result yet.
- Rectangle and window capture through Windows.Graphics.Capture: `Capture/ScreenshotCapturer` (0031, extended for windows by 0032). It turns a `SelectionResult` into a captured bitmap at native physical pixels. 0031 also provides `Support/ShutterSound`, `Support/ErrorAlerts` (the "Couldn't take screenshot" alert), and `Support/PngEncoder`/`Support/Clipboard`. This spec doesn't use the last two.
- Geometry (0028): `PointD`, `SizeD`, `RectD`, `PixelPoint`, `PixelSize`, `PixelRect`, `DisplayInfo` (bounds, work area, DPI/scale, primary flag), `DisplayGeometry`, `WindowDisplayRule`, and `Capture/DisplayInfoProvider`.
- Settings (0027): `AppSettings` with `AnnotationColor` (`RgbaColor`, default red) and `AnnotationStrokeWidth` (`StrokeWidth`, default M), persisted by `JsonSettingsStore`.
- The pure annotation model (0038): `AnnotationShape` (rectangle/arrow/text kinds, `StrokePixels(pixelsPerDip)`, `ArrowGeometry(pixelsPerDip)`, `MakeRectangle`/`MakeArrow`/`ArrowFromDrag`, the 3-view-DIP minimum-drag constant), `ShapeGeometry.SquareCorner`, `ArrowGeometry` (`Tip`, `BaseCenter`, `BaseLeft`, `BaseRight`, `ShaftStart`).
- `AnnotationDocument` (0039): `AnnotationDocument(PixelSize imagePixelSize, double pixelsPerDip)`, `Add`, `Shapes`, `Changed` event, undo/redo, dirty flag (docs/specs/todo/0039-windows-annotation-document-undo_TODO.md, public surface at lines 104–150).

The macOS reference is the code that 0017 and 0018 shipped (the 0022 focus fix lands before this batch):

- `Snapper/Annotate/CanvasTransform.swift:1-46` — view↔image transform, letterboxing, never upscales. Tests: `SnapperTests/CanvasTransformTests.swift:6-48`.
- `Snapper/Annotate/AnnotationWindowSizing.swift:3-29` — 80% sizing and cascade. Tests: `SnapperTests/AnnotationWindowSizingTests.swift:9-37`.
- `Snapper/Annotate/AnnotationTool.swift:1-18` — tools and key routing. Tests: `SnapperTests/AnnotationToolTests.swift:5-20`.
- `Snapper/Annotate/AnnotationDrawingGesture.swift:1-52` — drag state, image-edge clamping, Shift constraints, minimum drag. Tests: `SnapperTests/AnnotationDrawingGestureTests.swift:10-79`.
- `Snapper/Annotate/Model/ToolbarStyleState.swift:1-51` — current/displayed style, swatch selection, persist/apply effects. Tests: `SnapperTests/ToolbarStyleStateTests.swift:6-49`.
- `Snapper/Annotate/Model/RGBAColor.swift:54-82` — the 8 preset colors (sRGB values) and their names.
- `Snapper/Annotate/ShapeRenderer.swift:1-56` — the shared renderer (rectangle: mitered outline; arrow: butt-capped shaft from `shaftStart` to `baseCenter` plus filled head; image drawn top-left/y-down). Tests: `SnapperTests/ShapeRendererTests.swift:23-63`.
- `Snapper/Annotate/AnnotationCanvasView.swift:84-156` (mouse/keys/Shift-without-movement), `AnnotationWindowController.swift:30-188` (toolbar, style picks, persisting, placement), `AnnotationWindowManager.swift:14-31` (open, list, focus), `Snapper/App/AppCoordinator.swift:207-224` (annotate capture path: capture, shutter, open, no save/copy).

PRD behavior this spec delivers on Windows (§13.7: "Section 8 applies, with the shortcut mapping from 13.2"):

- §8.1: the capture works exactly as in §6, including the shutter-sound preference, but opens in a new annotation window instead of being saved or copied. The screenshot destination is ignored. The window is normal and resizable, with a toolbar on top. It initially fits the image into about 80% of the capture monitor's **work area** (§13.2), falling back to the primary monitor, allowing for window chrome. It never upscales, including on resize, and excess space becomes centered neutral margins. Several can be open at once. It opens in front with keyboard focus (§13.7, line 311), so tool keys work without a click.
- §8.2: Select/Move (V), Rectangle (R, Shift → square), Arrow (A, Shift → 45°), Text (T); the active tool stays selected and new windows start with Rectangle; drawing starts only inside the image and endpoints stay within it, including with Shift; rectangle drags with either side below 3 view DIPs, and arrows shorter than 3 view DIPs, create no shape at any zoom; 8 preset swatches plus "+" (Windows Color dialog, §13.2); S/M/L = 2/4/8 image DIPs; the last-used color and width are remembered between sessions (defaults red, M).
- §13.7: toolbar glyphs from Segoe Fluent Icons; the same renderer draws the canvas and (in 0043) the export.

### macOS facts that don't apply on Windows

- No activation-policy problem: §13.1 says the annotation window is a normal window that appears in the taskbar and Alt+Tab. There is no Dock and no `.accessory` trade-off (0017 decision 4).
- No `NSToolbar`. The toolbar is a WPF row inside the client area, so the "chrome" the sizing allows for is the non-client frame **plus** the toolbar row.
- No Y flip anywhere. The canvas (WPF, DIPs, y down) and image pixels are both top-left/y-down. Global coordinates are physical pixels (§13.4).
- `NSColorPanel` is continuous and non-modal. `ChooseColor` is modal and commits only on OK (§13.2 mapping). So there is no color-panel retargeting (`AnnotationWindowController.swift:170-191,256-263`).
- "Points" are DIPs, and `pixelsPerPoint` is the capture monitor's scale factor (DPI ÷ 96), named `PixelsPerDip` in 0039.
- macOS 0022's `NSApp.activate()` cooperation problem becomes the Windows foreground lock (see "Focus" below).

### Decisions taken in this spec (interpretations, no new product behavior)

1. **Shutter and destination.** As on macOS (0017 decision 1): the shutter plays iff *Play shutter sound* is on. *Screenshot destination* is ignored, so no file is written and the clipboard is untouched. Capture errors show 0031's "Couldn't take screenshot" alert (§13.5), and no window opens.
2. **Work area and fallback.** "80% of the capture display's visible frame" means 80% of the work area of the monitor the selection was on. For a rectangle, that's the drag's monitor. For a window, it's the monitor `WindowDisplayRule` assigned (largest overlap), the same one whose scale 0031/0032 used for the capture. If that monitor is gone when the window opens, use the primary monitor.
3. **Chrome on Windows** = the non-client frame from `AdjustWindowRectExForDpi` at the target monitor's DPI (left+right borders, title bar+bottom border, in physical pixels, converted to DIPs) plus the measured toolbar height in DIPs. Unlike macOS (no side chrome), the horizontal chrome is subtracted from the width budget too.
4. **Never upscale** is measured in DIPs, as on macOS in points: the image is shown at most at `imagePixels ÷ PixelsPerDip` DIPs. Captured at 150% and shown on a 150% monitor, that's one physical pixel per image pixel. If the user drags the window to a 100% monitor, the natural size stays the same in DIPs (it's shown smaller physically), exactly like a 2× capture on a 1× Mac display.
5. **Crisp 1:1 display.** `CanvasTransform` takes the canvas's current DPI scale (`viewScale`) and snaps the image's top-left offset to whole physical pixels. When one image pixel maps to exactly one physical pixel (`IsPixelExact`), the canvas draws the bitmap with `BitmapScalingMode.NearestNeighbor`. Otherwise it uses `HighQuality`. This is the only Windows addition to the macOS transform, and it doesn't change any geometry the model sees beyond the sub-pixel offset.
6. **Toolbar contents in this spec**: tools (Select, Rectangle, Arrow, Text), a separator, 8 swatches + "+", a separator, S/M/L. **Save and Copy are absent** until 0043 adds them (with Copy & Close in 0044) at the right end. A right-aligned empty `ActionsPanel` is reserved for them. This follows macOS 0017/0020: a button that does nothing looks broken, and 0043 owns the export behind it. Ctrl+S and Ctrl+C do nothing until 0043.
7. **Text tool**: the Text button is present but **disabled**, and T is ignored until 0042 enables them (as in 0017 decision 7). **Select tool (V)** can be chosen, but clicking and dragging with it does nothing until 0041.
8. **Color and stroke width** (from macOS 0018, assigned to this spec by the batch table):
   - Picking a swatch, "+" color, or S/M/L sets the window's current style for new shapes and immediately persists it to `AppSettings` (remembered between sessions).
   - Each window keeps its own current style from the moment it opens. A pick in one window doesn't restyle other open windows, but new windows read the persisted value.
   - `ToolbarStyleState` is ported **whole**, including selection effects, because it's pure and already tested. In this spec the glue never feeds it a selected-shape style (nothing can be selected yet), so `ApplyColorToSelection`/`ApplyStrokeWidthToSelection` are always null here. 0041 wires them.
   - The highlighted swatch follows `SwatchSelection`. A custom color highlights "+".
   - The Windows Color dialog opens with the current displayed color, full-open, and keeps its 16 custom colors for the lifetime of the process (shared by all windows). Cancel changes nothing.
9. **Ctrl+W closes the window** (§13.2); Alt+F4 and the title-bar X close it natively. There's no "Discard changes?" prompt until 0043. Esc never closes the window. During a drag, Esc aborts the drag and creates no shape (macOS `cancelOperation`, `AnnotationCanvasView.swift:165-169`). Otherwise it does nothing until 0041 adds deselect.
10. **Exit and shutdown with open windows**: until 0043, Exit Snapper closes all annotation windows without a prompt, and nothing registers a shutdown block reason.
11. **Focus** (port of macOS 0022): the overlay doesn't hand the foreground back after an annotate *selection* (only after cancellation). §13.4 assigns this to 0030. If 0030 didn't implement the exception, this spec adds the macOS `SelectionFocusPolicy` port (see Scope). The window then takes the foreground itself: Snapper received the last input event (the selecting click), so `SetForegroundWindow` is allowed.
12. **Cascade**: as on macOS, a new window whose top-left exactly matches an open annotation window's top-left shifts down-right by 24 DIPs (rounded to physical pixels at the target monitor's scale). On Windows y is down, so the shift is (+step, +step) instead of macOS's (+24, −24). It stops before leaving the work area.

## User story

As a Snapper user on Windows, I press Ctrl+Shift+6, select an area or a window, and the capture opens in front of me with keyboard focus, in a resizable window sized to my monitor. There I can draw rectangles and arrows in the color and width I last used, with Shift for squares and 45° angles, switch tools with V/R/A, and open several such windows at once.

## Scope & changes

### Pure logic (`Snapper.Core`, xUnit-tested; no WPF, WinForms, WinRT, or interop types)

- **`windows/src/Snapper.Core/Annotate/CanvasTransform.cs`**: `public readonly record struct CanvasTransform` (namespace `Snapper.Core.Annotate`). Port of `CanvasTransform.swift:1-46`, plus decision 5.
  - Constructor `CanvasTransform(PixelSize imagePixelSize, double pixelsPerDip, SizeD viewSize, double viewScale)`. `viewSize` is the canvas element's render size in DIPs. `viewScale` is the canvas's current DPI scale (`VisualTreeHelper.GetDpi(canvas).DpiScaleX`, for example 1.5 at 150%).
  - If any of the image width/height, `pixelsPerDip`, view width/height, or `viewScale` is non-finite or ≤ 0, then `Scale = 0`, `ImageRectInView = RectD.Empty`/zero, and every conversion to image space returns `null` (as Swift does, `:11-16`).
  - `Scale` (view DIPs per image pixel) = `min(viewW / imageW, viewH / imageH, 1 / pixelsPerDip)`.
  - `ImageRectInView`: size = image × `Scale`. The raw offset is `((viewW − w)/2, (viewH − h)/2)`, snapped per axis to the physical-pixel grid: `Math.Round(offset × viewScale, MidpointRounding.AwayFromZero) / viewScale`. With integer view sizes at `viewScale` 1 or 2, the macOS numbers hold.
  - Members: `PointD? ImagePoint(PointD viewPoint)`, `PointD ViewPoint(PointD imagePoint)`, `RectD ViewRect(RectD imageRect)`, `RectD? ImageRect(RectD viewRect)`, `double ViewLength(double imageLength)`, `bool IsInsideImage(PointD viewPoint)` (edges inclusive), `PointD? ClampedImagePoint(PointD viewPoint)` (clamped to `0…imageW`, `0…imageH`).
  - `bool IsPixelExact` = `Scale > 0 && |Scale × viewScale − 1| < 1e-9`.
  - Doc comment: canvas DIPs and image pixels are both top-left/y-down. This is view↔image space, so it must not use `DisplayGeometry` (that's for global physical pixels).
- **`windows/src/Snapper.Core/Annotate/AnnotationWindowSizing.cs`**: `public static class AnnotationWindowSizing`. Port of `AnnotationWindowSizing.swift:3-29`, in DIPs for the budget and physical pixels for the result.
  - `public static AnnotationWindowPlacement Frame(PixelSize imagePixelSize, double imagePixelsPerDip, PixelRect workArea, double monitorScale, SizeD chromeDip, SizeD minCanvasDip)`, returning `record struct AnnotationWindowPlacement(SizeD CanvasDip, PixelRect WindowRect)`.
    - `waDip = workArea.Size / monitorScale`. `imageDip = imagePixelSize / imagePixelsPerDip`.
    - Budget: `maxW = 0.8 · waDip.W − chromeDip.W`, `maxH = 0.8 · waDip.H − chromeDip.H`.
    - `fit = clamp(min(1, maxW / imageDip.W, maxH / imageDip.H), 0, 1)` (never upscaled).
    - Available: `availW = max(0, waDip.W − chromeDip.W)`, `availH = max(0, waDip.H − chromeDip.H)`. Effective minimum = `min(minCanvasDip, avail)` per axis (port of the tiny-screen rule at `AnnotationWindowController.swift:74-76`).
    - `CanvasDip` per axis = `Floor(min(avail, max(effectiveMin, imageDip · fit)) + 1e-6)`. The epsilon avoids `751.9999…` floating-point artifacts.
    - Outer window size in pixels = `Round((CanvasDip + chromeDip) × monitorScale)` per axis (`AwayFromZero`), and never larger than the work area.
    - Origin = `workArea.X + Round((workArea.W − outerW) / 2)`, the same for Y, with `MidpointRounding.AwayFromZero`. `WindowRect` is in physical virtual-screen pixels, ready for `SetWindowPos`.
  - `public static PixelRect Cascaded(PixelRect frame, IReadOnlyCollection<PixelPoint> existingTopLefts, PixelRect workArea, int stepPixels)`: while `frame.TopLeft` is in `existingTopLefts`, offset by `(+stepPixels, +stepPixels)`. Stop (keeping the last in-bounds frame) when the next frame wouldn't be fully inside `workArea`. Callers pass `stepPixels = Round(24 × monitorScale)`.
- **`windows/src/Snapper.Core/Annotate/AnnotationTool.cs`**: `public enum AnnotationTool { Select, Rectangle, Arrow, Text }` plus a static class `AnnotationTools`. Port of `AnnotationTool.swift:1-18`.
  - `static AnnotationTool? ForShortcut(string characters, bool hasCtrlAltOrWin, bool textToolEnabled)`: case-insensitive `v/r/a/t`. Returns `null` when Ctrl, Alt (including AltGr = Ctrl+Alt), or Win is held, for any other string (including `""` and `"rr"`), and for `t` when `textToolEnabled` is false. Shift is allowed.
  - `int SegmentIndex(this AnnotationTool)` / `AnnotationTool? FromSegmentIndex(int)` in order Select, Rectangle, Arrow, Text. `bool CreatesShapesByDragging(this AnnotationTool)` is true for Rectangle and Arrow. `DefaultTool = Rectangle` (PRD §8.2 line 146).
- **`windows/src/Snapper.Core/Annotate/AnnotationDrawingGesture.cs`**: `public sealed class` (or record struct with explicit mutation) `AnnotationDrawingGesture`. A line-by-line port of `AnnotationDrawingGesture.swift:1-52`.
  - `static AnnotationDrawingGesture? Begin(AnnotationTool tool, PointD viewPoint, CanvasTransform transform, RgbaColor color, StrokeWidth strokeWidth)` returns null unless the tool creates shapes by dragging and the point is inside the image.
  - `Update(PointD viewPoint)` stores the clamped image point. `Tool`, `Start`, and `Current` are exposed.
  - `AnnotationShape? Shape(bool constrained, bool completing)`: Shift constraint via `ShapeGeometry.SquareCorner`/`AnnotationShape.ArrowFromDrag` (0038). Then uniformly shorten about the start so the end stays inside the image (`:28-43`). Then build with `MakeRectangle`/`MakeArrow`, with `minimumDrag = completing ? <0038 minimum-drag DIPs, 3> / transform.Scale : 0`.
  - It never touches the document. The caller `Add`s the completed shape (one undo step, 0039).
- **`windows/src/Snapper.Core/Annotate/ToolbarStyleState.cs`**: `ShapeStyle` (record struct `Color`, `StrokeWidth`), `SwatchSelection` (preset index or custom), `ToolbarStyleEvent` (`SelectedShapeStyleChanged(ShapeStyle?)`, `ColorPicked(RgbaColor)`, `StrokeWidthPicked(StrokeWidth)`), `ToolbarStyleEffects` (4 nullable fields), and `ToolbarStyleState` with `CurrentStyle`, `SelectedShapeStyle`, `DisplayedStyle`, `SwatchSelection`, `ToolbarStyleEffects Reduce(ToolbarStyleEvent)`. A behavior-for-behavior port of `ToolbarStyleState.swift:1-51` (the macOS file lives in `Model/`, but the batch table assigns "toolbar style" to this spec, so it goes in `Annotate/`). Also add the `AnnotationShape.Style` accessor here as an extension method if 0038 didn't add one.
- **Color presets.** If 0027/0038 haven't already defined them, add `windows/src/Snapper.Core/Annotate/Model/ColorPreset.cs` (`enum ColorPreset { Red, Orange, Yellow, Green, Blue, Purple, Black, White }` with `DisplayName` and `Color`, using exactly the sRGB values in `RGBAColor.swift:72-79`, and `RgbaColor.Presets` in that order). If they exist, reuse them. Don't duplicate them.
- **`windows/src/Snapper.Core/Annotate/ColorRefConversion.cs`**: `static uint ToColorRef(RgbaColor c)` (`0x00BBGGRR`, each channel `Round(component × 255)`, alpha dropped) and `static RgbaColor FromColorRef(uint colorRef)` (alpha 1, high byte ignored). Pure bit math. `ChooseColor` uses it.
- **`windows/src/Snapper.Core/Annotate/AnnotationToolbarLayout.cs`**: static descriptors that the WPF toolbar renders, so labels, tooltips, and glyphs are tested. Each tool gets `(AnnotationTool Tool, string Label, string ToolTip, string Glyph, double GlyphRotation)`:
  - Select: "Select", "Select (V)", `` (TouchPointer).
  - Rectangle: "Rectangle", "Rectangle (R)", `` (Checkbox, an empty square).
  - Arrow: "Arrow", "Arrow (A)", `` (Forward) rotated −45° so it points up-right.
  - Text: "Text", "Text (T)", `` (Font).

  Also: swatch tooltips = `ColorPreset.DisplayName` ("Red", …), "+" tooltip "Custom color…", custom glyph `` (Add), stroke labels "S"/"M"/"L" with tooltips "Small"/"Medium"/"Large", and an empty `TrailingActions` list that 0043 (Save, Copy) and 0044 (Copy & Close) extend; 0044 expects this type (docs/specs/todo/0044-windows-annotation-copy-and-close_TODO.md:98-101). The implementer checks each codepoint against Microsoft's Segoe Fluent Icons reference. If one is wrong, they pick the documented glyph with that meaning and update the constant and test together.
- **`windows/src/Snapper.Core/Annotate/ShapeDrawingPlan.cs`**: makes the shared renderer testable without WPF. `static IReadOnlyList<DrawPrimitive> For(AnnotationShape shape, double pixelsPerDip)` returns pure primitives in image pixels:
  - `StrokeRect(RectD Rect, double Thickness, RgbaColor Color)`, with miter join, for rectangles.
  - For arrows (from `shape.ArrowGeometry(pixelsPerDip)`): `StrokeLine(PointD From, PointD To, double Thickness, RgbaColor Color)` from `ShaftStart` to `BaseCenter` with flat (butt) caps, **omitted when `ShaftStart == BaseCenter`**, followed by `FillPolygon(PointD[] Points, RgbaColor Color)` = `[Tip, BaseLeft, BaseRight]`. This mirrors `ShapeRenderer.swift:29-41`.
  - Text: an empty list. 0042 adds a text primitive.
  - `Thickness = shape.StrokePixels(pixelsPerDip)`.
- **Focus policy (only if 0030 didn't already implement the annotate exception):** `windows/src/Snapper.Core/Capture/SelectionFocusPolicy.cs`, a port of macOS 0022's `SelectionFocusPolicy` (`RestoresPreviousWindow(CapturePurpose, Outcome)`: cancelled → true for every purpose; selected → true for Screenshot/Record, false for Annotate; exhaustive switch with no default). 0030's `SelectionOverlayController` consults it before calling `ForegroundRestorer`. If 0030 already has an equivalent, add nothing and note that in the commit.

### Rendering (`Snapper.Windows`, shared with export in 0043)

- **`windows/src/Snapper.Windows/Annotate/ShapeRenderer.cs`**: `internal static class ShapeRenderer`, used on the dispatcher thread. Contract: the `DrawingContext` already has a transform that maps image pixels (top-left, y down) to the destination. The canvas pushes the `CanvasTransform` matrix; 0043's exporter uses identity at 96 DPI.
  - `DrawImage(DrawingContext dc, BitmapSource image)` draws at `new Rect(0, 0, image.PixelWidth, image.PixelHeight)`. Use pixel dimensions, not `image.Width`, which depends on the bitmap's DPI metadata.
  - `Draw(DrawingContext dc, IEnumerable<AnnotationShape> shapes, double pixelsPerDip)` and `Draw(DrawingContext dc, AnnotationShape shape, double pixelsPerDip)` map each `ShapeDrawingPlan` primitive:
    - `StrokeRect` → `dc.DrawRectangle(null, pen, rect)` with `PenLineJoin.Miter`.
    - `StrokeLine` → `dc.DrawLine` with `PenLineCap.Flat` start and end.
    - `FillPolygon` → a frozen `StreamGeometry` filled with no pen.
    - Brushes and pens are created from `RgbaColor` (`Color.FromArgb(Round(a×255), …)`) and frozen.
  - No selection handles here (UI only, 0041), so the export never contains them.

### Platform glue (`Snapper.Windows`, dispatcher thread, manually verified)

- **`windows/src/Snapper.Windows/Annotate/AnnotationCanvas.cs`**: `internal sealed class AnnotationCanvas : FrameworkElement`. `Focusable = true`, `FocusVisualStyle = null`, `ClipToBounds = true`. It holds the window's `AnnotationDocument`, the frozen `BitmapSource`, `CurrentTool`, `CurrentColor`, `CurrentStrokeWidth`, and the active gesture/preview.
  - `Transform` = `new CanvasTransform(document.ImagePixelSize, document.PixelsPerDip, new SizeD(ActualWidth, ActualHeight), VisualTreeHelper.GetDpi(this).DpiScaleX)`.
  - `OnRender`:
    1. Fill the bounds with a neutral margin brush (`SystemColors.AppWorkspaceBrush` or a fixed mid-grey that works in light and dark).
    2. If `Scale > 0`, push `new MatrixTransform(Scale, 0, 0, Scale, ImageRectInView.X, ImageRectInView.Y)`, then push a clip to `(0, 0, imageW, imageH)`.
    3. Call `ShapeRenderer.DrawImage`, then `ShapeRenderer.Draw(document.Shapes)`, then the preview shape, and pop.
    4. Set `RenderOptions.SetBitmapScalingMode(this, Transform.IsPixelExact ? NearestNeighbor : HighQuality)` before rendering (on size/DPI change).
  - `InvalidateVisual()` on `document.Changed`, on size changes, and in `OnDpiChanged`.
  - **Mouse (Rectangle/Arrow):**
    - `OnMouseLeftButtonDown`: `Focus()`. Begin `AnnotationDrawingGesture.Begin(tool, e.GetPosition(this), Transform, color, width)`. If it returns null (margin, or Select/Text tool), do nothing. Otherwise `CaptureMouse()` so the drag continues outside the window.
    - `OnMouseMove` while capturing: `Update(position)`, preview = `Shape(constrained: Keyboard.Modifiers has Shift, completing: false)`, then `InvalidateVisual()`.
    - `OnMouseLeftButtonUp`: `Update(position)`. If `Shape(constrained, completing: true)` is non-null, `document.Add(shape)` (exactly one undo step). Clear the gesture and preview, then `ReleaseMouseCapture()`. The tool stays the same.
    - `OnLostMouseCapture` while a gesture is active (Alt+Tab, another window taking capture): abort, with no shape.
    - **Shift without moving:** the window's `PreviewKeyDown`/`PreviewKeyUp` for `LeftShift`/`RightShift` during a gesture rebuilds the preview at the last pointer position (port of `flagsChanged`, `AnnotationCanvasView.swift:121-123`).
  - Changing the tool during a drag aborts the drag (port of `currentTool.didSet`, `:8-16`).
  - `public bool IsDragging` is true while a drawing gesture is active (port of `isDragging`, `:23`). 0041 extends it with its move/resize drag, and 0043/0044 read it to refuse Save/Copy mid-drag. `public event EventHandler? DraggingChanged` is optional.
  - **Cursor:** `Cursors.Cross` over the image rect while Rectangle/Arrow is active, `Cursors.Arrow` elsewhere (in `OnMouseMove`/`OnQueryCursor`).
- **`windows/src/Snapper.Windows/Annotate/AnnotationWindow.xaml` + `.xaml.cs`**: `internal sealed partial class AnnotationWindow : Window`.
  - Standard resizable window (`ResizeMode.CanResize`, `ShowInTaskbar = true`, `WindowStartupLocation.Manual`, `UseLayoutRounding = true`, no owner). Title: `FileNamer` base name for the capture date without extension or collision suffix (for example `Screenshot 2026-11-02 at 19.18.04`). If 0026's `FileNamer` only returns full names, strip the extension with `Path.GetFileNameWithoutExtension`.
  - Layout: a `DockPanel` with the toolbar row on top and the `AnnotationCanvas` filling the rest. The toolbar row is a `DockPanel` too: a left `StackPanel` with the tools, a separator, the swatches + "+", a separator, and S/M/L; and a right-docked `StackPanel x:Name="ActionsPanel"`, empty until 0043. Everything is built from `AnnotationToolbarLayout`.
  - Tool buttons are `RadioButton`s with a `ToggleButton` look, so exactly one is checked. Content is a `TextBlock` with `FontFamily="Segoe Fluent Icons"`, the glyph, and its rotation. `ToolTip` and `AutomationProperties.Name` come from the layout. The Text button has `IsEnabled = false` until 0042.
  - Swatches are buttons whose content is a filled 16-DIP `Ellipse` with a 1-DIP border, and the highlighted one gets an accent ring (`SystemParameters.WindowGlassBrush` or the system accent). "+" is a button with the Add glyph. S/M/L are `RadioButton`s.
  - **All toolbar controls set `Focusable = false`**, so keyboard focus stays on the canvas (port of `refusesFirstResponder`). After any toolbar click, call `canvas.Focus()`.
  - `CurrentTool` property: setting it updates the canvas and checks the matching radio button. Clicking a tool button sets it.
  - Style state: `ToolbarStyleState` is initialized from `AppSettings.AnnotationColor`/`AnnotationStrokeWidth` when the window opens. `Pick(ToolbarStyleEvent)`:
    1. Ignore the pick if a drag is in progress (`AnnotationWindowController.swift:149-150`).
    2. `Reduce` the event.
    3. Persist `PersistColor`/`PersistStrokeWidth` through 0027's settings store (whatever update/save API 0027 exposes).
    4. Update the canvas's current color and width, refresh the swatch highlight and the S/M/L check from `DisplayedStyle`/`SwatchSelection`, then `canvas.Focus()`.

    Selection effects are ignored here (decision 8).
  - "+" → `ColorDialogPresenter.Pick(owner: this, initial: DisplayedStyle.Color)`. A non-null result → `Pick(ColorPicked(color))`.
  - **Keys** (`PreviewKeyDown` on the window):
    - V/R/A/T, mapped from `Key.V` etc. to the letter: `AnnotationTools.ForShortcut(letter, hasCtrlAltOrWin: Keyboard.Modifiers has Control/Alt/Windows, textToolEnabled: false)` → set `CurrentTool`, `e.Handled = true`.
    - Ctrl+W → `Close()`. This is a minimal inline check. 0043 replaces it with its `AnnotationExportShortcuts` router for Ctrl+S/C/W, so don't build a competing key-router type here.
    - Esc → abort an active drag, otherwise nothing.
    - Shift during a drag → preview rebuild.

    All other keys pass through.
  - `public bool HasOpenDialog` is true while a modal dialog owned by this window is open. In this spec that's only the Color dialog: set it around the `ColorDialogPresenter.Pick` call in `try/finally`. 0043/0044 use it to refuse Close/Copy & Close while a dialog is open.
  - `Closed` → notify the manager. The window releases its bitmap and document references.
  - **Placement** (all in physical pixels, via `SetWindowPos`; never WPF `Left`/`Top`/`Width`/`Height`):
    1. `new WindowInteropHelper(this).EnsureHandle()`.
    2. Move the hidden window into the target monitor's work area: `SetWindowPos(hwnd, 0, wa.X, wa.Y, 200, 200, SWP_NOZORDER | SWP_NOACTIVATE)`. If that monitor's DPI differs from the one the HWND was created on, WPF processes `WM_DPICHANGED` synchronously and rescales.
    3. Measure the chrome at the target DPI: `AdjustWindowRectExForDpi` with the window's actual style/ex-style and `GetDpiForWindow(hwnd)` gives the non-client size in pixels, ÷ scale = DIPs. Measure the toolbar row's `DesiredSize.Height` (after `Measure`) and add it to the vertical chrome.
    4. `minCanvasDip = (max(480, toolbar desired width), 300)`.
    5. Call `AnnotationWindowSizing.Frame(...)`, then `Cascaded(...)` against the manager's open windows' current top-lefts (`GetWindowRect`), with step `Round(24 × scale)`.
    6. Apply the final rect with `SetWindowPos(..., SWP_NOZORDER | SWP_NOACTIVATE)`.
    7. Set WPF `MinWidth`/`MinHeight` from the effective minimum + chrome in DIPs.
    8. Then `Show()`.

    Invisible Windows 11 resize borders are part of the outer rect. That's accepted: the visible frame is at most ~7 px smaller per side, and the frame stays inside the work area.
- **`windows/src/Snapper.Windows/Annotate/ColorDialogPresenter.cs`**: wraps `ChooseColor` (CsWin32 `PInvoke.ChooseColor` with `CHOOSECOLORW`).
  - `hwndOwner` = the annotation window. `rgbResult = ColorRefConversion.ToColorRef(initial)`. Flags `CC_RGBINIT | CC_FULLOPEN | CC_ANYCOLOR`.
  - `lpCustColors` points to a process-wide pinned `uint[16]` (a static `GCHandle`, or a `fixed` block for the call's duration).
  - Returns `ColorRefConversion.FromColorRef(rgbResult)` on OK and `null` on Cancel or error.
  - The dialog is modal to its owner. Other annotation windows and the global shortcuts keep working.
- **`windows/src/Snapper.Windows/Annotate/AnnotationWindowManager.cs`**: `internal sealed class AnnotationWindowManager`, owned by `AppCoordinator`.
  - `IReadOnlyList<AnnotationWindow> Windows` (0043 uses it for the Exit count and the shutdown block reason).
  - `void Open(CapturedBitmap captured, DisplayInfo? captureDisplay, DateTime captureDate)` (use 0031's captured-image type, whatever its name). Steps:
    1. Resolve the target display: the capture display if it's still in `DisplayInfoProvider`'s current list (match by device name/handle as 0028 identifies monitors), else the primary.
    2. Create an `AnnotationDocument(new PixelSize(w, h), captureDisplay scale)`. `PixelsPerDip` is the **capture** monitor's scale, even if the window falls back to another monitor.
    3. Create the window, place it, add it to `Windows`, and `Show()`.
    4. Bring it to the front with focus: `Activate()`. If `GetForegroundWindow() != hwnd`, call `SetForegroundWindow(hwnd)`. If it's still not foreground, toggle `Topmost = true` then `false`, so it's at least visible on top (the Windows counterpart of 0022's `orderFrontRegardless`). Then `canvas.Focus()` and `Keyboard.Focus(canvas)`.
  - Removes a window from `Windows` when it closes. `CloseAll()` closes every window without a prompt, and `AppCoordinator.Exit` calls it (until 0043).
- **`windows/src/Snapper.Windows/App/AppCoordinator.cs`**: the Ctrl+Shift+6 path. When the overlay returns a `SelectionResult` for `CapturePurpose.Annotate`:
  1. Capture through the **same** method the screenshot path uses (0031/0032: rectangle crop or window via WGC cropped to the extended frame bounds, overlay excluded). Extract a shared `CaptureAsync(SelectionResult)` if 0031 inlined it.
  2. Then, on the dispatcher, play `ShutterSound` if `PlayShutterSound` is on, and call `annotationWindowManager.Open(bitmap, selection display, capture time)`.
  3. Don't encode PNG, write a file, or touch the clipboard, whatever the destination.
  4. On failure, show 0031's "Couldn't take screenshot" alert and open no window.

  The app mode returns to idle when the overlay closes (0029), and open annotation windows don't block any shortcut.
- **`windows/src/Snapper.Windows/NativeMethods.txt`**: add, unless already present: `SetWindowPos`, `GetWindowRect`, `AdjustWindowRectExForDpi`, `GetDpiForWindow`, `GetWindowLong` (or `GetWindowLongPtr`, for the style passed to `AdjustWindowRectExForDpi`), `SetForegroundWindow`, `GetForegroundWindow`, `ChooseColor`, `CHOOSECOLORW`, `CHOOSECOLOR_FLAGS`, `SET_WINDOW_POS_FLAGS`. If CsWin32's pointer signatures need it, `AllowUnsafeBlocks` is enabled for `Snapper.Windows` only (it may already be on from earlier specs).

### Project

- New files go under the folders above. `Snapper.Windows` already references `Snapper.Core`. No new NuGet packages.
- No PRD change. All behavior is already specified in §8 and §13. The decisions above are interpretations of it.

## Acceptance criteria

- [ ] Ctrl+Shift+6 (or tray → Annotate) shows the overlay. Selecting a rectangle, or a window in window mode, opens a new annotation window in front of the previously active app with keyboard focus. Pressing R/A/V right away switches tools without a click.
- [ ] The image is identical to a Ctrl+Shift+4 capture of the same selection: native physical pixels, a window without shadow and without covering windows, and no Snapper overlay.
- [ ] Ctrl+Shift+6 writes no file and leaves the clipboard unchanged for every *Screenshot destination*. The shutter plays iff *Play shutter sound* is on.
- [ ] The window opens centered on the capture monitor's work area (the primary monitor if the capture monitor is gone). Image area + chrome is at most about 80% of the work area, the image is never larger than its natural DIP size, and small captures sit at 100% in a window of at least the minimum size, centered with neutral margins.
- [ ] The window is titled like `Screenshot 2026-11-02 at 19.18.04`, is resizable, appears in the taskbar and Alt+Tab, and has a toolbar: Select / Rectangle / Arrow / Text (Text disabled) with Segoe Fluent Icons glyphs and "(V)"-style tooltips, 8 swatches + "+", and S/M/L. There are no Save/Copy buttons yet.
- [ ] Several annotation windows can be open at once. A new one whose position would exactly match an open one is cascaded down-right. Closing one doesn't affect the others, and Snapper keeps running in the tray after the last one closes.
- [ ] Rectangle (selected in every new window) draws an outline in the current color and width. Shift makes a square, also when pressed or released mid-drag without moving the mouse.
- [ ] Arrow draws a butt-capped shaft with a filled head. Shift snaps to multiples of 45°.
- [ ] After drawing, the same tool stays active. A click, or a drag below 3 view DIPs, creates nothing at any window size. Drags starting in the margin do nothing, and endpoints dragged outside clamp to the image edge, including with Shift.
- [ ] A swatch, "+" (Windows Color dialog), or S/M/L changes the style of the next shapes and is remembered: a new window (and a restarted Snapper) starts with the last pick. Defaults are red and M. Other already-open windows keep their own current style.
- [ ] Resizing rescales the image and shapes together. Enlarging beyond natural size leaves the image at 100% centered. At natural size on a monitor whose scale equals the capture's, the image is pixel-sharp (1:1 physical pixels).
- [ ] Ctrl+W, Alt+F4, and the X close the window (no prompt yet). Esc doesn't close it.
- [ ] The canvas exposes `IsDragging` and the window exposes `HasOpenDialog` (true while the Color dialog is open), for 0041/0043/0044.
- [ ] Each completed shape is one undo step in the document (unit-tested here and in 0039; Ctrl+Z is wired in 0041).
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` pass.

## Edge cases

- **Mixed DPI:** capture on a 150% monitor, window on that monitor. `PixelsPerDip` is 1.5, M strokes are 6 px in the image, and the window size is computed in that monitor's DIPs. If the capture monitor was disconnected and the window falls back to a 100% primary, the image's natural size stays `pixels ÷ 1.5` DIPs (sizing case 5 below).
- **Dragging the window to another monitor** with a different DPI: WPF rescales. `OnDpiChanged` recomputes `viewScale`, which can switch between NearestNeighbor and HighQuality. The model is untouched, and shapes stay on the same image content.
- **Negative-origin monitors** (left of or above the primary): the work area has negative X/Y. The frame is computed and set in physical pixels and must land on that monitor.
- **Window captures spanning monitors:** the target monitor is the one `WindowDisplayRule` chose, whose scale is the capture's `PixelsPerDip`.
- **HWND created on another monitor:** step 2 of Placement moves it first, so WPF's `WM_DPICHANGED` rescale happens before the final `SetWindowPos`, and the final size isn't rescaled again.
- **Very wide/tall captures:** the fit is limited by one dimension, the other is raised to the minimum, and the image is letterboxed.
- **Tiny captures** (10×10 px): the window is at its minimum size, and the image is drawn 1:1 in the center.
- **Tiny work area** (smaller than the minimum + chrome): the minimum shrinks to the available area (sizing case 6) and the window fits.
- **Drag continues outside the window** thanks to mouse capture, and its end clamps to the image edge. If capture is lost (Alt+Tab, a UAC prompt), the drag is aborted without a shape.
- **Ctrl+Shift+4/5/6 while an annotation window is focused:** the overlay (topmost) appears above it. A new annotate selection opens another window in front.
- **Color dialog open** while Ctrl+Shift+6 fires: the overlay appears and the new window opens. The dialog stays modal only to its own owner.
- **Foreground lock:** if Windows still refuses the foreground (for example, the user clicked another app during a slow capture), the Topmost toggle shows the window on top and its taskbar button flashes. That's accepted.
- **Very large captures** (8K): `OnRender` draws the frozen bitmap once per frame in retained mode. If resizing is sluggish, cache a scaled bitmap per view size (implementer's choice, no behavior change).
- **Capture failure** (window closed between selection and capture): the alert shows and no window opens. Focus isn't restored (accepted, as in 0022).
- **Key repeat** of V/R/A just re-selects the same tool. AltGr letters (Ctrl+Alt) never switch tools.
- **Exit** with open windows closes them without a prompt (until 0043).

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`, xUnit; compare doubles with a 1e-9 tolerance unless stated):
  - `CanvasTransformTests.cs` (port of `CanvasTransformTests.swift:6-48`, plus DPI cases):
    1. `ExactFitAndRoundTrips`: image 400×300, ppd 1, view 400×300, viewScale 1 → `Scale` 1, rect `(0,0,400,300)`. View→image→view round trip of (0,0), (400,300), and (200,150) is the identity. `IsPixelExact` is true.
    2. `LetterboxesAndClampsBothAxes`: image 1600×900, ppd 2, view 400×400, **viewScale 2** → `Scale` 0.25, rect `(0, 87.5, 400, 225)`. View (200,200) → image (800,450). View (0,87.5) → (0,0). Inside (200,200) is true and (0,0) is false. Clamp (−20,−20) → (0,0) and (500,500) → (1600,900). `ImageRect(ViewRect((50,60,300,100)))` round-trips, and `ViewLength(8)` = 2.
    3. `PortraitLetterbox`: image 600×1200, ppd 1, view 600×300, viewScale 1 → rect `(225, 0, 150, 300)`.
    4. `NeverUpscales` (theory ppd 1 and 2): image 200×100, view 800×800, viewScale 1 → `Scale == 1/ppd`, centered at (400,400). With ppd 2 the rect is `(350, 375, 100, 50)`.
    5. `OffsetSnapsToPhysicalPixels`: image 300×150, ppd 1.5, view 801×601, viewScale 1.5 → `Scale` = 1/1.5. The raw offset (300.5, 250.5) becomes (451/1.5, 376/1.5) = (300.6667, 250.6667), and `X×1.5` and `Y×1.5` are integers. `IsPixelExact` is true.
    6. `SameCaptureOnLowerDpiMonitorIsNotPixelExact`: the same image and ppd on view 801×601, viewScale 1 → `Scale` = 1/1.5, offset snapped to whole DIPs (301, 251), `IsPixelExact` false.
    7. `HundredPercentCaptureOn200PercentMonitor`: image 200×100, ppd 1, view 800×800, viewScale 2 → `Scale` 1 (not above 1/ppd), `IsPixelExact` false.
    8. `InvalidInputsAreSafe`: an image or view of 0×0, (−1,100), or (∞,100), ppd 0, and viewScale 0, −1, or NaN → `Scale` 0. `ImagePoint`, `ClampedImagePoint`, and `ImageRect` return null, and `IsInsideImage` is false (no NaN or ∞ anywhere).
  - `AnnotationWindowSizingTests.cs` (chrome `(16, 80)` DIPs, min canvas `480×300` unless stated):
    1. `LargeImageCappedAt80PercentOn100Percent`: image 3840×2160 @1.0, work area `(0,0,1920,1040)`, scale 1 → canvas 1336×752, window `(284, 104, 1352, 832)`.
    2. `LargeImageOn150PercentMonitor`: image 2880×1620 @1.5 (1920×1080 DIPs), work area `(0,0,2880,1680)`, scale 1.5 → canvas 1450×816, window `(341, 168, 2199, 1344)`. Pins `AwayFromZero` on the 340.5 midpoint.
    3. `SmallImageNotUpscaledOn200Percent`: image 400×300 @2 (200×150 DIPs), work area `(0,0,3840,2160)`, scale 2 → canvas 480×300 (minimum), window `(1424, 700, 992, 760)`.
    4. `ExactNaturalSizeWhenItFits`: image 1200×800 @1, work area `(0,0,2560,1400)`, scale 1 → canvas exactly 1200×800.
    5. `FallbackMonitorWithDifferentScaleUsesImageDips`: image 2880×1620 @1.5 on the 100% work area of case 1 → canvas 1336×752, window `(284,104,1352,832)` (same as case 1, because the sizing is in DIPs).
    6. `NegativeOriginMonitor`: case 1's image on work area `(−1920, −300, 1920, 1040)`, scale 1 → window `(−1636, −196, 1352, 832)`, fully inside the work area.
    7. `PortraitHeightLimited`: image 1000×3000 @1, work area `(0,0,1920,1040)` → canvas width = 480 (raised to the minimum), height 752. The aspect ratio of the fitted image area is preserved (fit = 752/3000).
    8. `TinyWorkArea`: image 5120×2880 @1, work area `(0,0,640,400)` → canvas 480×300, window `(72, 10, 496, 380)`. Work area `(0,0,320,240)` → canvas 304×160, window `(0,0,320,240)`.
    9. `Cascade`: frame `(341,168,2199,1344)` in work area `(0,0,2880,1680)`, step 36 (24 × 1.5). No existing → unchanged. `[(341,168)]` → `(377,204,…)`. `[(341,168),(377,204)]` → `(413,240,…)`. A frame flush with the work area's bottom-right edge and a matching existing top-left → unchanged (stops before leaving). Also negative-origin work area `(−1920,−300,1920,1040)` with step 24 → shifted by (24,24).
  - `AnnotationToolTests.cs` (port of `AnnotationToolTests.swift:5-20`): v/r/a/t and V/R/A/T map to Select/Rectangle/Arrow/Text with `textToolEnabled: true`. Any of them with `hasCtrlAltOrWin: true` → null. "t", "x", "", and "rr" with `textToolEnabled: false` → null. Segment index round-trips for all four, and −1 and 4 → null. `CreatesShapesByDragging` is true only for Rectangle and Arrow, and `DefaultTool == Rectangle`.
  - `AnnotationDrawingGestureTests.cs` (port of `AnnotationDrawingGestureTests.swift:10-79`; transform = image 1600×900, ppd 2, view 400×400, viewScale 2):
    1. `MarginsAndPassiveToolsNeverBegin`: view (0,0) → null for every tool. Select/Text at (200,200) → null.
    2. `ThresholdsUseViewDipsAtAnyZoom`: for ppd 1 and 2 and view width 200 and 800 (image 800×800, viewScale 1), for Rectangle and Arrow, starting at the image rect's origin: no move → null. +(2,1) → null. +(20,2) → shape only for Arrow. +(20,20) → shape.
    3. `ShiftUpdatesWithoutMovementAndCompletionIsOneUndo`: start image (100,100), update to (300,200). Free → rect (100,100,200,100). Constrained → (100,100,200,200). Asking free again returns the free rect (no state change). Adding the completed constrained shape to an `AnnotationDocument` gives 1 shape. Undo → empty with `!CanUndo`. Redo → the square. Tool is still Rectangle.
    4. `OutsidePointsAndConstraintsStayInsideImage`: start (1500,800), update to view (900,900) → `Current` (1600,900). For both tools, constrained and free, every handle is within `[0,1600]×[0,900]`. Edge square from (1500,100) toward (1600,500) → (1500,100,100,100). An edge arrow from (1500,700) toward (1600,850), constrained → end (1600,800) ±1e-8. An arrow from (100,100) to (300,250), constrained → `|dx − dy| < 1e-8`.
  - `ToolbarStyleStateTests.cs` (port of `ToolbarStyleStateTests.swift:6-49`, all four tests): selection only changes the displayed style; explicit picks persist and apply only actual changes; every preset maps to `Preset(i)`, grey (0.5,0.5,0.5) → `Custom`, and copies are independent (`with`/struct copy); undo restyle of the selection doesn't revert picks. Also `InitialDefaultsHighlightRedAndM`: state from red/M → `SwatchSelection == Preset(0)` and `DisplayedStyle.StrokeWidth == M`.
  - `ColorRefConversionTests.cs`: red preset (1, 59/255, 48/255) → `0x00303BFF`. `FromColorRef(0x00FF7A00)` == blue preset (0, 122/255, 1). Every preset round-trips exactly. The high byte is ignored (`0xFF0000FF` → pure red), and alpha is dropped (0.5-alpha white → `0x00FFFFFF`).
  - `ShapeDrawingPlanTests.cs`:
    1. Rectangle `(10,10,50,30)` S at ppd 1 → one `StrokeRect` with that rect and thickness 2. M at ppd 2 → thickness 8.
    2. Arrow (10,50)→(90,50), M, ppd 1 → `StrokeLine` (10,50)→(70,50), thickness 4, then `FillPolygon` [(90,50), (70,58), (70,42)].
    3. Short arrow (10,50)→(15,50), M, ppd 1 → **only** `FillPolygon` [(15,50), (10,52), (10,48)] (no zero-length shaft).
    4. Arrow at ppd 1.5, M → thickness 6, head length 3·6 + 8·1.5 = 30.
    5. Text shape → an empty list.
  - `AnnotationToolbarLayoutTests.cs`: tools are in the order Select, Rectangle, Arrow, Text. Tooltips are exactly "Select (V)", "Rectangle (R)", "Arrow (A)", "Text (T)". Every glyph is a single character in the Private Use Area (U+E000–U+F8FF). Arrow rotation is −45. There are 8 swatch tooltips in preset order ("Red" … "White"). Stroke labels are S/M/L. `TrailingActions` is empty.
  - `SelectionFocusPolicyTests.cs` (only if this spec adds the type; port of macOS 0022's four tests): cancelled → true for every purpose; Screenshot and Record selected → true; Annotate selected → false.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` both succeed (they run on macOS too).
- **Manual verification** (Windows 11 machine; ideally two monitors at 100% and 150%, one left of the primary; can't be automated):
  1. Click into Notepad so it's in the foreground. Press Ctrl+Shift+6 and drag a ~600×400 area. **Expected:** the shutter click (setting on), and a window titled `Screenshot <date> at <time>` in front of Notepad with an active title bar and a taskbar button. No new file in `%USERPROFILE%\Pictures\Screenshots`, and pasting in Notepad shows the old clipboard text.
  2. Without clicking, press A and drag on the image. **Expected:** the Arrow button is checked and an arrow is drawn. Press R and V: the buttons follow. Press T: nothing (Text is greyed out).
  3. Turn *Play shutter sound* off and set the destination to *Save to file*, then repeat step 1. **Expected:** no sound and still no file.
  4. Ctrl+Shift+6, Space, then click a window that's partly covered by another. **Expected:** only that window, with no shadow and nothing covering it.
  5. Ctrl+Shift+6 and select almost the whole monitor. **Expected:** the window is about 80% of the work area, centered, with the aspect ratio preserved and nothing behind the taskbar.
  6. Ctrl+Shift+6 and select a ~100×100 area. **Expected:** the window is at its minimum size, and the image is crisp (not blurry), 1:1, centered in grey margins.
  7. Mixed DPI: capture on the 150% monitor, then on the 100% monitor left of the primary. **Expected:** each window opens on its capture monitor with the right size. At natural size on the 150% monitor the image is pixel-sharp. Drag a window to the other monitor: the image and shapes stay aligned, and only the physical size changes.
  8. Open three windows with three captures of the same area. **Expected:** they're cascaded down-right, drawing in one doesn't affect the others, and closing one (Ctrl+W, Alt+F4, X) leaves the rest working. After closing all, the tray icon is still there.
  9. Rectangle (the default): drag → a red, medium outline. Hold Shift → a square. Press and release Shift mid-drag without moving → the preview toggles. The tool stays Rectangle. A single click creates nothing.
  10. Arrow with Shift → angles snap to 0/45/90°. The head is filled, and nothing pokes past the tip.
  11. Start a drag in the grey margin → nothing. Drag from inside the image to outside the window → the shape ends at the image edge. Press Esc mid-drag → no shape, and the window stays open.
  12. Click the blue swatch and L, then draw. **Expected:** a thick blue shape, with the blue swatch ringed. Click "+", pick a custom color, and click OK → "+" is highlighted and the next shape uses it. Cancel the dialog → nothing changes. Open a new annotation window → it starts with the custom color and L. Exit Snapper, start it again, Ctrl+Shift+6 → still the custom color and L. An already-open older window keeps its own style.
  13. Resize a window smaller and larger. **Expected:** the image and shapes scale together. Beyond natural size, the image stays at 100% and centered.
  14. With an annotation window focused, press Ctrl+Shift+4. **Expected:** the overlay appears above it and the screenshot works.
  15. Repeat step 1 ten times in a row, alternating between Notepad and File Explorer in the foreground. **Expected:** the annotation window is focused every time.
  16. Ctrl+Shift+6, then Esc. **Expected:** no window, and Notepad gets its focus back.

## Out of scope

- Select/move/resize, handles, Delete/Backspace, Esc deselect, applying color/width to the selection, Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z: **0041-windows-annotation-editing**.
- The Text tool and text rendering (a `ShapeDrawingPlan` text primitive, the WPF text measurer): **0042-windows-annotation-text-tool**.
- Full-resolution export, Save (Ctrl+S) and Copy (Ctrl+C) buttons in `ActionsPanel`, "Discard changes?", the Exit confirmation, and the "Unsaved annotations" shutdown block reason: **0043-windows-annotation-export** (it reuses `ShapeRenderer`, `AnnotationToolbarLayout.TrailingActions`, and `AnnotationWindowManager.Windows`).
- Copy & Close and Ctrl+Enter: **0044-windows-annotation-copy-and-close**.
- The shape model, constraints, and arrow geometry (**0038**); the document, undo, and dirty state (**0039**); capture, shutter, and alerts (**0031**); window mode (**0032**); the overlay and its focus restore (**0030**).
- Zoom controls and an app icon for the window (**0047** adds the icon). Neither is in the PRD for this spec.
