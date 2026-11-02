# 0019 — Add the Text tool: place, edit, re-edit, and render outlined text annotations

> Source: docs/PRD.md §8.2 (Text row, Esc rule, ⌘C rule, undo covers text edits, color/stroke apply to selection), §8.3 (Text, in full), §8.4 (export resolution)
> Depends on: 0003-app-settings, 0015-annotation-shape-model, 0016-annotation-document-undo, 0017-annotation-window-canvas, 0018-annotation-editing

## Context

No app code exists yet. When this spec is picked up, the lower-numbered specs will have delivered:

- **0003** `Snapper/Annotate/Model/RGBAColor.swift` (pure, Codable, 8 presets) and `Snapper/Annotate/Model/StrokeWidth.swift` (`.s/.m/.l` → 2/4/8 image points for lines, and text sizes 16/24/36 image points).
- **0015** `Snapper/Annotate/Model/AnnotationShape.swift`: shapes with `id`, `color: RGBAColor`, `strokeWidth: StrokeWidth`, and a text kind (`TextBox`) holding `origin`, `string`, and `measuredSize`, all in **image pixel coordinates** with a top-left origin and y growing downward. Hit-testing uses `origin` + `measuredSize`. Text has no resize handles (PRD §8.2 lists handles only for rectangles and arrows).
- **0016** `Snapper/Annotate/Model/AnnotationDocument.swift`: image pixel size, `pixelsPerPoint`, shapes, selection, undo/redo, dirty flag; each add / replace / remove is one undo step.
- **0017** `AnnotationWindowController`, `AnnotationCanvasView`, pure `CanvasTransform` (image pixels ↔ view points, with a `scale` = view points per image pixel), `AnnotationTool` (incl. `.text`, shortcut T), and `ShapeRenderer`, which draws shapes in image pixel coordinates into a `CGContext` and is reused by export (0020).
- **0018** selection, move, delete, color swatches + `NSColorPanel`, S/M/L control, apply-to-selection, remembered color/width via `AppSettings`, undo/redo wiring, Esc deselects.

If a sibling spec ended up with a slightly different member name than used here, use the sibling's actual name; the behavior in this spec is the contract.

What is missing: there is no way to create or edit text, and `ShapeRenderer` does not draw text (or draws a placeholder). PRD §8.3 requires:

> Clicking with the Text tool places a text box and starts editing. Enter adds a new line. **Esc** or clicking outside the box commits the text, and empty text boxes are removed. Double-clicking existing text edits it again. Text uses a bold system font in the current color. Its size follows the stroke width: S = 16pt, M = 24pt, L = 36pt, in image points. Text has a thin contrasting outline (white around dark text, black around light text) and no background.

And PRD §8.2: Esc while a text box is being edited commits (it does not deselect); ⌘C while editing copies text, not the image; undo covers text edits; color/stroke changes apply to the selected shape.

## User story

As a Snapper user annotating a capture, I press T, click on the image, type a (possibly multi-line) label, and press Esc or click elsewhere. The label appears in bold, in my current color and size, with a thin contrasting outline so it stays readable on any background. I can double-click it later to change it, and every text change can be undone in one step.

## Design decisions (made in this spec)

1. **The document is not mutated while editing.** The in-progress text lives in a `TextEditingSession` owned by the controller. On commit, exactly one document mutation (or none) is applied. This gives one undo step per editing session without needing an undo-grouping API from 0016. (If 0016 offers `beginEdit/commitEdit` grouping, the implementer may use it, but the observable undo behavior below must be identical.)
2. **Commit outcomes** (pure `TextCommitDecision`):

   | Session started from | Committed text | Result | Undo steps |
   |---|---|---|---|
   | new box | empty | `.discard`: no shape added | 0 |
   | new box | non-empty | `.add(shape)` | 1 (undo removes the box) |
   | existing box | empty | `.delete(id)` | 1 (undo restores the original box) |
   | existing box | identical text, color, and stroke width | `.noChange` | 0 |
   | existing box | anything else changed | `.update(shape)` | 1 (undo restores the previous text/color/size) |

   "Empty" means the string contains only whitespace and newlines (`trimmingCharacters(in: .whitespacesAndNewlines).isEmpty`). Non-empty text is stored **exactly as typed** (no trimming). Only `.add`, `.update`, `.delete` mark the document dirty.
3. **Origin anchor:** `TextBox.origin` is the **top-left corner of the text layout box** (top of the first line fragment, left edge of the glyph run; *not* the baseline), in image pixels, y down. `measuredSize` is the layout box size (width of the widest line × total line height) in image pixels, without the outline. A box with empty text still has the height of one line (needed for caret placement while editing).
4. **New-box placement:** a click at image point `p` gives `origin = (p.x, p.y − lineHeight / 2)`, so the caret is vertically centered on the click. The origin is then clamped so that `0 ≤ origin.x < imageWidth` and `0 ≤ origin.y ≤ imageHeight − lineHeight` (when the image is shorter than one line, `origin.y = 0`). Text may grow past the right/bottom edge of the image; it is clipped there in the canvas and on export.
5. **No automatic wrapping.** Lines break only where the user presses Enter. The text box grows horizontally and vertically with its content.
6. **Font sizes.** Font size in image pixels = `StrokeWidth` text size (16/24/36 image points) × `document.pixelsPerPoint`. The editor's on-screen font size = font pixel size × `CanvasTransform.scale`. Font = `NSFont.systemFont(ofSize:weight: .bold)`.
7. **Contrasting outline rule** (pure `ContrastingOutline`): compute WCAG 2.x relative luminance of the text color's sRGB components, `L = 0.2126 R + 0.7152 G + 0.0722 B` with each channel linearized (`c ≤ 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055)^2.4`). If `L ≥ 0.5` the text is "light" → **black** outline; otherwise "dark" → **white** outline. The outline's alpha equals the text color's alpha. Threshold 0.5 is the midpoint of the luminance scale. The pure max-contrast crossover (L ≈ 0.179) would give black outlines to red, blue, and purple, which reads as heavy and muddy on saturated annotation colors. The 0.5 midpoint gives saturated mid-tones the white halo that screenshot annotation tools use, and keeps black outlines for genuinely light colors (yellow, white).
8. **Outline thickness:** the visible outline outside the glyph is **6.25% of the font size** (S = 1, M = 1.5, L = 2.25 image points). It is drawn in **two passes**: first a stroke-only pass in the outline color with `NSAttributedString.Key.strokeWidth = +12.5` (a percentage of font size; the stroke is centered on the glyph path, so 6.25% lands outside it), with the context's line join set to `.round` to avoid miter spikes; then a normal fill pass in the text color on top. The fill covers the inner half of the stroke, so glyph shapes keep their full bold weight. A single pass with a negative `strokeWidth` (fill + stroke) is **not** used, because it paints the stroke over the glyph interior and visibly thins bold text at small sizes. At about 6% of the em the outline is thinner than a bold stem (roughly 13–15% of the em), so it reads as an edge, not a border.
9. **Clicks with the Text tool:**
   - On empty canvas (no text shape hit): place a new box and start editing.
   - On an existing text shape (single or double click): start editing **that** shape, with the caret at the clicked character. See *Open questions* (Q1).
10. **Double-click with the Select tool** on a text shape starts editing it, with the caret at the clicked character. The first click of the double-click selects it as usual (0018); a zero-distance "move" from that click must not register an undo step. Double-clicking text with the Rectangle or Arrow tool does not edit (those tools draw).
11. **Clicking outside the text view while editing** (anywhere on the canvas) commits, and **the click is consumed**: it does not also place a new box, select, or start drawing. A toolbar *tool* button click commits, then switches tools. Color swatches, the "+" color panel, and S/M/L do **not** commit (see 13).
12. **After commit,** the Text tool stays active (PRD §8.2 "active tool stays selected"). If the session was started from the Select tool, the (still existing) shape stays selected; otherwise nothing is selected.
13. **Color / stroke changes while editing** apply to the box being edited, update the editor live (and re-measure for a stroke change), and become part of that session's single undo step. They also update the remembered color/width in `AppSettings`, like any other color/width change (0018). Swatch, S/M/L, and "+" controls must not take first responder (`refusesFirstResponder = true`), so the text view keeps focus. The window resigning key (for example because `NSColorPanel` became key) does **not** commit.
14. **Undo while editing:** the text view has its own private `UndoManager` per session (returned from `NSTextViewDelegate.undoManager(for:)`), so ⌘Z/⇧⌘Z undo typing inside the box only and never touch the document's stack. That manager is thrown away at commit. Document-level undo resumes after commit.

## Scope & changes

### Pure (Annotate/Model, unit-tested, no AppKit)

- **`Snapper/Annotate/Model/ContrastingOutline.swift`**: `enum ContrastingOutline` with
  - `static func relativeLuminance(of: RGBAColor) -> Double` (WCAG formula above, components clamped to 0…1);
  - `static func color(for: RGBAColor) -> RGBAColor`: black or white (alpha = input alpha), threshold `L ≥ 0.5 → black`;
  - `static let outlineFractionOfFontSize = 0.0625` and `static let strokeWidthPercent = 12.5` (= 2 × fraction × 100, the `NSAttributedString` stroke value for the stroke-only pass).
- **`Snapper/Annotate/Model/TextMetrics.swift`**: `enum TextMetrics` with
  - `static func fontPixelSize(for: StrokeWidth, pixelsPerPoint: Double) -> Double` (16/24/36 × ppp, using 0003's text-size mapping; do not duplicate the constants);
  - `static func editorFontSize(fontPixelSize: Double, canvasScale: Double) -> Double`;
  - `static func newBoxOrigin(click: Point, lineHeight: Double, imageSize: Size) -> Point` (rule 4; use the point/size types 0015 uses).
- **`Snapper/Annotate/Model/TextMeasuring.swift`**: `protocol TextMeasuring { func layoutSize(of text: String, fontPixelSize: Double) -> Size; func lineHeight(fontPixelSize: Double) -> Double }`, so model-level code and tests can use a fake measurer.
- **`Snapper/Annotate/Model/TextCommit.swift`**:
  - `struct TextEditDraft` (`original: AnnotationShape?` — nil for a new box — plus current `string`, `origin`, `color`, `strokeWidth`, and `measuredSize`);
  - `enum TextCommitDecision { case discard, noChange, add(AnnotationShape), update(AnnotationShape), delete(ShapeID) }` with `static func decide(_ draft: TextEditDraft) -> TextCommitDecision` implementing the table in decision 2 (the shape keeps the original `id` on update);
  - `extension AnnotationDocument { mutating func apply(_ decision: TextCommitDecision) }` (or the equivalent for a class), using 0016's add / replace / remove so each non-trivial decision is exactly one undo step and `.discard`/`.noChange` leave the undo stack and dirty flag untouched.
- **Stroke-width change on an existing text shape (extends 0018):** when 0018's apply-stroke-width-to-selection targets a text shape, `measuredSize` must be recomputed through an injected `TextMeasuring`, and the new size stored in the same single undo step. Color changes don't change size.

### Platform glue (Annotate/, manually verified)

- **`Snapper/Annotate/TextStyle.swift`** (`@MainActor` where needed): one place that builds the attributes used for **measuring, editing, and rendering**, so that all three lay out identically. It provides the bold system font at a given size, the fill color, a paragraph style with `lineBreakMode = .byClipping`, and no extra spacing. It also builds the stroke-pass attributes (outline color + `strokeWidth = ContrastingOutline.strokeWidthPercent`) and the fill-pass attributes.
- **`Snapper/Annotate/AppKitTextMeasurer.swift`**: `struct AppKitTextMeasurer: TextMeasuring` measures with `NSAttributedString.boundingRect(with: .greatestFiniteMagnitude size, options: [.usesLineFragmentOrigin, .usesFontLeading])` using `TextStyle`, rounds up to whole pixels, and gives empty text one line height (`NSLayoutManager().defaultLineHeight(for:)` or the font's ascender − descender + leading; pick one and use it everywhere).
- **`ShapeRenderer` (from 0017), text case:** draws the attributed string in image pixel space at full font pixel size (so export in 0020 is full resolution, PRD §8.4). Pass 1 is stroke-only in the outline color (line join `.round`), pass 2 is fill. Use `NSAttributedString.draw(with:options:[.usesLineFragmentOrigin, .usesFontLeading])` inside an `NSGraphicsContext(cgContext:flipped:)` whose `flipped` flag matches 0017's image-space CTM (y-down → `flipped: true`), with the rect `origin`/`measuredSize`. It must look the same in the canvas and in an offscreen export context. No background is drawn. The shape currently being edited is **skipped** by the canvas (the editor shows it instead); export never skips anything.
- **`Snapper/Annotate/AnnotationTextView.swift`**: `final class AnnotationTextView: NSTextView`, an overlay subview of `AnnotationCanvasView`:
  - `drawsBackground = false`, no enclosing scroll view, `isRichText = false`, `importsGraphics = false`, `allowsUndo = true`;
  - `textContainerInset = .zero`, `textContainer.lineFragmentPadding = 0`, `widthTracksTextView = false`, container size `greatestFiniteMagnitude` on both axes, `isHorizontallyResizable = isVerticallyResizable = true`; the frame is resized to fit the content after each change (minimum width ≈ 2 view points so the caret shows);
  - automatic spelling correction, continuous spell checking, grammar checking, quote/dash/text replacement, and link detection are all off;
  - typing attributes come from `TextStyle` at the **editor** font size, with fill color and an approximated outline via `.strokeColor` + negative `.strokeWidth = −ContrastingOutline.strokeWidthPercent`. The editor only approximates the final look (the glyph interior may look slightly thinner while typing); layout, position, and size must match the committed rendering exactly;
  - Enter/Return inserts a newline (NSTextView's default `insertNewline:` for a non-field-editor text view; make sure it is not routed elsewhere);
  - Esc commits: handle both `cancelOperation(_:)` and `complete(_:)` (NSTextView maps Esc to completion) in `textView(_:doCommandBy:)` and return `true`.
- **`Snapper/Annotate/TextEditingController.swift`**: `@MainActor final class TextEditingController`, owned by the annotation window controller or canvas. It holds the current `TextEditDraft` (as `TextEditingSession`) and the text view. It:
  - `begin(newAt imagePoint:)` and `begin(editing shapeID:, caretAt imagePoint:)`: create and position the text view, make it first responder, and place the caret (`characterIndexForInsertion(at:)`). It tells the canvas which shape id to skip;
  - `isEditing: Bool` is exposed to the canvas, toolbar, and window controller;
  - on `textDidChange`, re-measures through `AppKitTextMeasurer` (image pixels), updates the draft, and resizes the text view;
  - `applyColor(_:)` / `applyStrokeWidth(_:)` update the draft and the typing attributes of the whole text (font size, color, outline) live;
  - `layout(using: CanvasTransform)`: called by the canvas on every transform change (window resize). It sets the text view frame origin from `origin` mapped to view points and the font size from `TextMetrics.editorFontSize`;
  - `commit()`: ends editing, computes `TextCommitDecision.decide`, calls `document.apply`, removes the text view, restores first responder to the canvas, sets selection per decision 12, and redraws. It is idempotent (a second call does nothing). It is **public so 0020** can call it before save/copy/close.
- **`AnnotationCanvasView` (0017/0018) changes:**
  - `mouseDown` while `isEditing` (the click is outside the text view, since clicks inside go to the text view) → `commit()` and return (the click is consumed);
  - Text tool: hit-test text shapes first (0015). A hit starts `begin(editing:)`, otherwise `begin(newAt:)` with `TextMetrics.newBoxOrigin`;
  - Select tool: `event.clickCount == 2` on a text shape starts `begin(editing:)`;
  - skip drawing the shape being edited; call `layout(using:)` whenever the transform changes;
  - single-key tool shortcuts (V/R/A/T) and ⌫ delete-shape must not fire while the text view is first responder. They must reach the text view as typing. If 0017/0018 implemented them as menu/toolbar key equivalents rather than in the canvas `keyDown`, gate them on `!isEditing`;
  - Esc while editing is handled by the text view (commit). The 0018 "Esc deselects" path only runs when not editing.
- **`AnnotationWindowController` / toolbar (0017/0018):** enable the Text tool button and the T shortcut if 0017 left them inert. A tool switch commits first. The color/width controls route to `TextEditingController` when `isEditing`, and to the selection otherwise. Set `refusesFirstResponder` on those controls. `windowShouldClose` commits an active edit before 0020's discard check runs.
- **⌘C/⌘X/⌘V/⌘A while editing** go to the text view as first responder through the standard Edit menu from 0001. No extra code, but nothing may intercept `copy:` ahead of the first responder (0020's image-copy action lives on the canvas or window controller, which comes later in the responder chain).
- New files under `Snapper/Annotate/` and `SnapperTests/` → run `xcodegen generate`. No `project.yml` content changes.
- No PRD update. All decisions above are implementation choices within PRD §8.2–8.4, except Q1 below.

## Acceptance criteria

- [ ] With the Text tool (T), clicking the image places a text box with a blinking caret at the click, and typing shows bold text in the current color at the current size (S/M/L = 16/24/36 image points).
- [ ] Enter/Return inserts a new line. The box grows to fit, and lines never wrap automatically.
- [ ] Esc commits the text (it does not deselect; the selection after commit follows decision 12). A second Esc (not editing) deselects per 0018.
- [ ] Clicking anywhere on the canvas outside the box commits, and that click does nothing else.
- [ ] Committing a box with no text, or only whitespace, removes it and leaves no undo step.
- [ ] Double-clicking existing text with the Select tool, or clicking it with the Text tool, re-enters editing with the caret at the click.
- [ ] Re-editing text to empty removes the shape, and undo brings it back with its original text.
- [ ] Rendered text has a thin outline: white around red, orange, green, blue, purple, and black text; black around yellow and white text (with 0003's preset values; see the table in the Test plan). There is no background.
- [ ] Changing color or S/M/L while editing changes the box being edited. Changing them with a text shape selected (Select tool) changes that shape, re-measuring its size for S/M/L. Each is one undo step.
- [ ] Each editing session (new or re-edit, including color/size changes made during it) is exactly one undo step. Redo re-applies it.
- [ ] While editing, ⌘C/⌘X/⌘V/⌘A act on the text, and typing V/R/A/T/⌫ edits text instead of switching tools or deleting the shape.
- [ ] Resizing the window while editing keeps the editor aligned with the image and at the right zoom. Committed text stays in the same place at every window size.
- [ ] Text in saved/copied output (0020) is rendered at full image pixel resolution with the same outline.
- [ ] The Text tool stays active after commit.

## Edge cases

- **Retina vs non-Retina captures:** `pixelsPerPoint` is 2 or 1, so the font pixel size is 32/48/72 or 16/24/36. A capture from a 1× display shown on a 2× display (and vice versa) must still use the document's `pixelsPerPoint`, never the window's backing scale.
- **Very small canvas scale** (large image in a small window): the editor font may get tiny. It must still be positioned exactly; no minimum font clamp (it has to match the rendering).
- **Click near the right/bottom edge:** the origin is clamped per decision 4. Text running past the image edge is clipped and doesn't crash or extend the image.
- **Click near the top edge:** `origin.y` is clamped to 0.
- **Overlapping text shapes:** the topmost (last drawn) text shape wins the hit test, as defined by 0015's z-order.
- **IME / marked text** (e.g. Japanese input): committing while marked text is pending must first call `unmarkText()` so the composed text is kept.
- **Paste of rich text or very long text:** pasted text is plain (rich text is off). Long single lines just extend the box.
- **Text that is only newlines:** counts as empty, so it is discarded or deleted.
- **Window closed, save (⌘S), or copy (⌘C with the canvas as first responder) while editing:** the edit is committed first (0020 calls `commit()`). Quitting the app commits active edits before the unsaved-annotations count (0020).
- **`NSColorPanel` opened while editing:** editing continues, and color changes apply live to the box.
- **Annotation window loses key status** (user switches apps): editing continues. When the user comes back, the caret is still in the box.
- **Undo while the session's text undo stack is empty:** ⌘Z is disabled (the text view validates it). It does not fall through to the document stack.
- **Several annotation windows:** each has its own `TextEditingController`. Editing in one doesn't affect the others.

## Test plan

- **Unit tests**
  - `SnapperTests/ContrastingOutlineTests.swift`
    - `relativeLuminance`: black = 0, white = 1 (±1e-9). Pure sRGB red (1,0,0) ≈ 0.2126, green (0,1,0) ≈ 0.7152, blue (0,0,1) ≈ 0.0722 (±1e-4).
    - Each of the 8 presets from 0003 maps to the expected outline. The expected values below assume 0003 defines the presets as Apple's light-mode system colors. If 0003 uses different components, recompute with the formula and update the table in the test (the 0.5 rule is the contract):

      | Preset (sRGB 0–255) | L | Outline |
      |---|---|---|
      | red (255, 59, 48) | 0.246 | white |
      | orange (255, 149, 0) | 0.428 | white |
      | yellow (255, 204, 0) | 0.645 | black |
      | green (52, 199, 89) | 0.423 | white |
      | blue (0, 122, 255) | 0.211 | white |
      | purple (175, 82, 222) | 0.204 | white |
      | black (0, 0, 0) | 0 | white |
      | white (255, 255, 255) | 1 | black |
    - Threshold edges: a gray just below L = 0.5 → white, a gray at or just above 0.5 → black. Compute the gray component by inverting the formula in the test (≈ 0.7354 gives L ≈ 0.5).
    - Alpha is preserved: red with alpha 0.4 → white with alpha 0.4.
    - Out-of-range components are clamped (e.g. 1.2 behaves like 1.0).
    - `strokeWidthPercent == 2 * outlineFractionOfFontSize * 100`.
  - `SnapperTests/TextMetricsTests.swift`
    - `fontPixelSize`: S/M/L at ppp 1 → 16/24/36, at ppp 2 → 32/48/72.
    - `editorFontSize`: 48 px at scale 0.5 → 24; at scale 1 → 48.
    - `newBoxOrigin`: a mid-image click gives `(x, y − lineHeight/2)`. A click at y = 2 with lineHeight 30 gives y clamped to 0. A click at the bottom edge gives y = imageHeight − lineHeight. A click at x = imageWidth − 1 keeps x. An image shorter than one line gives y = 0.
  - `SnapperTests/TextCommitDecisionTests.swift`
    - new + "" → `.discard`; new + "  \n\t" → `.discard`; new + "Hi" → `.add` with string "Hi" exactly as typed (including a trailing newline if present).
    - existing("Hi") + "" → `.delete(id)`; existing("Hi") + "\n" → `.delete(id)`.
    - existing("Hi") + "Hi" with the same color and width → `.noChange`.
    - existing("Hi") + "Hi" with a different color → `.update`; with a different width → `.update` (and the measured size from the draft is carried over).
    - existing("Hi") + "Hello" → `.update` with the same `id` and the new string.
  - `SnapperTests/TextEditingUndoTests.swift` (an `AnnotationDocument` + `FakeTextMeasurer`)
    - `.add`: shapes count +1, one undo step; undo → shape gone; redo → shape back; dirty is true.
    - `.discard` / `.noChange`: undo stack unchanged, dirty flag unchanged.
    - `.update`: undo restores the original string, color, width, and measuredSize in one step; redo re-applies.
    - `.delete`: shape removed in one step; undo restores it with the same `id` and text.
    - Stroke-width change on a selected text shape (0018 path + measurer): the new `measuredSize` comes from the fake measurer, it is one undo step, and undo restores the old size.
  - `SnapperTests/AppKitTextMeasurerTests.swift` (AppKit fonts are available in the test host; assert only stable properties): empty text height equals `lineHeight` > 0; two lines are taller than one line; "WWWW" is wider than "W"; size scales roughly linearly with font size (48 vs 24 px within 10% of 2×); results are whole numbers.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` both succeed.
- **Manual verification** (needs a real GUI session with Screen Recording permission; can't be automated):
  1. Press ⌘⇧6, capture an area with both dark and light content (e.g. part of a dark terminal next to a white web page). The annotation window opens.
  2. Press T, then click on the dark area. A caret appears centered on the click. Type `Hello`. Bold red text at size M appears where you clicked.
  3. Press Return and type `World`. A second line appears below; the box grows.
  4. Press Esc. Editing ends, the text stays, and the Text tool is still highlighted. Zoom in visually: red text has a thin **white** outline and no background.
  5. Click on the light area, type `Note`, then click elsewhere on the canvas. It commits, and that click does **not** start another box.
  6. Click on an empty spot, type nothing, press Esc. No shape remains. Press ⌘Z: the previous action (the `Note` box) is undone, not an empty box.
  7. Pick yellow, click, type `Light`, press Esc. The yellow text has a thin **black** outline. Repeat with white (black outline) and black (white outline).
  8. Press V, then double-click `Hello`. Editing resumes with the caret at the clicked letter. Change it to `Hello there`, press Esc. Press ⌘Z: the text reverts to `Hello\nWorld` in one step. Press ⇧⌘Z: it comes back.
  9. Double-click `Note`, delete all its text, press Esc. The shape is gone. Press ⌘Z: `Note` comes back.
  10. While editing a box, click the blue swatch and then L. The box being edited turns blue and gets bigger, and the caret stays in the box. Press Esc. Press ⌘Z once: the whole session (text + color + size) is undone.
  11. With the Select tool, select a committed text shape and click S. It gets smaller and stays at the same top-left. ⌘Z restores the size.
  12. While editing, type `v`, `r`, `a`, `t` and press ⌫. They edit the text; the tool and shapes don't change.
  13. While editing, select part of the text and press ⌘C, then paste into TextEdit. You get the selected text, not an image. Press ⌘V into the box: the text is pasted as plain text in the box's style.
  14. While editing, open the "+" color panel and pick a color. The box updates live, and editing continues after the panel closes.
  15. While editing, resize the annotation window larger and smaller. The editor stays exactly over its position in the image and scales with it. Press Esc and resize again: the committed text stays aligned with the image content at every size, with no jump between the editing and committed appearance (only the slightly different outline approximation while editing).
  16. Click near the bottom-right corner of the image and type a long line. The box is placed inside the image and the overflow is clipped at the image edge.
  17. On a Retina display, capture and add text at S/M/L, then save (⌘S, 0020). Open the PNG in Preview at 100%: the text is sharp, with the same outline, and its size relative to the image matches the canvas.
  18. Close the window while editing. The edit is committed first, then "Discard changes?" appears (0020).

## Open questions

- **Q1 — Single click on existing text with the Text tool.** PRD §8.3 says "Clicking with the Text tool places a text box" and "Double-clicking existing text edits it again", but doesn't say what a *single* Text-tool click on existing text does. This spec chooses: **it starts editing that text (caret at the click)**, so you never stack a new empty box on top of a label you meant to fix, and the double-click also works (its first click already enters editing). The alternative is that it places a new box on top. If the user picks the alternative, change decision 9 and acceptance criterion 6, and add a line to PRD §8.3.

## Out of scope

- The image-level ⌘C copy, ⌘S save panel, full-resolution flattening (`AnnotationExporter`), "Discard changes?", and the quit prompt: **0020**. This spec only exposes `TextEditingController.commit()` and requires that 0020 call it first.
- Shape selection, move, delete, swatches/color panel, S/M/L UI, remembered color/width, general undo/redo menu wiring, and Esc-deselect: **0018** (this spec only extends them for text).
- The `TextBox` shape case, its hit-testing, and moving it: **0015**. Undo stack and dirty flag: **0016**. Canvas, transform, window sizing, tool switching, and rectangle/arrow rendering: **0017**.
- Text resize handles, rotation, alignment options, font choice, background fills, and auto-wrapping: not in v1 (PRD §2, §8.2).
