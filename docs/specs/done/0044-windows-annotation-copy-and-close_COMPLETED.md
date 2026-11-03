# 0044 — Add a "Copy & Close" toolbar button and Ctrl+Enter to the Windows annotation window

> Source: docs/PRD.md §8.2 (Copy & Close row and the toolbar-order bullet), §12 (Copy & Close criterion), §13.2 (⌘↩ → Ctrl+Enter), §13.7 (Segoe Fluent Icons, checkmark glyph). Windows port of the macOS spec `docs/specs/todo/0021-annotation-copy-and-close_TODO.md`.
> Depends on: 0043-windows-annotation-export (and through it 0040, 0041, 0042, 0031)

## Context

When this spec starts, the Windows annotation window (0040–0043) has a toolbar whose right end holds
**Save** and **Copy**:

- **Copy** (0043, Ctrl+C when no text box is being edited) beeps if a canvas drag is in progress,
  commits any in-progress text edit (0042), flattens the image at full resolution with
  `Annotate/AnnotationExporter` (0043), writes PNG + DIB to the clipboard with `Support/Clipboard`
  (0031), and marks the document clean. On failure it shows the `Support/ErrorAlerts` (0031) alert
  **"Couldn't copy to clipboard"** and the window stays open and dirty.
- **Closing** (Ctrl+W, Alt+F4, title-bar X) goes through the WPF `Window.Closing` handler from 0043:
  it commits text, refuses while one of the window's own modal dialogs is open, and asks
  **"Discard changes?"** when `UnsavedAnnotationsPolicy` (Core, 0043) says the document is dirty.
  `Closed` removes the window from `Annotate/AnnotationWindowManager` (0040), which also updates the
  "Unsaved annotations" shutdown block reason and the Exit-confirmation count (0043).

Missing: a one-step "done" action. The PRD says:

- §8.2 table: **Copy & Close | ⌘↩ |** "Copies the flattened image to the clipboard, then closes the
  window without asking. If copying fails, the error is shown and the window stays open."
- §8.2 bullet: "Save, Copy, and Copy & Close sit at the right end of the toolbar, in that order.
  Copy & Close uses a checkmark icon. ⌘↩ also works while a text box is being edited: it commits the
  text first, then copies and closes. (Return alone still adds a new line.)"
- §12: "Copy & Close (toolbar button or ⌘↩) copies the full-resolution annotated image and closes
  the window without a prompt."
- §13.2: ⌘↩ (Copy & Close) → **Ctrl+Enter**. §13.7: toolbar buttons use Segoe Fluent Icons glyphs;
  Copy & Close uses the **checkmark** glyph.

### macOS reference implementation (port this, don't redesign it)

The macOS spec 0021 is implemented before this batch. Port its decision logic and tests:

- `Snapper/Annotate/CopyAndCloseDecision.swift` (created by 0021; spec
  `docs/specs/todo/0021-annotation-copy-and-close_TODO.md:46-53`): `start(isDragging:hasAttachedSheet:)`
  → `.refuse` / `.copy`, and `finish(copySucceeded:)` → `.close` / `.stayOpen`.
- `SnapperTests/CopyAndCloseDecisionTests.swift` (created by 0021; cases at
  `docs/specs/todo/0021-annotation-copy-and-close_TODO.md:126-131`).
- The controller flow (0021 spec `:63-88`): guard → `copyFlattenedImage()` (extracted from
  `copy(_:)`, today at `Snapper/Annotate/AnnotationWindowController.swift:237-249`) → `close()` on
  success. The close path relies on the document being clean after `markClean()`, so the
  `windowShouldClose` policy (`Snapper/Annotate/AnnotationWindowController.swift:193-209`) closes
  without prompting.
- Toolbar order test (0021 spec `:136-139`): Copy & Close is the last item, after Save and Copy.

macOS facts that **don't apply** on Windows:

- There's no main menu, so there's no "File → Copy & Close" item, no `MainMenuLayout` change, and no
  enabled/disabled menu validation. Ctrl+Enter is a window-local key handler.
- macOS matches menu key equivalents before the text view sees the key. In WPF, the equivalent is
  handling `PreviewKeyDown` (tunneling) on the annotation window, so the `TextBox` editor from 0042
  never sees Ctrl+Enter.
- "Attached sheet" becomes "one of this window's modal dialogs is open" (the Save dialog, the
  "Discard changes?" prompt, the Color dialog, or an error alert). While a modal dialog is open WPF
  disables the owner, so the guard is mostly defensive, but port it anyway.
- No `NSColorPanel` cleanup. The Windows Color dialog is modal (0040), so nothing outlives the window.

## User story

As a Snapper user on Windows who has finished annotating a capture, I click the checkmark
**Copy & Close** button (or press Ctrl+Enter). The annotated image is on my clipboard and the window
is gone, in one step and with no prompt.

## Scope & changes

### Pure logic (`Snapper.Core`, unit-tested)

- **New `windows/src/Snapper.Core/Annotate/CopyAndCloseDecision.cs`**
  (namespace `Snapper.Core.Annotate`): a `static class CopyAndCloseDecision` that ports the Swift enum
  and adds the Windows shortcut match. No WPF/WinForms/interop references.
  - `public enum CopyAndCloseStart { Refuse, Copy }`
  - `public static CopyAndCloseStart Start(bool isDragging, bool hasOpenDialog)` returns `Refuse`
    when either flag is true (the caller beeps), otherwise `Copy`.
  - `public enum CopyAndCloseFinish { Close, StayOpen }`
  - `public static CopyAndCloseFinish Finish(bool copySucceeded)` returns `Close` on success and
    `StayOpen` on failure.
  - `public const int VirtualKeyReturn = 0x0D;` (`VK_RETURN`. Main Enter and numpad Enter both map
    to it.)
  - `public static bool IsShortcut(int virtualKey, bool ctrl, bool shift, bool alt, bool win, bool isRepeat)`
    returns `true` only for `virtualKey == VirtualKeyReturn && ctrl && !shift && !alt && !win && !isRepeat`.
    Exact modifiers: Ctrl+Shift+Enter and Ctrl+Alt+Enter don't trigger. Auto-repeat is ignored so
    that holding Ctrl+Enter can't close the next annotation window that Windows activates after the
    first one closes.
    Use primitive parameters rather than 0027's `KeyChord`, so this doesn't depend on that type's
    exact shape.
- **New `windows/src/Snapper.Core/Annotate/AnnotationToolbarLayout.cs`**: the trailing
  action buttons as testable data.
  - `public enum AnnotationToolbarAction { Save, Copy, CopyAndClose }`
  - `public static IReadOnlyList<AnnotationToolbarAction> TrailingActions` =
    `[Save, Copy, CopyAndClose]` (PRD §8.2 order, Copy & Close at the very right end).
  - `public const string CopyAndCloseLabel = "Copy & Close";`
    `public const string CopyAndCloseToolTip = "Copy & Close (Ctrl+Enter)";`
    `public const string CopyAndCloseGlyph = "";` (Segoe Fluent Icons `CheckMark`).
  - **If 0040 or 0043 already created a Core type that describes the toolbar's action buttons**
    (order, labels, tooltips, glyphs), add `CopyAndClose` to that type instead of creating this file,
    keep its naming, and adapt the tests below to it. Don't duplicate the Save/Copy descriptors.

### Platform glue (`Snapper.Windows`, manually verified)

- **`windows/src/Snapper.Windows/Annotate/AnnotationWindow.xaml(.cs)`** (from 0040/0043):
  - **Toolbar button.** Add a Copy & Close button directly after Copy, at the right end of the
    toolbar. Its content is a `TextBlock` with `FontFamily="Segoe Fluent Icons"` and
    `Text = AnnotationToolbarLayout.CopyAndCloseGlyph`. Set `ToolTip = CopyAndCloseToolTip` and
    `AutomationProperties.Name = CopyAndCloseLabel`. Use the same style, size, and spacing as the
    Save and Copy buttons. Build the three trailing buttons in the order of
    `AnnotationToolbarLayout.TrailingActions` (a small switch from action to button), so the
    unit-tested order is the one shown. Leave the Save and Copy handlers unchanged.
  - **Extract** the body of 0043's Copy handler after the drag guard into
    `private bool TryCopyFlattenedImage()`: commit any in-progress text edit, flatten with
    `AnnotationExporter`, write to the clipboard with `Clipboard`, mark the document clean, and return
    `true`. On any failure (an exporter exception, or the clipboard throwing, e.g. `COMException`
    `CLIPBRD_E_CANT_OPEN` when another app holds the clipboard), show the existing
    `ErrorAlerts` "Couldn't copy to clipboard" alert owned by this window and return `false`,
    without marking the document clean. The Copy command keeps its drag guard and then calls this
    method, so its behavior doesn't change.
  - **Add `private void CopyAndClose()`**, called by the button's `Click` and by the shortcut:
    1. If `CopyAndCloseDecision.Start(isDragging: <canvas drag-in-progress state used by Copy/Save>,
       hasOpenDialog: <window's open-dialog state>) == Refuse`, call `System.Media.SystemSounds.Beep.Play()`
       (or the beep helper 0043 uses for Copy/Save) and return.
    2. `var copied = TryCopyFlattenedImage();` This commits any in-progress text first, so the text
       appears in the copied image.
    3. If `CopyAndCloseDecision.Finish(copied) == Close`, call `Close()`. The 0043 `Closing` handler
       still runs. It commits text (now a no-op), finds the document clean, and closes without the
       "Discard changes?" prompt. `Closed` then removes the window from `AnnotationWindowManager` as
       usual. Don't add a bypass flag: the clean document is what guarantees no prompt, as on macOS.
  - **Open-dialog state.** If 0043 already tracks "a modal dialog owned by this window is open" (for
    its `Closing` refusal), reuse it. Otherwise add `private int _openDialogCount` and increment and
    decrement it in `try/finally` around every modal dialog this window shows (Save dialog,
    "Discard changes?" prompt, Color dialog, error alerts). `hasOpenDialog` is `_openDialogCount > 0`.
  - **Ctrl+Enter.** In the window's `PreviewKeyDown` handler (add one, or extend 0043's if it routes
    Ctrl+S/Ctrl+C there), compute
    `var key = e.Key == Key.System ? e.SystemKey : e.Key; var vk = KeyInterop.VirtualKeyFromKey(key);`
    and the modifier flags from `Keyboard.Modifiers` (Ctrl/Shift/Alt/Windows). If
    `CopyAndCloseDecision.IsShortcut(vk, …, e.IsRepeat)` is true, set `e.Handled = true` and call
    `CopyAndClose()`. Because this runs in the tunneling phase on the window, it fires before the
    0042 text `TextBox` sees the key. That's what makes Ctrl+Enter work while editing text. Plain
    Enter isn't handled here, so the `TextBox` (`AcceptsReturn`) keeps inserting a new line. While an
    IME composition is active, WPF reports `Key.ImeProcessed`, so the shortcut doesn't fire mid-composition.
    Set `e.Handled = true` even when `Start` refuses, so a refused Ctrl+Enter never inserts a
    newline into an open text box.
- No change to `AnnotationWindowManager`, `UnsavedAnnotationsPolicy`, the Exit confirmation, or the
  shutdown block reason. They already react to the window closing.
- No `NativeMethods.txt` additions. No new NuGet packages.
- **docs/PRD.md**: no change. §8.2, §12, §13.2, and §13.7 already describe this behavior.

## Acceptance criteria

- [ ] The annotation toolbar's right end shows, from left to right: Save, Copy, **Copy & Close**.
      Copy & Close shows the Segoe Fluent Icons checkmark, its tooltip is "Copy & Close (Ctrl+Enter)",
      and its accessible name is "Copy & Close".
- [ ] Clicking Copy & Close puts the flattened, full-resolution annotated image on the clipboard (PNG
      + DIB, same content as Copy) and closes the window with no "Discard changes?" prompt, even with
      unsaved shapes.
- [ ] Ctrl+Enter (main or numpad Enter) does the same when an annotation window is active.
- [ ] While a text box is being edited, Ctrl+Enter commits the text (it appears in the copied image)
      and then copies and closes. Plain Enter still inserts a new line.
- [ ] If copying fails, the "Couldn't copy to clipboard" alert is shown and the window stays open and
      still dirty (closing it afterwards still asks "Discard changes?").
- [ ] During a shape drag, Copy & Close (button or Ctrl+Enter) beeps and does nothing; the drag
      continues.
- [ ] Ctrl+C (Copy) and Ctrl+S (Save) behave exactly as before.
- [ ] After Copy & Close, the window is removed from `AnnotationWindowManager`: Exit Snapper from the
      tray doesn't count it as unsaved, and it no longer contributes to the "Unsaved annotations"
      shutdown block reason.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed.

## Edge cases

- **Text being edited**: committing an empty text box removes it before the flatten (0042 behavior).
  Committing can make the document dirty. Marking it clean after a successful copy covers that.
- **IME composition in a text box**: Enter is consumed by the IME (`Key.ImeProcessed`), so
  Ctrl+Enter doesn't fire until the composition ends.
- **Several annotation windows**: only the active window receives the key event or the click, and
  only that window copies and closes. Windows then activates another window. Key auto-repeat is
  ignored, so holding Ctrl+Enter doesn't close that one too.
- **Empty document** (no shapes, never dirty): still copies the plain capture and closes.
- **Clipboard locked by another app**: the copy fails, the alert appears, and the window stays open
  and dirty.
- **Modal dialog open** (Save dialog, "Discard changes?", Color dialog, error alert): WPF disables the
  owner, so the click and the key never arrive. If they do (for example through a dispatcher
  re-entrancy path), `Start` refuses and beeps.
- **Ctrl+Shift+Enter, Ctrl+Alt+Enter, Alt+Enter, Win+Ctrl+Enter**: not the shortcut, so they're
  ignored by this handler. Inside a text box they keep their default `TextBox` behavior.
- **Mixed DPI / window on a 150% monitor**: export is at the image's pixel size (0043, 96 DPI
  render), independent of the monitor. Copy & Close adds no geometry.

## Test plan

- **Unit tests** (xUnit, `windows/tests/Snapper.Core.Tests/`):
  - `CopyAndCloseDecisionTests.cs` (new; port of `SnapperTests/CopyAndCloseDecisionTests.swift`):
    - `Start(isDragging: false, hasOpenDialog: false) == Copy`
    - `Start(isDragging: true, hasOpenDialog: false) == Refuse`
    - `Start(isDragging: false, hasOpenDialog: true) == Refuse`
    - `Start(isDragging: true, hasOpenDialog: true) == Refuse`
    - `Finish(copySucceeded: true) == Close`; `Finish(copySucceeded: false) == StayOpen`
    - `IsShortcut(0x0D, ctrl: true, shift: false, alt: false, win: false, isRepeat: false)` is true
    - `[Theory]` false cases: no Ctrl (plain Enter); Ctrl+Shift; Ctrl+Alt; Ctrl+Win; `isRepeat: true`;
      a different key with Ctrl (e.g. `0x43` 'C', `0x53` 'S', `0x20` Space)
    - `VirtualKeyReturn == 0x0D`
  - `AnnotationToolbarLayoutTests.cs` (new, or added to 0040/0043's toolbar test file if that type
    already exists):
    - `TrailingActions` equals `[Save, Copy, CopyAndClose]`, and its last element is `CopyAndClose`
    - `CopyAndCloseLabel == "Copy & Close"`, `CopyAndCloseToolTip == "Copy & Close (Ctrl+Enter)"`,
      and `CopyAndCloseGlyph == ""`
  - `UnsavedAnnotationsPolicy` tests (0043) already cover that a clean document closes without
    prompting. No change needed.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  succeed (both run on macOS).
- **Manual verification** (Windows 11 machine; can't be automated):
  1. Press Ctrl+Shift+6 and select a region. The toolbar's right end shows Save, Copy, and then a
     checkmark button. Hover over it: the tooltip reads "Copy & Close (Ctrl+Enter)".
  2. Draw a rectangle and an arrow, then click the checkmark. The window closes with **no** prompt.
     Paste into Paint (Ctrl+V): the image has both shapes. On a 150% monitor, the pasted image is
     1.5× the selection's DIP size (native pixels).
  3. Open another annotation, draw a shape, and press Ctrl+Enter. Same result as step 2. Repeat with
     the numpad Enter.
  4. Open another annotation, place a text box, type "Hello", press Enter (a new line appears), type
     "World", then press Ctrl+Enter without leaving the box. The window closes, and the pasted image
     shows both lines of text.
  5. Open another annotation and start dragging a rectangle. While holding the mouse button, press
     Ctrl+Enter: it beeps, the window stays open, and the drag continues.
  6. Open two annotation windows, draw in both, and press Ctrl+Enter in one, holding the keys down
     for a second. Only that window closes, and the other window stays open. Then choose Exit
     Snapper from the tray: the confirmation counts **1** unsaved annotation.
  7. Clipboard failure: run a PowerShell loop that holds the clipboard open (e.g. a small script that
     calls `OpenClipboard` and sleeps), then click the checkmark. The "Couldn't copy to clipboard"
     alert appears, and the window stays open. Close it with Ctrl+W: "Discard changes?" is asked.
     (If you can't reproduce a locked clipboard, mark this step as not run.)
  8. Open one annotation window, draw a shape, and press Ctrl+Enter. Then sign out: Windows'
     shutdown screen does **not** list Snapper with "Unsaved annotations". Cancel if it appears for
     another reason.
  9. Regression: Ctrl+C copies and leaves the window open; while editing text, Ctrl+C copies the
     selected text. Ctrl+S opens the Save dialog and saves. Ctrl+Shift+Enter in a text box doesn't
     close the window.

## Out of scope

- A "Save & Close" action, or any change to Save/Copy behavior (0043).
- Making the shortcut configurable in Settings (§10 covers only the three global shortcuts).
- Copying a file reference instead of image data. Copy & Close puts the same PNG + DIB on the
  clipboard as Copy (0031 `Support/Clipboard`).
- Packaging and the full §13.11 acceptance pass (0047).

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**. `dotnet test windows/Snapper.slnx` passed **1,251 tests**, with **0 failures and 0 skips** (17 new cases). The added tests cover every start/finish decision, exact modifiers, repeat/IME exclusion, Return virtual-key identity, and the toolbar order/descriptors. `git diff --check` also passed.

Implementation adaptations to the landed dependencies:

- Extended the existing `AnnotationToolbarLayout` and `AnnotationActionDescriptor` rather than adding a second toolbar description. Its descriptors now drive all three rendered trailing buttons.
- Reused 0043's `TryCopyFlattenedImageAsync()` rather than introducing a synchronous copy helper. Copy & Close commits text, temporarily disables this window's canvas/toolbar input and consumes keyboard input while encoding, then uses the normal close policy only after successful copying leaves the document clean. A defensive final-state check prevents closing over a newer edit. Failure restores input and focus. This action adds no discard-confirmation bypass, and plain Copy/Save retain their existing async behavior.
- The window's preview handler matches Ctrl+Enter before the text editor sees it. Other Enter modifier combinations retain native editor behavior; IME-processed keys and repeat presses do not match.
- PRD and AGENTS.md already describe the required behavior and architecture; neither needed changes.

GUI behavior has not been verified. Run these on Windows 11:

- [ ] not yet run by user: Open an annotation and confirm Save, Copy, then the checkmark button at the toolbar's right end; its tooltip is "Copy & Close (Ctrl+Enter)" and accessible name is "Copy & Close".
- [ ] not yet run by user: Draw a rectangle and arrow, click Copy & Close, and confirm the window closes without a prompt. Paste into Paint and check both shapes and native pixel dimensions, including on a 150% monitor.
- [ ] not yet run by user: Repeat with Ctrl+Enter and then numpad Ctrl+Enter; confirm both copy and close without prompting.
- [ ] not yet run by user: Edit a text box with "Hello", plain Enter, and "World"; press Ctrl+Enter without leaving it and confirm the copied image contains both lines.
- [ ] not yet run by user: During a shape drag, press Ctrl+Enter; confirm it beeps, the window stays open, and the drag continues.
- [ ] not yet run by user: Modify two editors, hold Ctrl+Enter in one, and confirm only that editor closes. Exit Snapper and confirm the unsaved count is one.
- [ ] not yet run by user: Hold the clipboard open from another process and try Copy & Close. Confirm the copy-error alert, open dirty window, restored input, and discard prompt on subsequent Ctrl+W. Mark this unrun if clipboard locking cannot be reproduced.
- [ ] not yet run by user: Copy & Close the only modified annotation, then sign out; confirm Snapper no longer blocks shutdown for "Unsaved annotations".
- [ ] not yet run by user: Regress Ctrl+C image copy, text-selection copy, and Ctrl+S save. Confirm Ctrl+Shift+Enter does not close the editor, plain Enter adds a line, and IME composition Enter does not trigger copying.
- [ ] not yet run by user: With this editor's Save, Color, or Discard dialog open, confirm Copy & Close cannot close the owner underneath it; dismiss the dialog and confirm the editor remains usable.
- [ ] not yet run by user: Copy & Close a large capture and immediately attempt drawing, toolbar changes, Undo, and another close while encoding. Confirm input is guarded, the copied snapshot is complete, and no newer edit is lost. On a reproducible copy failure, confirm canvas/toolbar input and focus are restored.
