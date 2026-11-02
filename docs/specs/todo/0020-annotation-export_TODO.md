# 0020 — Export annotations: full-resolution flatten, ⌘S save, ⌘C copy, unsaved-changes prompts

> Source: docs/PRD.md §8.1 (lines 116–117), §8.2 (Save/Copy rows, lines 128–129, and line 139), §8.4 (lines 149–150), §9 (lines 154–162), §12 (lines 200, 206)
> Depends on: 0001-xcodegen-app-shell, 0002-file-naming-save-folder, 0003-app-settings, 0008-screenshot-rectangle-capture, 0016-annotation-document-undo, 0017-annotation-window-canvas, 0018-annotation-editing, 0019-annotation-text-tool

## Context

No app code exists yet. This is the last annotation spec. When it starts, these pieces should already exist (canonical names from the backlog; check the real code before you begin and use whatever the earlier specs actually named things):

- **0001**: `AppDelegate`, `AppCoordinator`, and an `NSApp.mainMenu` with an Edit menu, so ⌘Z/⌘C/⌘Q route when our windows are key.
- **0002**: `FileNamer` (pure; `Screenshot <yyyy-MM-dd> at <HH.mm.ss>.png`, plus ` (2)`, ` (3)`… on collision), `CaptureKind.screenshot`, `SaveFolder` (resolve, create, writability check, `SaveFolderError`).
- **0003**: `AppSettings.saveFolderURL` (default `~/Documents/Screenshots`).
- **0008**: the PNG encoder that `ScreenshotCapturer` uses to write a `CGImage` as PNG with DPI metadata, `Clipboard.copy(…)`, and `ErrorAlerts`.
- **0016**: `AnnotationDocument` with `imagePixelSize`, `pixelsPerPoint`, `shapes`, `selection`, undo/redo, `isDirty`, and `markClean()`.
- **0017**: `AnnotationWindowController` (one per window), `AnnotationWindowManager` (tracks the open windows), `AnnotationCanvasView`, `CanvasTransform`, the toolbar, and `ShapeRenderer`. `ShapeRenderer` draws shapes in image pixel coordinates into a `CGContext` and is meant to be reused for export.
- **0018**: selection handles, which the canvas draws as editing chrome. They are not part of `ShapeRenderer` output.
- **0019**: text shapes, in-place text editing with an `NSTextView`, and a way to commit the in-progress text edit. Committing removes empty text boxes.

Nothing yet saves, copies, or flattens the annotated image, and closing a modified window or quitting Snapper does not warn about unsaved changes. The PRD requires:

- §8.2 **Save ⌘S**: "Save dialog pre-filled with the default folder and file name."
- §8.2 **Copy ⌘C**: "Copies the flattened image to the clipboard. The window stays open." Also: "⌘C copies the image only when no text box is being edited. While editing, it copies text as usual."
- §8.1: "Closing a window with changes that have not been saved or copied asks **"Discard changes?"**. Quitting Snapper while one or more annotation windows have unsaved changes asks once: **"Quit and discard N unsaved annotations?"**."
- §8.4: "The flattened output is rendered at the image's **full pixel resolution**, not at the window's display scale. Shapes are stored in image coordinates, so window zoom never changes the output."
- §9: the default folder and the `Screenshot <capture timestamp>.png` name, with a collision suffix.
- §12: "Saved or copied annotated images keep full resolution." and "Closing a modified annotation window asks "Discard changes?", and quitting with unsaved annotations asks once."

## User story

As a Snapper user who annotated a capture, I can press ⌘S to save it as a full-resolution PNG (the dialog opens on my screenshot folder with a ready-made name) or press ⌘C to put it on the clipboard. I'm warned before closing a window or quitting Snapper would throw away annotations I haven't saved or copied.

## Scope & changes

### Pure logic (unit-tested)

- **`Snapper/Annotate/AnnotationExporter.swift`**: a caseless `enum AnnotationExporter` with
  `static func flatten(image: CGImage, document: AnnotationDocument) throws(AnnotationExportError) -> CGImage`.
  - It creates a bitmap `CGContext` whose size is **exactly** `document.imagePixelSize`, which must equal `image.width × image.height`. If they differ, it throws `AnnotationExportError.sizeMismatch`. That is a programming error, so add an `assertionFailure` too.
  - **Pixel format:** 8 bits per component, RGBA, `CGImageAlphaInfo.premultipliedLast`, and `bytesPerRow: 0` (let CG choose).
    - **Why 8-bit:** captures are 8-bit, and the output is an 8-bit PNG.
    - **Why premultiplied:** `CGBitmapContext` does not support non-premultiplied alpha.
    - **Why RGBA:** it is the most widely supported `CGBitmapContext` RGB format. ImageIO and the pasteboard both accept it.
  - **Color space:** use the **source image's color space** when its `model == .rgb`, so a Display P3 capture keeps its colors and its embedded profile. Otherwise fall back to `CGColorSpace(name: CGColorSpace.sRGB)`.
  - **Draw order:**
    1. Draw the image at 1:1 into the full context rect, with no interpolation scaling (`interpolationQuality = .none` is fine, because the scale is 1).
    2. Draw every shape in `document.shapes`, in z-order, through `ShapeRenderer`, with a pure image-pixel transform. There is no `CanvasTransform` and no zoom factor.
    3. Draw **no** selection handles, hover states, or text-editing chrome. `ShapeRenderer` must not draw these. If 0017/0018 put handle drawing inside `ShapeRenderer`, add an option (for example `includeSelection: false`) and use it here.
  - **Orientation:** `CGContext` has a bottom-left origin. Image pixel coordinates (from 0015/0016) have a top-left origin. Set up the context exactly the way 0017's canvas does for image space, with zoom 1. If shapes are top-left (y down), flip the CTM for the shape pass (`translateBy(x: 0, y: height)` then `scaleBy(x: 1, y: -1)`) but draw the `CGImage` unflipped. The orientation pixel test below fails if this is wrong.
  - **Text:** 0019 may draw text through AppKit (`NSAttributedString.draw` or `NSString.draw`), which needs a current `NSGraphicsContext`. Wrap the shape pass in `NSGraphicsContext.saveGraphicsState()`, set `NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: <matches the CTM flip>)`, and restore it afterwards.
  - It returns `ctx.makeImage()` and throws `.contextCreationFailed` or `.imageCreationFailed` on failure.
  - **Isolation:** give it the same actor isolation as `ShapeRenderer`. If `ShapeRenderer` is `@MainActor`, so is the exporter, and its tests are `@MainActor`.
  - It is "pure-ish": there are no windows or views. Unit tests can run it headlessly.
- **`Snapper/Annotate/UnsavedAnnotationsPolicy.swift`** (pure): a caseless enum.
  - `enum CloseDecision: Equatable { case closeImmediately, confirmDiscard }`
  - `static func closeDecision(isDirtyAfterCommittingText: Bool) -> CloseDecision`
  - `static func quitDecision(dirtyWindowCount: Int) -> QuitDecision`, where `enum QuitDecision: Equatable { case terminateNow, confirm(message: String) }`.
  - `static func quitMessage(dirtyWindowCount: Int) -> String` returns `"Quit and discard 1 unsaved annotation?"` for 1 and `"Quit and discard \(n) unsaved annotations?"` for n ≥ 2. Each **window** counts as one annotation, whatever its number of shapes. It is never called with 0. If it is, it falls back to the plural form.
  - String constants: `discardTitle = "Discard changes?"`, `discardButton = "Discard"`, `quitButton = "Quit"`, `cancelButton = "Cancel"`.
- **`Snapper/Annotate/AnnotationSaveDefaults.swift`** (pure): `static func suggestedFileName(captureDate: Date, directory: URL?, fileExists: (URL) -> Bool) -> String`. It delegates to `FileNamer` for `.screenshot` at **the capture date**, not "now", with collision suffixes checked against `directory`. When `directory` is `nil`, it returns the base name with no suffix. Reuse `FileNamer`'s time zone and calendar parameters as 0002 defines them. **Do not** reimplement the date format or the suffix rule.

### Platform glue (manually verified)

- **Capture date on the window**: `AnnotationWindowController` stores `captureDate: Date`, the moment the ⌘⇧6 capture completed, and keeps the source `CGImage` it already holds. If 0017's initializer doesn't take a capture date, add the parameter and pass it from the ⌘⇧6 path in `AppCoordinator` (or wherever 0017 creates annotation windows).
- **Toolbar**: add **Save** (SF Symbol `square.and.arrow.down`, tooltip "Save (⌘S)") and **Copy** (`doc.on.doc`, tooltip "Copy (⌘C)") buttons to the annotation toolbar, at the trailing end, unless 0017 already put placeholders there. They target the `AnnotationWindowController` directly.
- **Main menu (extends 0001)**: in the code that builds `NSApp.mainMenu`, add a **File** menu between the app menu and Edit, unless it already exists:
  - "Save…" with ⌘S, action `#selector(NSDocument.saveDocument(_:))` (the standard `saveDocument:` selector), target `nil`;
  - "Close" with ⌘W, action `#selector(NSWindow.performClose(_:))`, target `nil`, if 0001 didn't already provide ⌘W.

  The Edit menu's "Copy" item from 0001 must be nil-targeted with `#selector(NSText.copy(_:))` (`copy:`). Verify this, and fix it if needed. Because the app is `LSUIElement`, the menu bar is never shown, but key equivalents still route through `mainMenu` while our window is key.
- **⌘S / Save button**: `AnnotationWindowController.saveDocument(_:)` (`@objc`; also the Save button's action):
  1. Commit any in-progress text edit (0019's commit entry point). This also covers ⌘S pressed while typing: `NSTextView` doesn't handle `saveDocument:`, so the action reaches the controller.
  2. Resolve the default folder from `AppSettings.saveFolderURL` through `SaveFolder`, creating it if it's missing (PRD §9). **If it can't be resolved or created, don't show an alert and don't block.** Set `directoryURL` to `nil` so the panel opens on its default location. The user is about to choose a folder anyway, and the §9 "Couldn't save to <folder>" alert is for automatic saves.
  3. Build an `NSSavePanel` with:
     - `allowedContentTypes = [.png]`
     - `canCreateDirectories = true`
     - `directoryURL` from step 2
     - `nameFieldStringValue = AnnotationSaveDefaults.suggestedFileName(captureDate:directory:fileExists:)`, using `FileManager.default.fileExists(atPath:)`

     Present it as a **sheet** on the annotation window (`beginSheetModal(for:)`). Every Save starts from the Settings folder: don't persist the panel's chosen folder back to `AppSettings`. `NSSavePanel` itself asks for confirmation before overwriting an existing file.
  4. On `.OK`, call `AnnotationExporter.flatten(image:document:)`, then write PNG data to `panel.url` with **0008's PNG encoder**. Pass DPI = `72 × document.pixelsPerPoint` so a 2× image opens at its point size in Preview. Write atomically. Do not write a second PNG encoder.
  5. On success, call `document.markClean()`. The window **stays open**, as with Copy. Save does **not** play the shutter sound, does not copy to the clipboard, and does not reveal the file in Finder. The *Screenshot destination* and *Play shutter sound* settings apply only to ⌘⇧4 (PRD §6).
  6. On a flatten or write error, show 0008's generic error alert (`ErrorAlerts`) with the title "Couldn't save to <folder display name>" and the error's `localizedDescription`. There is **no** Choose Folder… button here, because the user already picked the folder in the panel. The document stays dirty.
- **⌘C / Copy button**: `AnnotationWindowController.copy(_:)` (`@objc`; also the Copy button's action).
  - **Keyboard routing:** ⌘C goes through Edit ▸ Copy (`copy:`, nil target) and up the responder chain. While a text box is being edited, its `NSTextView` is the first responder and handles `copy:` itself, so text is copied as usual (PRD line 139). If nothing is selected in the text view, the item is disabled and nothing happens. The image is never copied while editing. When no text is being edited, the first responder is the canvas (or the window), which does **not** implement `copy:`, so the action falls through to `AnnotationWindowController`. The controller must be the window's delegate, or otherwise sit in the window's responder chain, so it receives the action.
  - **Rule:** `AnnotationCanvasView` and other views in the window must **not** implement `copy:`.
  - **The Copy toolbar button** first commits any in-progress text edit. Clicking outside the text box commits it (PRD §8.3), and the toolbar is outside it. The button then copies the image.
  - **Copy steps:** `flatten`, then `Clipboard.copy(image)` (0008, which writes the same PNG representation as screenshots), then `document.markClean()`. The window **stays open**. No sound plays. On a flatten error, show the generic `ErrorAlerts` alert.
  - **Menu validation:** implement `NSMenuItemValidation.validateMenuItem(_:)` on the controller and return `true` for `copy:` and `saveDocument:`. When no annotation window is key, nothing responds, so the items are disabled.
- **Close**: `AnnotationWindowController.windowShouldClose(_:)` (`NSWindowDelegate`). It covers the close button, ⌘W, and File ▸ Close.
  1. Commit any in-progress text edit. Committing an empty box removes it (0019).
  2. If `UnsavedAnnotationsPolicy.closeDecision(isDirtyAfterCommittingText: document.isDirty) == .closeImmediately`, return `true`.
  3. Otherwise, return `false` and show an `NSAlert` **sheet** on the window: `messageText = "Discard changes?"`, `alertStyle = .warning`, and buttons "Discard" (first, default, `hasDestructiveAction = true`) and "Cancel" (second, Esc). There's no "Save…" button; the PRD only asks "Discard changes?".
  4. On Discard, close the window for real. Set a `isClosingConfirmed` flag and call `window.close()`, or bypass the delegate check, so the prompt doesn't show again. `AnnotationWindowManager` drops the controller as it already does on close. On Cancel, do nothing.

  Don't show a second sheet if one is already attached (`window.attachedSheet != nil`). Return `false` and beep.
- **Quit**: `AppDelegate.applicationShouldTerminate(_:)`. It covers ⌘Q, "Quit Snapper" in the status-item menu, and logout.
  1. Ask `AnnotationWindowManager` (reached through `AppCoordinator`) to commit in-progress text edits in **every** open annotation window. Add something like `commitAllTextEdits()` and `dirtyWindowCount` to the manager.
  2. `UnsavedAnnotationsPolicy.quitDecision(dirtyWindowCount:)`: `.terminateNow` returns `.terminateNow`.
  3. On `.confirm(message)`, call `NSApp.activate()` (agent app), then run an **app-modal** `NSAlert` (`runModal()`, a single alert even with several windows): `messageText = message`, `alertStyle = .warning`, and buttons "Quit" (first, default, destructive) and "Cancel" (Esc). Quit returns `.terminateNow`. The windows aren't asked again one by one, because `terminate` doesn't call `windowShouldClose`. Cancel returns `.terminateCancel`, and all windows and documents stay untouched.

  If earlier specs (for example 0011, for an in-progress recording) already put logic in `applicationShouldTerminate`, keep it and run this check in sequence. Any "cancel" result wins.
- No `project.yml` changes beyond the new source and test files. Run `xcodegen generate` after adding them.
- No PRD change is made in this spec. See "Decisions" below for the behaviors this spec pins down where the PRD is silent.

### Decisions (PRD silent; taken here, consistent with the PRD's intent)

1. Save and Copy both leave the window open and both mark the document clean. "Saved or copied" in §8.1 counts either one.
2. Save ignores the *Screenshot destination* and *Play shutter sound* settings and doesn't reveal the file in Finder.
3. If the default folder can't be created, the Save panel still opens, on its default location, with no alert.
4. The panel's chosen folder isn't remembered. Each Save opens on `AppSettings.saveFolderURL`.
5. The "Discard changes?" alert has only Discard and Cancel. The quit alert has Quit and Cancel, and its count is the number of dirty **windows**, with a singular form for 1.
6. The Copy and Save toolbar buttons commit an in-progress text edit first. ⌘C while editing copies only text, and ⌘S while editing commits the text and then saves.
7. A text box that's placed but left empty doesn't make the document dirty once committed.

## Acceptance criteria

- [ ] ⌘S and the Save button open a save sheet on the annotation window, pre-filled with the Settings save folder (created if missing) and `Screenshot <capture date> at <capture time>.png`, with ` (2)`, ` (3)`… when that name already exists in the folder.
- [ ] The saved PNG's pixel size equals the captured image's pixel size (2× on Retina), whatever the window's zoom (PRD §8.4, §12).
- [ ] The saved PNG and the copied image contain every shape (rectangles, arrows, text) and no selection handles or editing chrome.
- [ ] ⌘C and the Copy button put the flattened, full-resolution image on the clipboard, and the window stays open.
- [ ] While a text box is being edited, ⌘C copies the selected text, not the image.
- [ ] Closing a window with changes not saved or copied asks "Discard changes?" (Discard / Cancel). Cancel keeps the window and its edits, and Discard closes it.
- [ ] Closing a window right after a Save or Copy (with no edits since) doesn't prompt. Editing after a Copy makes it prompt again.
- [ ] Quitting with N ≥ 1 dirty annotation windows shows exactly one alert, "Quit and discard N unsaved annotations?" (singular for 1). Quit terminates and Cancel keeps everything open. With 0 dirty windows, Snapper quits without asking.
- [ ] Quit Snapper from the status-item menu triggers the same check as ⌘Q.
- [ ] Save and write errors show a short alert, and the document stays dirty.

## Edge cases

- **1× vs 2× captures**: the output is always `imagePixelSize`. DPI metadata is 72 for 1× and 144 for 2×.
- **Window zoomed far below 100%**: the export is unaffected, because nothing in the export path reads `CanvasTransform` or `window.backingScaleFactor`.
- **Display P3 / non-sRGB source**: the color space is preserved. ImageIO embeds the profile. A grayscale or other non-RGB source falls back to sRGB.
- **No shapes**: Save and Copy still work and output the unmodified image. The document stays or becomes clean.
- **A selected shape**: it is exported without handles.
- **Shapes partly outside the image** (dragged past the edge): they are clipped by the context bounds. No error.
- **An in-progress text edit** at Save, Copy button, close, or quit: it is committed first. Empty text is removed and doesn't count as a change.
- **⌘C in a text box with an empty text selection**: the menu item is disabled, so nothing happens. It never falls back to copying the image.
- **The default save folder is missing**: it is created. If it is uncreatable or unwritable, the panel opens on its default location. If the user picks an unwritable folder, the write fails, an alert appears, and the document stays dirty.
- **Name collision**: the suggested name gets ` (2)`, ` (3)`…. If the user types an existing name, `NSSavePanel`'s own replace confirmation handles it.
- **Several windows from captures in the same second**: each suggests a unique name at panel time, based on the files that exist then.
- **Quit while a save panel or "Discard changes?" sheet is open**: the quit alert still appears, app-modal. On Quit, the app terminates.
- **Quit with several dirty windows on different Spaces or displays**: one alert, which is not attached to any window.
- **Close while a sheet is already attached**: no second sheet.
- **Logout or restart**: `applicationShouldTerminate` shows the same alert. Cancel aborts the logout, which is standard macOS behavior.

## Test plan

- **Unit tests**, `SnapperTests/AnnotationExporterTests.swift` (`@MainActor` if `ShapeRenderer` is). Build synthetic source images in code, for example a solid opaque blue (sRGB 0,0,255) `CGImage`. Read the output pixels by drawing the result into a known sRGB RGBA8 buffer, where row 0 is the **top** row.
  - `testOutputSizeEqualsSourcePixelSize_1x`: a 300×200 image with `pixelsPerPoint = 1` and one rectangle gives output 300×200.
  - `testOutputSizeEqualsSourcePixelSize_2x`: a 600×400 image with `pixelsPerPoint = 2` gives output 600×400 (not 300×200).
  - `testOutputIndependentOfCanvasZoom`: create the same rectangle twice by converting the same image-space rect through 0017's `CanvasTransform` at zoom 0.25 and at 1.0 (view → image), add each to its own document, and flatten. The outputs are byte-identical.
  - `testRectangleStrokePixelsAndUntouchedInterior`: a 200×100 blue image at 1×, with a red rectangle at image rect (20, 10, 100, 50), top-left origin, stroke M (4 image points). The pixel at the middle of the top edge (70, 10) is red (±3 per channel). The interior (70, 35) and a far pixel (180, 90) are exactly blue.
  - `testOrientationTopLeft`: a red rectangle near the **top** (y = 5…25). Pixels in the top rows are red, and the mirrored bottom rows are blue. This catches a missing or extra flip.
  - `testArrowPixels`: a horizontal red arrow from (20, 80) to (180, 80). The midpoint (100, 80) is red, and (100, 20) is blue.
  - `testStrokeScalesWithPixelsPerPoint`: the same rect stroke M on a 2× document. The stroke is about 8 px thick, so a pixel 3 px inside the edge is red, whereas on 1× it is blue.
  - `testTextRendersSomething`: a text shape ("Hi", red, M) on a blue image. At least one pixel inside the text's image-space bounds is not blue. This is a robust check that AppKit text drawing works in the bitmap context, with no exact glyph matching.
  - `testNoSelectionChromeExported`: a document whose rectangle is **selected** flattens byte-identical to the same document with no selection.
  - `testSizeMismatchThrows`: a document whose `imagePixelSize` ≠ the image size throws `.sizeMismatch`. Run it with assertions disabled, or skip the `assertionFailure` in tests through an injected flag. Pick one and document it.
- **Unit tests**, `SnapperTests/UnsavedAnnotationsPolicyTests.swift`:
  - `quitMessage(1) == "Quit and discard 1 unsaved annotation?"`; `quitMessage(2) == "Quit and discard 2 unsaved annotations?"`; `quitMessage(10)` is plural.
  - `quitDecision(0) == .terminateNow`; `quitDecision(1)` and `quitDecision(3)` are `.confirm` with the matching message.
  - `closeDecision(true) == .confirmDiscard`; `closeDecision(false) == .closeImmediately`.
  - **Decision table with the real model** (0016/0019 types, no AppKit), covering clean or dirty × text being edited or not:
    - clean, no text edit → `.closeImmediately`;
    - clean, an in-progress text edit with non-empty text, committed → `isDirty == true` → `.confirmDiscard`;
    - clean, an in-progress text edit with **empty** text, committed → `isDirty == false` → `.closeImmediately`;
    - dirty (shape added), no text edit → `.confirmDiscard`;
    - dirty, then `markClean()` (simulating Save or Copy) → `.closeImmediately`;
    - after `markClean()`, another edit → `.confirmDiscard`.

    If the 0016/0019 dirty tracking can't satisfy the empty-text row, fix the model in this spec.
- **Unit tests**, `SnapperTests/AnnotationSaveDefaultsTests.swift`, with a fixed time zone and a fixed date 2026-11-02 19:18:04:
  - no collision → `"Screenshot 2026-11-02 at 19.18.04.png"`;
  - `fileExists` reports the base name as taken → `"… (2).png"`; base and `(2)` taken → `"… (3).png"`;
  - `directory == nil` → the base name, and `fileExists` is never called;
  - the capture date, not "now", is used. Pass a date in the past and assert it appears in the name.
- **Build check:** run `xcodegen generate` (new files), then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test`. Both must succeed.
- **Manual verification** (needs a real GUI session with Screen Recording permission granted; can't be automated):
  1. On a Retina display, press ⌘⇧6, select a region of roughly 400×300 pt, and draw a rectangle, an arrow, and a text box. Note the region's size.
  2. Press ⌘S. A sheet appears on the annotation window. The folder is the Settings save folder (`~/Documents/Screenshots` by default) and the name is `Screenshot <date> at <time>.png`, matching the time of the capture, not of the Save. Save it.
  3. In Terminal, run `sips -g pixelWidth -g pixelHeight ~/Documents/Screenshots/"Screenshot … .png"`. The size is 2× the selection's point size. Open the file in Preview: all three shapes are present and there are no selection handles.
  4. Resize the annotation window very small so the canvas is zoomed out, then press ⌘S again. The suggested name now ends in ` (2)`. Save it, and `sips` shows the **same** pixel size as in step 3.
  5. Rename `~/Documents/Screenshots` temporarily, then press ⌘S. The folder is recreated and the panel opens in it. Restore it afterwards.
  6. Press ⌘C (no text being edited). In Preview, choose File ▸ New from Clipboard. The image has full resolution (Tools ▸ Adjust Size shows the 2× pixel dimensions) and contains the shapes. The annotation window is still open.
  7. Double-click the text box, select part of its text, and press ⌘C. Paste into TextEdit: only the selected text is pasted. In Preview, File ▸ New from Clipboard shows no new image.
  8. Draw a new shape, then click the window's close button. "Discard changes?" appears as a sheet. Click Cancel: the window and shape remain. Press ⌘W, then click Discard: the window closes.
  9. Open a new annotation, draw, press ⌘C, then close the window. It closes without a prompt.
  10. Open a new annotation, draw, press ⌘C, draw another shape, then close. "Discard changes?" appears.
  11. Open a new annotation, place a text box, type nothing, and close the window. It closes with no prompt.
  12. Open two annotation windows and draw in both. Press ⌘Q while one is key. Exactly one alert appears: "Quit and discard 2 unsaved annotations?". Click Cancel: both windows remain. Then choose **Quit Snapper** from the status-item menu. The same alert appears. Click Quit: Snapper quits.
  13. With one dirty window, quit: the alert reads "Quit and discard 1 unsaved annotation?". With only clean annotation windows open, Snapper quits immediately.
  14. Press ⌘S, choose a read-only folder (for example `/System`), and Save. An error alert appears and the window still prompts on close.

## Out of scope

- The capture itself, PNG encoding, and the clipboard and alert helpers: 0008, which this spec reuses.
- The shape model, document, and undo (0015, 0016); the window, canvas, toolbar, and `ShapeRenderer` drawing (0017); selection, colors, and widths (0018); text placement, editing, and rendering (0019). This spec only adds an export entry point to them, plus `includeSelection: false` if needed.
- The *Screenshot destination* setting, the shutter sound, and the "Couldn't save to <folder>" **Choose Folder…** alert for ⌘⇧4 (0008).
- Quitting while recording (0011).
- JPEG or other formats (PRD §2 non-goal), and "Save As" or other format options.
