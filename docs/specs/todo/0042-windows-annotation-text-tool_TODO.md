# 0042 — Add the Windows Text tool: place, edit, and re-edit outlined text annotations

> Source: docs/PRD.md §8.3 (Text, in full), §8.2 (Text row, T key, Esc rule, Ctrl+C-while-editing rule, undo covers text edits and is coalesced, color/stroke apply to the selection), §8.4 (export resolution), §13.2 (Ctrl shortcuts, points → DIPs, bold system font → Segoe UI Bold, system color picker → `ChooseColor`), §13.7 (Annotate on Windows; one renderer for canvas and export)
> Depends on: 0027-windows-app-settings, 0038-windows-annotation-shape-model, 0039-windows-annotation-document-undo, 0040-windows-annotation-window-canvas, 0041-windows-annotation-editing

## Context

The `windows/` app doesn't exist yet. When this spec is picked up, the lower-numbered Windows specs will have delivered:

- **0027**: `Snapper.Core.Annotate.Model.RgbaColor` (8 presets, matching macOS `Snapper/Annotate/Model/RGBAColor.swift`) and `StrokeWidth` (S/M/L), plus `AppSettings` keys for the last-used color and width.
- **0038**: the pure shape model in `windows/src/Snapper.Core/Annotate/Model/`. It includes the `AnnotationShape` text kind (`TextBox` with `Origin` and `String`, plus `MeasuredSize` in **image pixels**, top-left origin, y down), text hit-testing (inside `Origin` + `MeasuredSize`, no slop), and no handles for text. 0038 also owns these, and this spec only references them (it never redefines them):
  - `ContrastingOutline` (port of `Snapper/Annotate/Model/ContrastingOutline.swift:3-17`) and its tests;
  - `TextMetrics` (port of `Snapper/Annotate/Model/TextMetrics.swift:3-12`) and its tests;
  - the measuring interface `ITextMeasurer` and the measurer-aware `AnnotationShape.WithStrokeWidth(StrokeWidth, double pixelsPerDip, ITextMeasurer)` (port of `Snapper/Annotate/Model/TextMeasuring.swift:3-17`);
  - the test fake `windows/tests/Snapper.Core.Tests/Fakes/FakeTextMeasurer.cs`;
  - `StrokeWidth` text sizes 16/24/36 (macOS `Snapper/Annotate/Model/StrokeWidth.swift:14-20`).
- **0039**: `AnnotationDocument` with image pixel size, `PixelsPerDip` (the capture monitor's scale factor, PRD §13.2: points → DIPs), shapes, selection, `Add`/`Replace`/`Delete`/`Select`, undo/redo, and dirty tracking (port of `Snapper/Annotate/Model/AnnotationDocument.swift`).
- **0040**: `AnnotationWindow`, `AnnotationCanvas`, the pure `CanvasTransform` (image pixels ↔ canvas DIPs, `Scale` = DIPs per image pixel, plus the image rect in DIPs), `AnnotationTool` (with `Text`, key T; 0040 may leave Text inert, as macOS 0017 did), the toolbar style state, and the WPF `ShapeRenderer`. That renderer draws shapes in image-pixel space into a `DrawingContext` and is shared with export (0043).
- **0041**: selection, move, resize, Delete/Backspace, Esc-deselect, recolor/restroke the selection, remembered color/width, document undo/redo wiring (Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z), and the Core canvas hit tester / editing routing.

Sibling specs may use slightly different member names than this spec. Use the sibling's actual names; the behavior described here is the contract. 0038 owns `ContrastingOutline`, `TextMetrics`, `ITextMeasurer`, the measurer-aware `WithStrokeWidth`, and `FakeTextMeasurer`. Reference them, and don't redefine them here. 0039 left `AnnotationDocument.Apply(TextCommitDecision)` and the port of `TextEditingUndoTests` to this spec.

What's missing is any way to create or edit text, and `ShapeRenderer` doesn't draw the text case yet. PRD §8.3:

> Clicking with the Text tool places a text box and starts editing. Enter adds a new line. **Esc** or clicking outside the box commits the text, and empty text boxes are removed. Double-clicking existing text edits it again. A single click on existing text with the Text tool also edits it, with the caret at the clicked character. Text uses a bold system font in the current color. Its size follows the stroke width: S = 16pt, M = 24pt, L = 36pt, in image points. Text has a thin contrasting outline (white around dark text, black around light text) and no background.

On Windows, "bold system font" is **Segoe UI Bold**, and "image points" are image DIPs: font size in image pixels = 16/24/36 × the document's `PixelsPerDip` (§13.2).

**macOS reference implementation** (spec `docs/specs/done/0019-annotation-text-tool_COMPLETED.md`, commit `9f5f8e1`). Port its behavior and tests; don't redesign it:

| macOS | Purpose | Windows port |
|---|---|---|
| `Snapper/Annotate/Model/TextCommit.swift:4-38` | `TextEditDraft`, `TextCommitDecision.decide`, `AnnotationDocument.apply` | Core, this spec |
| `Snapper/Annotate/Model/TextInteractionDecision.swift:5-17` | click routing: commit / beginNew / edit / none | Core, this spec |
| `Snapper/Annotate/Model/TextMeasuring.swift:8-17` | `withStrokeWidth(_:pixelsPerPoint:measurer:)` re-measures text | Core, 0038 (this spec wires it into 0041) |
| `Snapper/Annotate/Model/TextMetrics.swift`, `ContrastingOutline.swift` | font sizes, new-box origin, outline color | Core, 0038 |
| `Snapper/Annotate/TextStyle.swift:3-18` | one style for measuring, editing, and rendering | Windows `Annotate/TextStyle` |
| `Snapper/Annotate/AppKitTextMeasurer.swift:3-15` | measurement (trailing-newline fix, whole pixels) | Windows `Annotate/WpfTextMeasurer` |
| `Snapper/Annotate/AnnotationTextView.swift:3-30` | editor view configuration | Windows `Annotate/AnnotationTextEditor` (TextBox) |
| `Snapper/Annotate/TextEditingController.swift:3-126` | session lifecycle, live re-measure, apply color/width, layout, commit | Windows `Annotate/TextEditingController` |
| `Snapper/Annotate/ShapeRenderer.swift` (text case, `git show 9f5f8e1`) | two-pass outlined text | Windows `ShapeRenderer` text case |
| `Snapper/Annotate/AnnotationCanvasView.swift:6-10,56,63,87-93,166,202` | canvas integration | Windows `AnnotationCanvas` |
| `Snapper/Annotate/AnnotationWindowController.swift:58,107-113,139,152-163,190-194` | toolbar integration, close commits | Windows `AnnotationWindow` |
| `Snapper/Annotate/AnnotationWindowManager.swift:9-11` | `commitAllTextEdits()` before quit | Windows `AnnotationWindowManager` |
| `SnapperTests/TextCommitDecisionTests.swift`, `TextEditingUndoTests.swift`, `TextInteractionDecisionTests.swift`, `TextMetricsTests.swift`, `ContrastingOutlineTests.swift`, `AppKitTextMeasurerTests.swift`, `ShapeRendererTests.swift:72-93` | tests | xUnit ports below; the measurer and renderer are WPF-only, so they're checked manually |

macOS facts that **don't apply** on Windows: there's no Y flip (`TextClippingView.isFlipped`, `NSGraphicsContext(flipped:)`), no `NSColorPanel` live updates (Windows uses the modal `ChooseColor` dialog), no `cancelOperation`/`complete:` selector mapping, no `refusesFirstResponder` (WPF uses `Focusable="False"`), and no per-view `UndoManager` (the WPF `TextBox` has its own built-in undo stack).

## User story

As a Snapper user on Windows annotating a capture, I press T, click the image, type a label (several lines if I like), and press Esc or click elsewhere. The label appears in bold Segoe UI, in my current color and size, with a thin contrasting outline that keeps it readable on any background. I can click it with the Text tool, or double-click it with the Select tool, to edit it again with the caret where I clicked. Each editing session is undone in one Ctrl+Z.

## Design decisions (made in this spec; same as macOS 0019 unless marked *Windows*)

1. **The document isn't mutated while editing.** The in-progress text lives in a `TextEditDraft` owned by `TextEditingController`. On commit, exactly one document mutation (or none) is applied, which gives one undo step per session. Color and width changes during the session are part of that step.
2. **Commit outcomes** (pure `TextCommitDecision.Decide`):

   | Session started from | Committed text | Result | Undo steps |
   |---|---|---|---|
   | new box | empty | `Discard` | 0 |
   | new box | non-empty | `Add(shape)` | 1 |
   | existing box | empty | `Delete(id)` | 1 (undo restores the original) |
   | existing box | same text, color, and stroke width | `NoChange` | 0 |
   | existing box | anything else changed | `Update(shape)` with the original id | 1 |

   "Empty" means `string.IsNullOrWhiteSpace(text)`, which covers spaces, tabs, `\r`, and `\n`. Non-empty text is stored exactly as typed (after newline normalization, decision 3). Only `Add`/`Update`/`Delete` mark the document dirty.
3. ***Windows:* newline normalization.** The model always stores `\n` line breaks (it's shared, cross-platform logic, and it keeps parity with macOS). A WPF `TextBox` with `AcceptsReturn` inserts `\r\n`. Pure `EditorNewlines.ToModel(string)` converts `\r\n` and lone `\r` to `\n`. `EditorNewlines.ToEditor(string)` converts `\n` to `\r\n` (an existing `\r\n` isn't doubled). The draft string and every commit comparison use the model form.
4. **Origin anchor:** `TextBox.Origin` is the top-left corner of the layout box (top of the first line, left edge of the text; not the baseline), in image pixels. `MeasuredSize` = (width of the widest line including trailing whitespace) × (total line height), in image pixels, without the outline, rounded **up** to whole pixels. Empty text has width 0 and one line's height.
5. **New-box placement:** `TextMetrics.NewBoxOrigin(click, lineHeight, imageSize)` gives `(click.X, click.Y − lineHeight / 2)`, clamped to `0 ≤ X < imageWidth` and `0 ≤ Y ≤ imageHeight − lineHeight` (or `Y = 0` when the image is shorter than one line). Text can run past the right or bottom edge of the image, where it is clipped in both the canvas and the export.
6. **No automatic wrapping.** Lines break only at Enter. The box grows with its content.
7. **Font:** `Segoe UI`, `FontWeights.Bold`, normal style and stretch. Font size in image pixels = `TextMetrics.FontPixelSize(width, document.PixelsPerDip)` (16/24/36 × `PixelsPerDip`; at 150% that's 24/36/54). Measuring, editing, and rendering all use `TextFormattingMode.Ideal` and `pixelsPerDip = 1.0`, so layout depends only on the font pixel size, never on the monitor the window is on.
8. **Contrasting outline** (`ContrastingOutline` from 0038): WCAG relative luminance with a 0.5 threshold. `L ≥ 0.5` gives a black outline, otherwise white. The outline's alpha equals the text alpha.
9. **Outline drawing (*Windows* form of macOS's two-pass draw):** `FormattedText.BuildGeometry(origin)` gives the glyph geometry. Pass 1 strokes it with a `Pen` in the outline color, `Thickness = 2 × ContrastingOutline.OutlineFractionOfFontSize × fontPixelSize` (12.5% of the font size, centered on the glyph edge, so 6.25% lands outside: S = 1, M = 1.5, L = 2.25 image points), and `LineJoin = Round`. Pass 2 fills the same geometry with the text color, so glyphs keep their full bold weight. There's no background. The canvas and the export use **the same code** (0040's `ShapeRenderer`).
10. ***Windows:* editor = transparent `TextBox` over a live rendering.** The editor is a WPF `TextBox` whose text is invisible (`Foreground = Transparent`), with a visible caret and selection. While a session runs, the canvas skips the original shape and instead renders the **draft** as a text shape through `ShapeRenderer`. So the editing appearance is exactly the committed appearance (macOS only approximated the outline while editing). This works because the `TextBox` and `FormattedText` lay out with the same typeface, size, formatting mode, and line spacing. The editor lives in an **image-space layer**: a WPF `Canvas` whose `RenderTransform` is `Scale(t.Scale) · Translate(imageRect.X, imageRect.Y)`, where one layer unit is one image pixel. The `TextBox` uses `FontSize = fontPixelSize`, unscaled. Its effective on-screen size equals `TextMetrics.EditorFontSize(fontPixelSize, t.Scale)`, and its glyphs line up with the rendered draft at every zoom.
11. ***Windows:* text-box inset.** A WPF `TextBox` can have internal padding even with a minimal template. After the first layout, `TextEditingController` reads `textBox.GetRectFromCharacterIndex(0)` (in `TextBox` coordinates) and treats its `(X, Y)` as the inset. It places the `TextBox` at `Origin − inset`, so the caret's top-left sits exactly on `Origin`. The `TextBox` never scrolls. It's hosted in a `Canvas`, which measures it with infinite space, and it never gets a `Width` or `Height`, only `MinWidth = 2`.
12. **Clicks** (pure `TextInteractionDecision.Decide(isEditing, tool, clickCount, imagePoint?, document)`, port of the macOS version):
    - while editing, any canvas click outside the `TextBox` → `Commit`, and the click is consumed (no new box, no selection, no drawing);
    - Text tool, single or double click on a text shape (topmost wins) → `Edit(id)`, with the caret at the clicked character;
    - Text tool on no text shape, inside the image → `BeginNew(point)`; outside the image → `None`;
    - Select tool, `ClickCount == 2` on a text shape → `Edit(id)`; the first click of the pair selected it as usual (0041), and its zero-distance press must not add an undo step;
    - Rectangle and Arrow never edit text, even on double-click.
13. **Caret at the clicked character** (pure `CaretPlacement.InsertionIndex`, decision 12). After the `TextBox` is laid out (`UpdateLayout()`), `TextBox.GetCharacterIndexFromPoint(local, snapToText: true)` returns the nearest character `i`. If the click is at or past the horizontal middle of that character (`GetRectFromCharacterIndex(i)` vs `GetRectFromCharacterIndex(i, trailingEdge: true)`), the caret goes to `i + 1`, otherwise to `i`. When `i` is a `\r` or `\n`, the caret goes before the line break. It never lands between `\r` and `\n`. `local` is the click in `TextBox` coordinates: `imagePoint − Origin + inset`.
14. **After commit:** the active tool stays active. If the session started from the Select tool, the shape (if it still exists) stays selected. Otherwise nothing is selected. Keyboard focus returns to the canvas.
15. **Color, width, and tool changes while editing:** swatches, "+" (`ChooseColor`), and S/M/L apply to the **draft**. Changing the width re-measures. The `TextBox` and the rendered draft update immediately, and the change also persists to `AppSettings` as the last-used color or width (as in 0041). These toolbar controls are `Focusable="False"`, so the `TextBox` keeps keyboard focus. While `ChooseColor` is open (it's modal), the session continues, and focus returns to the `TextBox` when the dialog closes. Clicking a **tool** button, or pressing a tool key while not editing, commits first and then switches tools.
16. **Losing focus doesn't commit.** Deactivating the window (Alt+Tab, `ChooseColor`) keeps the session. When the window is reactivated, WPF restores focus to the `TextBox`. Nothing hooks `LostKeyboardFocus` to commit.
17. **Keys while editing** (pure `TextEditingKeyPolicy.Route`):

    | Key | Route | Effect |
    |---|---|---|
    | Esc | `Commit` | commits; it doesn't deselect and never closes the window |
    | Enter, Shift+Enter | `Editor` | new line |
    | Ctrl+Enter | `Window` | Copy & Close (0044 commits first); until 0044 lands, it's handled and does nothing (no newline) |
    | Ctrl+S, Ctrl+W, Alt+F4 | `Window` | save / close (each commits first; save is 0043) |
    | Tab, Shift+Tab | `Ignore` | handled, inserts nothing, and doesn't move focus (*Windows*: macOS inserts a tab character, see Edge cases) |
    | everything else, including V/R/A/T, Delete, Backspace, Ctrl+C/X/V/A, Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z | `Editor` | normal text editing; the `TextBox`'s own undo only |

    When not editing, `Route` returns `NotEditing`, and the existing 0040/0041 key handling runs unchanged. 0041's window `PreviewKeyDown` already skips its `CanvasKeyMap` entirely while `Keyboard.FocusedElement` is a `TextBoxBase` (0041 §7, "Text editing guard"). This spec fills that hook: in the same `PreviewKeyDown`, when the focused element is the editor `TextBox`, call `TextEditingKeyPolicy.Route`. `Commit` → `Commit()` and handled. `Ignore` → handled. `Window` → run the window command (or nothing yet) and handled. `Editor` → not handled, so the `TextBox` gets the key. 0040's tool-key handling (V/R/A/T) must sit behind the same guard: if it doesn't already skip `TextBoxBase` focus, gate it here. Any later window-level handler (0043's Ctrl+C/Ctrl+S, 0044's Ctrl+Enter) must also consult the policy, whether it's a `PreviewKeyDown` handler or an `InputBinding`/`CommandBinding`. This matters because the WPF `TextBox` doesn't mark Ctrl+Z as handled when its own undo stack is empty. A window binding would then undo the document.
18. **Undo inside a session:** the `TextBox` keeps `IsUndoEnabled = true` and adds an `InputBinding` that maps Ctrl+Shift+Z to `ApplicationCommands.Redo` (WPF binds only Ctrl+Y by default). A new `TextBox` is created per session, so its undo stack is discarded at commit. Document undo applies again after commit.

## Scope & changes

### Pure logic (`windows/src/Snapper.Core/Annotate/Model/`, unit-tested, no WPF)

- **`TextCommit.cs`**: port of `Snapper/Annotate/Model/TextCommit.swift:4-38`.
  - `public sealed record TextEditDraft(AnnotationShape? Original, string Text, PointD Origin, RgbaColor Color, StrokeWidth StrokeWidth, SizeD MeasuredSize)`. `Text` is in model form (`\n`).
  - `public abstract record TextCommitDecision` with nested `Discard`, `NoChange`, `Add(AnnotationShape Shape)`, `Update(AnnotationShape Shape)`, and `Delete(<shape id type> Id)`, plus `static TextCommitDecision Decide(TextEditDraft draft)` implementing decision 2. `Update` keeps the original id. `Add` gets a new id.
  - `public static class TextCommitDocumentExtensions { public static void Apply(this AnnotationDocument document, TextCommitDecision decision) }`, which uses 0039's `Add`/`Replace`/`Delete`. `Discard`/`NoChange` don't touch history or dirty state. (If 0039's document is a struct, use `ref this`; match 0039.)
- **`TextInteractionDecision.cs`**: port of `Snapper/Annotate/Model/TextInteractionDecision.swift:5-17`, an abstract record `Commit | BeginNew(PointD) | Edit(id) | None` with `static Decide(bool isEditing, AnnotationTool tool, int clickCount, PointD? imagePoint, AnnotationDocument document)` (decision 12). It hit-tests text shapes only, from the topmost down, with zero slop. If `AnnotationTool` lives in `Snapper.Core.Annotate` (0040), reference it there.
- **Text re-measure on restroke** (wiring only): 0041's `ApplyStyle(effects, withStrokeWidth)` takes a stroke delegate that is `(s, w) => s.WithStrokeWidth(w)` until this spec. This spec passes `(s, w) => s.WithStrokeWidth(w, document.PixelsPerDip, measurer)` instead, using 0038's measurer-aware overload and the window's `WpfTextMeasurer`. A selected text shape is then re-measured in the same single undo step. Color changes don't change size.
- **`EditorNewlines.cs`** (*Windows*): `ToModel(string)` and `ToEditor(string)`, as in decision 3.
- **`CaretPlacement.cs`** (*Windows*): `static int InsertionIndex(string editorText, int hitIndex, double clickX, double charLeft, double charRight)`, as in decision 13. An empty text or `hitIndex < 0` gives `editorText.Length` (0 when empty). The result is clamped to `[0, Length]` and never falls between `\r` and `\n`.
- **`TextEditingKeyPolicy.cs`** (*Windows*): `enum TextEditingKeyRoute { NotEditing, Commit, Editor, Window, Ignore }`, a Core key enum `TextEditingKey { Escape, Enter, Tab, S, W, F4, Other }` plus `bool ctrl, bool shift, bool alt`, and `static TextEditingKeyRoute Route(bool isEditing, TextEditingKey key, bool ctrl, bool shift, bool alt)` implementing the table in decision 17. The WPF glue maps `System.Windows.Input.Key` to `TextEditingKey`.
- **`TextEditorLayout.cs`** (*Windows*, in `Snapper.Core.Annotate` next to 0040's `CanvasTransform`): pure math for the image-space layer.
  - `LayerTransform(CanvasTransform t)` returns `(double Scale, double OffsetX, double OffsetY)`, which is `t.Scale` and the image rect's top-left in DIPs.
  - `TextBoxPosition(PointD origin, PointD inset)` returns `origin − inset`.
  - `EditorLocalPoint(PointD imagePoint, PointD origin, PointD inset)` returns `imagePoint − origin + inset`.

  There are no DIP↔pixel monitor conversions here. The layer is in image pixels, and the canvas transform (0040) already maps them to window DIPs. Monitor DPI only enters through the document's `PixelsPerDip` (font size).

### Platform glue (`windows/src/Snapper.Windows/Annotate/`, manually verified)

- **`TextStyle.cs`**: one source for the typeface (`new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)`) and for `CreateFormattedText(string modelText, double fontPixelSize, Brush brush)`. That method uses `CultureInfo.CurrentUICulture`, `FlowDirection.LeftToRight`, `pixelsPerDip: 1.0`, and `TextFormattingMode.Ideal`, with no `MaxTextWidth` (no wrapping). It also provides `ApplyTo(TextBox, double fontPixelSize, RgbaColor color)` for the editor's font family, weight, size, and caret brush. The measurer, the editor, and the renderer all use it. Port of `Snapper/Annotate/TextStyle.swift:3-18`.
- **`WpfTextMeasurer.cs`**: `public sealed class WpfTextMeasurer : ITextMeasurer` (port of `Snapper/Annotate/AppKitTextMeasurer.swift:3-15`).
  - `LineHeight(size)` = `Math.Ceiling(TextStyle.CreateFormattedText(" ", size, …).Height)`. Use this one definition everywhere.
  - `LayoutSize(text, size)`: when `text` is empty or ends with `\n`, measure `text + " "`, so the final empty line counts toward the height. Width = `Math.Ceiling(ft.WidthIncludingTrailingWhitespace)` (0 for empty text). Height = `Math.Max(LineHeight(size), Math.Ceiling(ft.Height))`. Main/dispatcher thread only.
- **`ShapeRenderer` text case** (file owned by 0040): it builds `FormattedText` at `TextMetrics.FontPixelSize(shape.StrokeWidth, ppp)` from the model string, calls `BuildGeometry(new Point(origin.X, origin.Y))`, and draws it in two passes per decision 9. Freeze brushes and pens. No background, no clipping of its own (the canvas and export clip to the image). The same code draws the canvas and the 96-DPI export (0043).
- **`AnnotationTextEditor.cs`**: `internal sealed class AnnotationTextEditor : TextBox` (port of `Snapper/Annotate/AnnotationTextView.swift`).
  - `AcceptsReturn = true`, `AcceptsTab = false`, `TextWrapping = NoWrap`, both scroll bar visibilities `Hidden`, `SpellCheck.IsEnabled = false`, `IsUndoEnabled = true`, `Background = Transparent`, `BorderThickness = 0`, `Padding = 0`, `Foreground = Transparent`, `TextOptions.TextFormattingMode = Ideal`, and `MinWidth = 2`.
  - It uses a minimal `ControlTemplate`: a `ScrollViewer` named `PART_ContentHost`, with no border chrome and no focus visual.
  - `CaretBrush` = the draft color. `SelectionBrush` = the system highlight with `SelectionOpacity = 0.4`.
  - Ctrl+Shift+Z → `ApplicationCommands.Redo`, and Tab/Shift+Tab are handled as a no-op (decision 17).
  - Pasted text is plain text (a `TextBox` is always plain).
- **`TextEditingController.cs`**: `internal sealed class TextEditingController`, one per `AnnotationWindow` (port of `Snapper/Annotate/TextEditingController.swift:3-121`).
  - `IsEditing`, `EditingShapeId`, `Style` (the draft's color and width, for toolbar display), and `DraftShape` (the draft as a text shape, for the canvas to render).
  - `BeginNew(PointD imagePoint)`: uses the current toolbar color and width, computes `Origin` with `TextMetrics.NewBoxOrigin` and the measurer's line height, and starts the session.
  - `BeginEditing(id, PointD caretImagePoint)`: loads the shape, sets `TextBox.Text = EditorNewlines.ToEditor(text)`, and starts the session.
  - Starting a session: creates the `AnnotationTextEditor`, adds it to the image-space layer, applies `TextStyle`, calls `UpdateLayout()`, measures the inset (decision 11), positions the editor, focuses it with `Keyboard.Focus`, and sets `CaretIndex` from `CaretPlacement` (decision 13). For a new box, the caret goes to 0. Then it redraws the canvas.
  - `TextChanged`: sets `draft.Text = EditorNewlines.ToModel(textBox.Text)` (this includes any in-progress IME composition text), re-measures, and redraws.
  - `ApplyColor(RgbaColor)`, `ApplyStrokeWidth(StrokeWidth)`: update the draft (width re-measures), re-apply `TextStyle`, re-measure the inset, and redraw.
  - `Layout(CanvasTransform)`: sets the layer's `RenderTransform` from `TextEditorLayout.LayerTransform` and its `Clip` to the image rect `(0, 0, imageWidth, imageHeight)` in layer units. The canvas calls it on every transform change (window resize or DPI change).
  - `Commit()`: if not editing, does nothing (idempotent). Otherwise it reads the final text, re-measures, ends the session, removes the `TextBox` from the layer, computes `TextCommitDecision.Decide`, calls `document.Apply`, sets the selection (decision 14), focuses the canvas, and redraws. It's public so that 0043 (save, copy, close, exit) and 0044 can call it first.
- **Image-space editor layer**: a WPF `Canvas` stacked above 0040's `AnnotationCanvas` in the same layout cell (or as its child if `AnnotationCanvas` is a `Panel`). It has `Background = null`, so clicks outside the `TextBox` fall through to the canvas.
- **`AnnotationCanvas` changes** (0040/0041 files; port of the macOS canvas lines in Context):
  - Its mouse-down handler converts the click to an image point (null outside the image) and calls `TextInteractionDecision.Decide` **before** any select or draw logic. `Commit` → `Commit()` and `e.Handled = true`. `BeginNew` → `BeginNew`. `Edit` → `BeginEditing`. `None` → existing behavior. If the handler is a `Preview…` tunnel handler, it ignores events whose `OriginalSource` is inside the editor `TextBox`.
  - Rendering skips the shape with `EditingShapeId` and draws `DraftShape` in its place (also for a new box). It doesn't draw selection chrome while editing.
  - Calls `TextEditingController.Layout` whenever the canvas transform changes.
- **`AnnotationWindow` / toolbar changes** (0040/0041 files):
  - Enable the Text tool button and the T key if 0040 left them inert.
  - Swatches, "+", and S/M/L get `Focusable="False"`. When `IsEditing`, color and width picks go to `TextEditingController.ApplyColor`/`ApplyStrokeWidth` and still persist to `AppSettings`. Otherwise they go to the selection, using the re-measuring `WithStrokeWidth` overload for text.
  - The toolbar's displayed style uses `TextEditingController.Style ?? selected shape style`.
  - Setting the current tool commits first.
  - Fill 0041's `TextBoxBase` guard in `PreviewKeyDown` with `TextEditingKeyPolicy` (decision 17).
  - `Closing` calls `Commit()` first (0043 adds the "Discard changes?" check after it).
  - After `ChooseColor` returns, focus goes back to the `TextBox` when editing.
- **`AnnotationWindowManager`** (0040): add `CommitAllTextEdits()` (port of `Snapper/Annotate/AnnotationWindowManager.swift:9-11`), which 0043 calls before counting unsaved windows on Exit or shutdown.
- No `NativeMethods.txt` additions and no new packages. New `.cs` files are picked up by the SDK-style projects automatically.
- No PRD update. Everything here is an implementation choice within §8.2–8.4 and §13. Decision 17's Tab behavior is reported back as a possible §13.7 note (see Edge cases).

## Acceptance criteria

- [ ] With the Text tool (T), clicking the image places a text box with a blinking caret vertically centered on the click. Typing shows Segoe UI Bold in the current color at S/M/L = 16/24/36 image points (× the capture monitor's scale factor in image pixels).
- [ ] Enter or Shift+Enter inserts a new line. The box grows, and lines never wrap automatically.
- [ ] Esc commits and doesn't deselect or close the window. A second Esc (not editing) deselects, as in 0041.
- [ ] Clicking anywhere on the canvas outside the box commits, and that click does nothing else.
- [ ] A box with no text, or only whitespace or newlines, is removed on commit and leaves no undo step.
- [ ] Clicking existing text with the Text tool, or double-clicking it with the Select tool, resumes editing with the caret at the clicked character.
- [ ] Editing existing text down to empty removes the shape, and Ctrl+Z brings it back with its original text.
- [ ] Text has a thin outline and no background: white around red, orange, green, blue, purple, and black; black around yellow and white.
- [ ] The text looks the same while editing and after commit: the same position, size, and outline, with no jump.
- [ ] Color, "+", and S/M/L while editing change the box being edited, and the caret stays in it. With a text shape selected (Select tool), they change that shape, and S/M/L re-measures it. Each case is one undo step.
- [ ] Each editing session (including color and size changes made during it) is exactly one document undo step, and Ctrl+Y / Ctrl+Shift+Z redo it.
- [ ] While editing: Ctrl+C/X/V/A act on the text, Ctrl+Z/Ctrl+Y/Ctrl+Shift+Z undo and redo typing only and never reach the document, and V/R/A/T, Delete, and Backspace edit text.
- [ ] Resizing the window, or moving it to a monitor with a different scale, while editing keeps the editor and its caret aligned with the rendered text. Committed text stays aligned with the image at every size.
- [ ] Closing the window while editing commits the text first.
- [ ] The Text tool stays active after commit.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed.

## Edge cases

- **Capture scale:** a capture from a 100%, 150%, or 200% monitor has `PixelsPerDip` 1, 1.5, or 2, so font pixel sizes are 16/24/36, 24/36/54, or 32/48/72. The window's current monitor DPI never changes the font pixel size, the measurements, or the export.
- **Mixed-DPI move:** dragging the window from a 100% to a 150% monitor while editing changes the DIP→device mapping, not the canvas transform (the transform is in DIPs). The editor stays aligned because it lives in the transformed image-space layer. Negative-origin monitors don't matter here: all of this spec's geometry is image-pixel-relative.
- **Very small zoom:** large image, small window. The editor scales down with the layer. There's no minimum font clamp.
- **Click near an edge:** the origin is clamped (decision 5). Text running past the right or bottom image edge is clipped there in the canvas (layer `Clip`) and in the export. Nothing crashes, and the image doesn't grow.
- **Overlapping text shapes:** the topmost one wins.
- **Trailing newline:** `"Hi\n"` measures two lines, and the caret on the empty second line stays inside the box.
- **Only newlines or whitespace:** counts as empty.
- **`\r\n` vs `\n`:** `"A\nB"` re-edited and committed unchanged is `NoChange` (decision 3), not `Update`.
- **IME composition:** `TextBox.Text` already contains the composition string, so committing mid-composition keeps the composed text.
- **Tab (*Windows*):** Tab and Shift+Tab are ignored while editing (macOS inserts a tab character). Tab characters pasted from elsewhere are kept. Both `FormattedText` and `TextBox` lay them out with WPF's default tab stops. The manual check confirms the caret stays aligned.
- **Undo with an empty `TextBox` stack:** Ctrl+Z does nothing and doesn't fall through to the document (decision 17).
- **`ChooseColor` while editing:** modal. Cancel leaves the draft unchanged, OK applies the color live, and either way the caret comes back to the box.
- **Alt+Tab away and back:** the session continues, and the caret is still in the box.
- **Several annotation windows:** each has its own `TextEditingController`.
- **Before 0043/0044 land:** Ctrl+S and image Ctrl+C don't exist yet. While editing, Ctrl+C still copies text. While not editing, Ctrl+C does nothing. Ctrl+Enter while editing is handled and does nothing. Closing commits, then closes without a prompt (0043 adds "Discard changes?").

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`). Use 0038's `Fakes/FakeTextMeasurer.cs` (`LayoutSize = (text.Length × size, size)`, `LineHeight = size`, like `SnapperTests/TextCommitDecisionTests.swift:5-8`).
  - `TextCommitDecisionTests.cs` (port of `SnapperTests/TextCommitDecisionTests.swift:17-36`):
    - a new draft with `""`, `"  \n\t"`, `"\n"`, or `"\r\n"` → `Discard`;
    - a new draft with `" Hi\n"` → `Add` with exactly `" Hi\n"` and origin (10, 20);
    - existing `"Hi"` + `""` or `"\n"` → `Delete(original.Id)`;
    - existing `"Hi"` + `"Hi"`, same color and width → `NoChange`;
    - existing + a different color, a different width (with a new measured size), or `"Hello"` → `Update` with the same id, the new text, and the draft's measured size.
  - `TextEditingUndoTests.cs` (port of `SnapperTests/TextEditingUndoTests.swift:6-50`):
    - applying `Discard` and `NoChange` leaves `CanUndo == false` and `IsDirty == false`;
    - `Add` gives 1 shape and dirty, undo → empty and `CanUndo == false`, redo → the same shape;
    - after `MarkClean`, `NoChange` leaves it clean;
    - `Delete` removes the shape, and undo restores it with the same id and leaves it clean;
    - an `Update` session (text + color + L + size) undoes to the original in one step and redoes to the updated shape;
    - `WithStrokeWidth(S, ppp 2, FakeTextMeasurer)` on the selected text → measured size (160, 32) for `"Hello"`, one undo step back to the previous size.
  - `TextInteractionDecisionTests.cs` (port of `SnapperTests/TextInteractionDecisionTests.swift:6-26`): two overlapping text shapes.
    - Text with 1 or 2 clicks, and Select with 2 clicks → `Edit(topmost id)`;
    - Rectangle, Arrow, or Select with 1 click → `None`; Arrow or Rectangle with 2 clicks → `None`;
    - Text on empty space → `BeginNew(point)`; Text with a null point → `None`;
    - editing with a null point → `Commit`; editing with Rectangle → `Commit`.
  - `EditorNewlinesTests.cs`:
    - `ToModel("A\r\nB")`, `ToModel("A\rB")`, and `ToModel("A\nB")` → `"A\nB"`;
    - `ToEditor("A\nB")` → `"A\r\nB"`; `ToEditor("A\r\nB")` → `"A\r\nB"` (not doubled);
    - round trip `ToModel(ToEditor(s)) == s` for `""`, `"\n"`, `"a\n\nb\n"`.
  - `CaretPlacementTests.cs`, using `"Hi\r\nYo"`:
    - hit 0 left of the midpoint → 0; hit 0 at or right of the midpoint → 1;
    - hit 1 (`i`) on the right half → 2;
    - hit 2 (`\r`) → 2; hit 3 (`\n`) → 2 (never 3);
    - hit 5 (`o`) on the right half → 6 (= Length);
    - empty text → 0; `hitIndex = -1` → Length;
    - an out-of-range hit is clamped.
  - `TextEditingKeyPolicyTests.cs`, one assertion per row of decision 17:
    - Esc → `Commit`; Enter and Shift+Enter → `Editor`; Ctrl+Enter → `Window`;
    - Ctrl+S, Ctrl+W, Alt+F4 → `Window`; Tab and Shift+Tab → `Ignore`;
    - Other (letters, Delete, Backspace, Ctrl+C, Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z) → `Editor`;
    - every key with `isEditing == false` → `NotEditing`.
  - `TextEditorLayoutTests.cs`:
    - `LayerTransform` for scale 1 with the image rect at (0, 0); for scale 0.5 with the image rect at (40, 12.5) (centered margins); and for scale 0.37 → `(Scale, OffsetX, OffsetY)` equal to the transform's;
    - `TextBoxPosition((100, 50), inset (2, 0))` → (98, 50);
    - `EditorLocalPoint((130, 60), origin (100, 50), inset (2, 0))` → (32, 10);
    - a round trip: view point → `CanvasTransform` image point → `EditorLocalPoint` for scale 0.5 lands on the expected local point;
    - the font pixel size combined with `EditorFontSize` for captures from 100%, 150%, and 200% monitors (ppp 1, 1.5, 2) at M with canvas scales 1 and 0.5 → 24/12, 36/18, 48/24.
  - `ContrastingOutline` and `TextMetrics` tests belong to 0038. Don't duplicate them here.
  - **Not unit-testable here:** `WpfTextMeasurer` and the `ShapeRenderer` text pixels need WPF, and `Snapper.Core.Tests` targets plain `net10.0`. The macOS equivalents (`SnapperTests/AppKitTextMeasurerTests.swift`, `ShapeRendererTests.swift:72-93`) are covered by manual steps 3, 4, 13, and 14 below.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` both succeed (both also run on macOS).
- **Manual verification** (Windows 11; build and run `Snapper.Windows`):
  1. Press Ctrl+Shift+6 and select an area with both dark and light content (for example a dark terminal next to a white web page). The annotation window opens.
  2. Press T and click on the dark area. A caret appears, vertically centered on the click. Type `Hello`. Bold red Segoe UI text at size M appears, starting exactly at the caret position.
  3. Press Enter and type `World`. A second line appears below it, and the box grows. Press Esc. Editing ends, the text doesn't move or change size, and the Text tool stays highlighted. Zoom in visually (for example with Windows Magnifier, Win+Plus): the red text has a thin **white** outline and no background.
  4. Click on the light area, type `Note`, then click elsewhere on the canvas. It commits, and that click doesn't start another box.
  5. Click an empty spot, type nothing, and press Esc. Nothing remains. Press Ctrl+Z: the `Note` box is undone, not an empty box.
  6. Pick yellow, click, type `Light`, and press Esc: the outline is **black**. Repeat with white (black outline) and black (white outline).
  7. Press V and double-click inside `Hello` between `l` and `o`. Editing resumes with the caret right there. Type ` there` at the end, then press Esc. Press Ctrl+Z: it goes back to `Hello`/`World` in one step. Press Ctrl+Y, then Ctrl+Z and Ctrl+Shift+Z: it redoes each time.
  8. Press T and single-click the middle of `World`. The caret lands at the clicked character, and no new box is created.
  9. Double-click `Note` (Select tool), delete all of its text, and press Esc. The shape is gone. Ctrl+Z brings it back.
  10. While editing a box, click the blue swatch, then L. The box turns blue and gets bigger, the caret stays in it, and the caret still sits at the glyph edges. Press Esc. One Ctrl+Z undoes the whole session (text, color, and size).
  11. With the Select tool, select a committed text shape and click S. It gets smaller and keeps its top-left. Ctrl+Z restores it.
  12. While editing, type `v`, `r`, `a`, `t`, then press Backspace and Delete. They edit the text, and neither the tool nor the shapes change. Press Ctrl+Z repeatedly until the box's typing is fully undone, then press it once more: nothing else in the document changes.
  13. While editing, select part of the text and press Ctrl+C, then paste into Notepad: you get the text, not an image. Paste rich text from a browser into the box: it arrives as plain text in the box's style, and the caret stays aligned with the glyphs.
  14. While editing, click "+" and pick a color in the Windows Color dialog: the box updates, and the caret is back in the box. Open the dialog again and cancel: nothing changes.
  15. While editing, resize the window larger and smaller: the caret and selection highlight stay exactly on the rendered glyphs. With two monitors at different scales (for example 100% and 150%), drag the window across while editing, then commit: the text stays aligned with the image content, with no jump.
  16. Click near the bottom-right corner of the image and type a long line. The box starts inside the image, and the overflow is clipped at the image edge.
  17. While editing, press Tab: nothing is inserted, and the caret stays in the box. Alt+Tab to another app and back: the caret is still in the box, and typing continues.
  18. While editing, close the window with Ctrl+W (also try the X button): the text is committed first (before 0043, the window then closes; after 0043, "Discard changes?" appears).
  19. Make a capture on a 150% monitor and add text at S/M/L. It's visibly the same size relative to UI text as a 16/24/36-DIP label. (After 0043: save it and open the PNG at 100%. The text is sharp, with the same outline, at full resolution.)

## Out of scope

- Image-level Ctrl+C, Ctrl+S, full-resolution export, "Discard changes?", the Exit confirmation, and the shutdown block reason: **0043**. This spec exposes `TextEditingController.Commit()` and `AnnotationWindowManager.CommitAllTextEdits()`, and 0043 must call them first.
- Copy & Close and Ctrl+Enter's behavior: **0044**. This spec only routes Ctrl+Enter to the window while editing.
- Selection, move, resize, delete, swatches, `ChooseColor`, the S/M/L UI, remembered color and width, document undo wiring, and Esc-deselect: **0041**. The canvas, transform, window sizing, tool switching, and rectangle/arrow rendering: **0040**. The `TextBox` shape kind and its hit-testing: **0038**. Undo history and dirty tracking: **0039**.
- Text resize handles, rotation, alignment, font choice, backgrounds, and auto-wrap: not in v1 (PRD §2, §8.2).
