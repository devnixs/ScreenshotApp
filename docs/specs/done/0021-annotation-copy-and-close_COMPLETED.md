# 0021 — Add a "Copy & Close" toolbar button and ⌘↩ to the annotation window

> Source: user request (2026-11-03); docs/PRD.md §8.2 (Copy & Close row and the toolbar-order bullet), §12 (Copy & Close criterion)
> Depends on: 0017-annotation-window-canvas, 0019-annotation-text-tool, 0020-annotation-export

## Context

The annotation window's toolbar ends with two buttons after a flexible space: **Save** and **Copy**
(`Snapper/Annotate/AnnotationWindowController.swift:8-9`, `:22`, `:88-99`).

- **Copy** (`copy(_:)`, `AnnotationWindowController.swift:237-249`) beeps if a drag is in progress,
  commits any in-progress text edit, flattens the image with `AnnotationExporter.flatten`, encodes it
  with `PNGEncoder.data`, writes it with `Clipboard.copy(pngData:image:)`, and calls
  `canvas.document.markClean()`. On failure it shows `ErrorAlerts.showError(title: "Couldn't copy to clipboard", …)`.
  The window stays open.
- Closing goes through `windowShouldClose` (`:193-209`): it commits text, and if the document is
  dirty it asks "Discard changes?" (`UnsavedAnnotationsPolicy.closeDecision`). `windowWillClose`
  (`:255-262`) releases the color panel and calls `onClose`, which removes the controller from
  `AnnotationWindowManager` (`Snapper/Annotate/AnnotationWindowManager.swift:25`).
- Keyboard commands reach the controller through the main menu: `MainMenuLayout`
  (`Snapper/App/MainMenuLayout.swift:28-41`) defines File → Close ⌘W and Save… ⌘S, and Edit → Copy ⌘C.
  `MainMenuBuilder` (`Snapper/App/MainMenuBuilder.swift`) turns these specs into `NSMenuItem`s with
  a `nil` target, so they go down the responder chain to the key window's delegate (the controller).
  `validateMenuItem` (`AnnotationWindowController.swift:251-253`) enables only `copy:` and `saveDocument:`.

Users want a one-step "done" action: copy the annotated image and close the window. The PRD
(updated in this change) now says:

- §8.2 table: **Copy & Close | ⌘↩ |** "Copies the flattened image to the clipboard, then closes the
  window without asking. If copying fails, the error is shown and the window stays open."
- §8.2 bullet: "Save, Copy, and Copy & Close sit at the right end of the toolbar, in that order.
  Copy & Close uses a checkmark icon. ⌘↩ also works while a text box is being edited: it commits the
  text first, then copies and closes. (Return alone still adds a new line.)"
- §12: "Copy & Close (toolbar button or ⌘↩) copies the full-resolution annotated image and closes
  the window without a prompt."

## User story

As a Snapper user who has finished annotating a capture, I click **Copy & Close** (or press ⌘↩) and
the annotated image is on my clipboard and the window is gone, in one step and with no prompt.

## Scope & changes

### Pure logic (unit-tested)

- **New `Snapper/Annotate/CopyAndCloseDecision.swift`**: a caseless `enum CopyAndCloseDecision` with
  two pure functions. No AppKit import.
  - `enum Start: Equatable { case refuse, copy }` and
    `static func start(isDragging: Bool, hasAttachedSheet: Bool) -> Start`. Returns `.refuse` when
    either flag is true (the caller beeps), otherwise `.copy`. A sheet means the Save panel or the
    "Discard changes?" alert is open, and the window must not close underneath it.
  - `enum Finish: Equatable { case close, stayOpen }` and
    `static func finish(copySucceeded: Bool) -> Finish`. `.close` on success, `.stayOpen` on failure.
- **`Snapper/App/MainMenuLayout.swift`**: add to `file`, after "Save…":
  `.init(title: "Copy & Close", keyEquivalent: "\r", modifiers: .command, action: "copyAndClose:")`.
  This is how ⌘↩ is bound. Menu key equivalents are matched before the focused `NSTextView` sees the
  key, so ⌘↩ also works while a text box is being edited, and plain Return keeps inserting a newline
  (`TextEditingController.textView(_:doCommandBy:)`, `Snapper/Annotate/TextEditingController.swift:114`,
  doesn't need to change). The existing `shortcutsHaveNoDuplicateBindings` test must still pass.

### Platform glue (manually verified)

- **`Snapper/Annotate/AnnotationWindowController.swift`**:
  - Add `static let copyAndCloseIdentifier = NSToolbarItem.Identifier("AnnotationCopyAndClose")`.
  - Make the toolbar order a `static let toolbarItems: [NSToolbarItem.Identifier]` (replacing the
    private instance property at `:22`) so tests can check it:
    `[toolsIdentifier, styleIdentifier, .flexibleSpace, saveIdentifier, copyIdentifier, copyAndCloseIdentifier]`.
    Copy & Close is at the **very right end**. Update the two `toolbar…ItemIdentifiers` methods to
    return it.
  - In `toolbar(_:itemForItemIdentifier:willBeInsertedIntoToolbar:)`, build the new item: label
    `"Copy & Close"`, tooltip `"Copy & Close (⌘↩)"`, image
    `NSImage(systemSymbolName: "checkmark", accessibilityDescription: "Copy & Close")`, target `self`,
    action `#selector(copyAndClose(_:))`. Restructure the Save/Copy branch however reads best (for
    example a small switch); keep Save and Copy unchanged.
  - Extract the body of `copy(_:)` after the drag guard into
    `private func copyFlattenedImage() -> Bool`: commit text, flatten, encode, write to the clipboard,
    `markClean()`, and return `true`. On failure, show the existing "Couldn't copy to clipboard" alert
    and return `false`. `copy(_:)` keeps its drag guard and then calls it, so its behavior doesn't change.
  - Add `@objc func copyAndClose(_ sender: Any?)`:
    1. `guard let window`. If `CopyAndCloseDecision.start(isDragging: canvas.isDragging, hasAttachedSheet: window.attachedSheet != nil) == .refuse`, `NSSound.beep()` and return.
    2. `let copied = copyFlattenedImage()` (this commits any in-progress text first).
    3. If `CopyAndCloseDecision.finish(copySucceeded: copied) == .close`, call `window.close()`.
       The document is clean after `markClean()`, so no prompt is possible. Call `close()` directly,
       not `performClose(_:)`, so the close button doesn't flash and `windowShouldClose` doesn't run
       again. `windowWillClose` still runs, so `onClose` and the color-panel cleanup happen as usual.
  - `validateMenuItem`: also return `true` for `#selector(copyAndClose(_:))`.
- No change to `AnnotationWindowManager`. Closing removes the controller through `onClose` as it
  does today.
- New source file → run `xcodegen generate`.
- **docs/PRD.md** §8.2 and §12 were updated in this change (see Context). Don't edit it again unless a
  decision changes.

## Acceptance criteria

- [ ] The annotation toolbar shows, from left to right at the right end: Save, Copy, **Copy & Close**.
      Copy & Close shows a checkmark icon, its tooltip is "Copy & Close (⌘↩)", and its label/accessibility
      description is "Copy & Close".
- [ ] Clicking Copy & Close puts the flattened, full-resolution annotated PNG on the clipboard (same
      content as Copy) and closes the window with no "Discard changes?" prompt, even with unsaved shapes.
- [ ] ⌘↩ does the same when an annotation window is key.
- [ ] While a text box is being edited, ⌘↩ commits the text (it appears in the copied image) and then
      copies and closes. Plain Return still inserts a newline.
- [ ] If copying fails, the "Couldn't copy to clipboard" alert is shown and the window stays open and
      still dirty.
- [ ] During a shape drag, or while the Save panel or "Discard changes?" sheet is attached, Copy &
      Close beeps and does nothing.
- [ ] Copy (⌘C) and Save (⌘S) behave exactly as before.
- [ ] After Copy & Close, the window is removed from `AnnotationWindowManager` (quitting afterwards
      doesn't count it as unsaved).
- [ ] File menu shows "Copy & Close ⌘↩"; it's disabled when no annotation window is key.

## Edge cases

- **Text being edited**: committing an empty text box removes it before the flatten (existing 0019
  behavior). Committing can make the document dirty; `markClean()` after a successful copy covers that.
- **Several annotation windows**: only the key window copies and closes. The others are unaffected.
- **Empty document** (no shapes, never dirty): still copies the plain capture and closes.
- **Color panel targeting this window**: `windowWillClose` already clears `NSColorPanel` ownership.
- **Repeated ⌘↩**: after the first close the window is gone, so the next ⌘↩ hits a disabled menu
  item (or another annotation window that becomes key; that's expected and mirrors ⌘W).
- **Mid-drag via ⌘↩**: the drag guard refuses; the drag keeps going.

## Test plan

- **Unit tests**
  - `SnapperTests/CopyAndCloseDecisionTests.swift` (new):
    - `start(isDragging: false, hasAttachedSheet: false) == .copy`
    - `start(isDragging: true, hasAttachedSheet: false) == .refuse`
    - `start(isDragging: false, hasAttachedSheet: true) == .refuse`
    - `start(isDragging: true, hasAttachedSheet: true) == .refuse`
    - `finish(copySucceeded: true) == .close`, `finish(copySucceeded: false) == .stayOpen`
  - `SnapperTests/MainMenuLayoutTests.swift`:
    - Update `fileCommandsRouteSaveAndClose` to expect three items: Close ⌘W, Save… ⌘S, and
      `MenuCommandSpec(title: "Copy & Close", keyEquivalent: "\r", modifiers: .command, action: "copyAndClose:")`.
    - `shortcutsHaveNoDuplicateBindings` passes unchanged.
  - Toolbar order (`@MainActor` test, in a new `SnapperTests/AnnotationToolbarLayoutTests.swift` or an
    existing annotation test file): `AnnotationWindowController.toolbarItems` equals
    `[toolsIdentifier, styleIdentifier, .flexibleSpace, saveIdentifier, copyIdentifier, copyAndCloseIdentifier]`,
    and its last element is `copyAndCloseIdentifier`.
  - `UnsavedAnnotationsPolicyTests` already covers that a clean document closes immediately. No change needed.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build`
  and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (needs a real GUI session with Screen Recording permission granted):
  1. Press ⌘⇧6 and capture a region. The toolbar's right end shows Save, Copy, then a checkmark
     button. Hover over it: the tooltip reads "Copy & Close (⌘↩)".
  2. Draw a rectangle and an arrow, then click the checkmark. The window closes with **no** prompt.
     Paste into Preview (File → New from Clipboard): the image has both shapes at full resolution
     (on Retina, pixel size is 2× the selection's point size).
  3. Open another annotation, draw a shape, press ⌘↩. Same result as step 2.
  4. Open another annotation, place a text box, type "Hello", press Return (a new line appears),
     type "World", then press ⌘↩ without leaving the box. The window closes, and the pasted image
     shows both lines of text.
  5. Open another annotation, press ⌘S to open the Save panel, then click the checkmark (or press
     ⌘↩). It beeps (or is disabled) and the window stays open. Cancel the panel.
  6. Open two annotation windows, draw in both, and press ⌘↩ in one. Only that window closes. Then
     press ⌘Q: Snapper asks "Quit and discard 1 unsaved annotation?".
  7. Check the File menu while an annotation window is key: "Copy & Close ⌘↩" is listed and enabled.
     With no annotation window open, it's disabled.
  8. Regression: ⌘C still copies and leaves the window open; ⌘S still saves.

## Out of scope

- A "Save & Close" action or any change to Save/Copy behavior.
- Making the shortcut configurable in Settings (§10 covers only the three global shortcuts).
- Copying a file reference (file URL) instead of image data: Copy & Close puts the same PNG + image
  data on the clipboard as Copy.

## Verification status

- **2026-11-03:** `xcodegen generate` succeeded; `xcodebuild -scheme Snapper -configuration Debug build` succeeded; `xcodebuild -scheme Snapper test` succeeded: **364 tests in 66 suites passed**, zero failures.
- Added six pure decision tests and one main-actor toolbar-order test; updated the File-menu expectation. The duplicate-shortcut test still passes.
- PRD §8.2 and §12 already specify this behavior; no PRD or architecture changes were needed.
- GUI behavior has not been manually verified. Run the following with Screen Recording permission granted:

- [ ] not yet run by user — Capture with ⌘⇧6; confirm Save, Copy, checkmark order and tooltip "Copy & Close (⌘↩)".
- [ ] not yet run by user — Draw a rectangle and arrow; click the checkmark; confirm no discard prompt and paste into Preview to verify both shapes at full pixel resolution (2× on Retina).
- [ ] not yet run by user — Draw a shape in a new annotation and press ⌘↩; confirm the same clipboard image and immediate close.
- [ ] not yet run by user — Edit a text box with "Hello", Return, "World"; press ⌘↩ while editing; confirm both lines appear in the pasted image.
- [ ] not yet run by user — Open the Save panel with ⌘S; invoke Copy & Close; confirm it beeps or is disabled and leaves the window open; cancel the panel.
- [ ] not yet run by user — Draw in two annotation windows; use ⌘↩ in one; confirm only that window closes and ⌘Q reports one unsaved annotation.
- [ ] not yet run by user — Confirm File → Copy & Close ⌘↩ is enabled for a key annotation window and disabled without one.
- [ ] not yet run by user — Regression: ⌘C copies while leaving the window open; ⌘S still saves.

Rebuilding with a different signature can reset Screen Recording permission.
