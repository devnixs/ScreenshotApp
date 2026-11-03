# 0043 — Windows: export annotations at full resolution, Ctrl+S save, Ctrl+C copy, unsaved-changes prompts

> Source: docs/PRD.md §8.1 ("Discard changes?" and quit bullets), §8.2 (Save and Copy rows, "⌘C copies the image only when no text box is being edited"), §8.4, §9, §12 ("Saved or copied annotated images keep full resolution", "Closing a modified annotation window asks…"), §13.2 (Ctrl+S, Ctrl+C, Ctrl+W/Alt+F4), §13.5 (clipboard formats, full folder path in alerts), §13.7 (96-DPI export, shutdown block reason "Unsaved annotations"), §13.8, §13.11
> Note: on Windows the quit confirmation reads "Exit and discard N unsaved annotations?" with an **Exit** button (PRD §13.2). The type may keep the name `QuitMessage`.
> Depends on: 0025-windows-solution-tray-shell, 0026-windows-file-naming-save-folder, 0027-windows-app-settings, 0031-windows-screenshot-rectangle-capture, 0034-windows-recording-core, 0039-windows-annotation-document-undo, 0040-windows-annotation-window-canvas, 0041-windows-annotation-editing, 0042-windows-annotation-text-tool

## Context

This spec ports macOS spec 0020 (`docs/specs/done/0020-annotation-export_COMPLETED.md`, commit
`13063be`) to the Windows app in `windows/`. When it starts, these pieces exist (names from the
batch ownership table; check the real code first and use whatever the earlier specs actually named
things):

- **0025**: `App/AppCoordinator` with `Exit()` (the tray menu's **Exit Snapper**), `App.xaml` with
  `ShutdownMode=OnExplicitShutdown`, `NativeMethods.txt`.
- **0026**: Core `Support/CaptureKind`, `Support/FileNamer` (base name
  `Screenshot 2026-11-02 at 19.18.04`, `.png`, ` (2)`/` (3)` collision suffix through an injected
  "exists" predicate, injectable time zone), `Support/SaveFolderPolicy` (resolve/create/writability
  and the "Couldn't save to <folder>" text); Windows `Support/SaveFolder`.
- **0027**: Core `Settings/AppSettings` with the save folder path (default
  `%USERPROFILE%\Pictures\Screenshots`), `Support/KeyChord` (modifiers Ctrl/Alt/Shift/Win + virtual key).
- **0031**: Windows `Support/PngEncoder` (WPF `PngBitmapEncoder`), `Support/Clipboard` (registered
  `PNG` format + DIB), `Support/ErrorAlerts` (WinForms `TaskDialog`).
- **0034**: Windows `Recording/ShutdownBlocker` and the recording quit/shutdown save path in
  `AppCoordinator` (session end while recording registers "Saving recording…").
- **0039**: Core `Annotate/Model/AnnotationDocument` with `ImagePixelSize`, `PixelsPerPoint`,
  `Shapes`, selection, undo/redo, `IsDirty`, `MarkClean()` (port of
  `Snapper/Annotate/Model/AnnotationDocument.swift:114-123`; `MarkClean` requires no active edit).
- **0040**: Windows `Annotate/AnnotationWindow`, `Annotate/AnnotationCanvas`,
  `Annotate/ShapeRenderer` (draws shapes in image pixel coordinates into a WPF `DrawingContext`,
  shared by canvas and export), `Annotate/AnnotationWindowManager` (tracks open windows), the
  toolbar; the window holds the source `BitmapSource` and the capture time.
- **0041**: selection handles drawn by the canvas as editing chrome (not by `ShapeRenderer`).
- **0042**: text shapes, the `TextBox` editing overlay, a "commit the active text edit" entry point
  (empty text boxes are removed), Core text commit decisions, the WPF text measurer.

Nothing yet saves, copies, or flattens the annotated image, and closing a modified window, exiting
Snapper, or signing out does not care about unsaved annotations.

macOS reference implementation (port behavior and tests; don't redesign):

- `Snapper/Annotate/AnnotationExporter.swift:15-46` — flatten at the image's exact pixel size, image
  first, then `ShapeRenderer.draw(shapes, pixelsPerPoint:)` with no zoom and no selection chrome;
  `sizeMismatch` error (`:3-13`, `:24-27`).
- `Snapper/Annotate/AnnotationSaveDefaults.swift:3-11` — suggested name from `FileNamer` at the
  **capture** date, collision suffix against the target folder, no probing when the folder is `nil`.
- `Snapper/Annotate/UnsavedAnnotationsPolicy.swift:1-19` — close/quit decisions and strings.
- `Snapper/Annotate/AnnotationWindowController.swift:193-249` — `windowShouldClose`,
  `saveDocument(_:)`, `copy(_:)` (drag guard + beep, commit text, flatten, encode, write/copy,
  `markClean()`, error alert titles).
- `Snapper/Annotate/AnnotationWindowManager.swift:9-12` — `commitAllTextEdits()`, `dirtyWindowCount`.
- `Snapper/App/AppCoordinator.swift:178-194` — quit: commit all text, one confirmation, then the
  recording termination path.
- Tests: `SnapperTests/AnnotationSaveDefaultsTests.swift:5-28`,
  `SnapperTests/UnsavedAnnotationsPolicyTests.swift:5-37`,
  `SnapperTests/AnnotationExporterTests.swift:38-133` (raster tests; on Windows they become manual
  checks because WPF rendering can't run in the `net10.0` test project).

macOS facts that **don't apply** on Windows: there is no Edit/File main menu or responder chain
(shortcuts are routed explicitly, below), no sheets (WPF dialogs are owner-modal instead), no
`NSGraphicsContext` flip or bottom-left CTM (WPF is top-left, y down, like image pixels), no Display P3
profile preservation (Windows captures are 8-bit sRGB BGRA), and no 72-DPI PNG metadata rule (PNG
metadata follows 0031's encoder). Logout on Windows does **not** show the quit confirmation (§13.7).

## User story

As a Snapper user on Windows who annotated a capture, I press Ctrl+S to save it as a full-resolution
PNG (the dialog opens on my screenshot folder with a ready-made name) or Ctrl+C to put it on the
clipboard. I'm asked "Discard changes?" before closing a window with unsaved edits, asked once
before Exit Snapper throws away unsaved annotations, and Windows' shutdown screen lists Snapper
("Unsaved annotations") if I sign out with unsaved work.

## Scope & changes

### Pure logic (`Snapper.Core`, unit-tested)

- **`windows/src/Snapper.Core/Annotate/AnnotationSaveDefaults.cs`** — `public static class`, port of
  `AnnotationSaveDefaults.swift:3-11`:
  `public static string SuggestedFileName(DateTime captureTime, string? directory, Func<string, bool> fileExists, FileNamer? namer = null)`.
  Delegates to 0026's `FileNamer` for `CaptureKind.Screenshot` at **`captureTime`** (never "now"),
  using its unique-name method with an exists-predicate that calls `fileExists(Path.Combine(directory, candidate))`.
  When `directory` is `null`, return the plain file name and never call `fileExists`. Don't
  reimplement the date format or the suffix rule; pass `namer` through so tests can fix the time zone
  as 0026's tests do.
- **`windows/src/Snapper.Core/Annotate/UnsavedAnnotationsPolicy.cs`** — `public static class`, port of
  `UnsavedAnnotationsPolicy.swift:1-19`, plus the Windows session-end rule:
  - `public enum CloseDecision { CloseImmediately, ConfirmDiscard }`;
    `CloseDecision CloseDecisionFor(bool isDirtyAfterCommittingText)`.
  - `public abstract record QuitDecision` with `sealed record TerminateNow` and
    `sealed record Confirm(string Message)` (or an equivalent value type with equality);
    `QuitDecision QuitDecisionFor(int dirtyWindowCount)`: `TerminateNow` for 0 (and negatives),
    otherwise `Confirm(QuitMessage(n))`.
  - `string QuitMessage(int dirtyWindowCount)`: `"Exit and discard 1 unsaved annotation?"` for 1,
    `"Exit and discard {n} unsaved annotations?"` otherwise (0 falls back to the plural form). Each
    **window** counts as one annotation, whatever its number of shapes.
  - `public enum SessionEndDecision { Allow, Block }`;
    `SessionEndDecision SessionEndDecisionFor(bool isDirtyAfterCommittingText, bool isCritical)`:
    `Block` only when dirty and not critical (`ENDSESSION_CRITICAL` can't be blocked).
  - Constants: `DiscardTitle = "Discard changes?"`, `DiscardButton = "Discard"`,
    `QuitButton = "Quit"`, `CancelButton = "Cancel"`, `ShutdownBlockReason = "Unsaved annotations"`,
    `SaveFailedTitle(string folderPath) => $"Couldn't save to {folderPath}"` (full path, §13.5; reuse
    0026's `SaveFolderPolicy` message text if it already exposes exactly this),
    `CopyFailedTitle = "Couldn't copy to clipboard"`.
- **`windows/src/Snapper.Core/Annotate/AnnotationExportPlan.cs`** — pure description of the flatten
  render, so the size/DPI rule is tested headlessly:
  `public static AnnotationExportPlan Create(PixelSize documentImageSize, int sourcePixelWidth, int sourcePixelHeight)`
  throws `AnnotationExportException(AnnotationExportError.SizeMismatch)` when the document size
  differs from the source pixel size (programming error; also `Debug.Fail` in the Windows exporter,
  **not** here, so tests can assert the throw). Otherwise returns a record with
  `PixelWidth`, `PixelHeight` (= the source pixel size), `Dpi = 96.0`,
  `ImageRect = new RectD(0, 0, PixelWidth, PixelHeight)` (the rect the source is drawn into, in
  render units = image pixels), `PixelsPerDip = 1.0` (passed to WPF text rendering for export), and
  `DrawSelectionChrome = false`. Zero or negative sizes throw `AnnotationExportError.EmptyImage`.
  `AnnotationExportError` (`SizeMismatch`, `EmptyImage`, `RenderFailed`) and
  `AnnotationExportException` live in the same file; messages port `AnnotationExporter.swift:6-12`:
  "The annotation dimensions don't match the captured image.", "The annotated image couldn't be
  rendered.".
- **`windows/src/Snapper.Core/Annotate/AnnotationExportShortcuts.cs`** — keyboard routing for the
  export/close commands (WPF has no responder chain, so this replaces macOS's menu routing):
  `public enum AnnotationExportCommand { None, Save, CopyImage, Close, Refuse }`;
  `AnnotationExportCommand Resolve(KeyChord chord, bool isEditingText, bool isDragging)`:
  - Ctrl+S (exactly Ctrl, VK `0x53`) → `Save`, also while editing text (the save commits it
    first); `Refuse` while dragging (caller beeps; `AnnotationWindowController.swift:212`).
  - Ctrl+C (VK `0x43`) → `None` while editing text (the `TextBox` copies text; **the image is never
    copied while editing**, even with an empty text selection); `Refuse` while dragging; else `CopyImage`.
  - Ctrl+W (VK `0x57`) → `Close` (same path as the title-bar X), also while editing.
  - Anything else, including Ctrl+Shift+S/C/W, Alt/Win combinations, and Ctrl+Enter (0044) → `None`.
  If 0040–0042 already created a Core annotation key router (for V/R/A/T, Delete, Esc, Ctrl+Z/Y),
  **add these cases to it** instead of creating this file, and put the tests in that router's test
  file. Alt+F4 is not routed here: it is the system close and arrives as `Window.Closing`.

### Platform glue (`Snapper.Windows`, manually verified)

- **`windows/src/Snapper.Windows/Annotate/AnnotationExporter.cs`** — `internal static class`, runs on
  the dispatcher thread:
  `public static BitmapSource Flatten(BitmapSource source, AnnotationDocument document)`.
  1. `var plan = AnnotationExportPlan.Create(document.ImagePixelSize, source.PixelWidth, source.PixelHeight)`;
     on `SizeMismatch` call `Debug.Fail(...)` and rethrow.
  2. Build a `DrawingVisual`. In its `DrawingContext`: push a clip to `plan.ImageRect`; draw the
     source with `dc.DrawImage(source, plan.ImageRect)` — **always into the pixel rect, never at
     `source.Width/Height`**, which are DIPs derived from the source's own DPI and would rescale a
     150%/200% capture; set `RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor)`
     so the 1:1 copy is exact; then call 0040's `ShapeRenderer` with the document's shapes in z-order,
     `document.PixelsPerPoint`, an identity transform (image pixel = render unit; no `CanvasTransform`,
     no zoom, no monitor DPI), and `plan.PixelsPerDip` for text. Draw **no** selection handles, hover
     states, or text-editing chrome. If `ShapeRenderer` lacks an overload that takes `pixelsPerDip`
     or draws selection chrome, add a parameter (`pixelsPerDip`, `includeSelection: false`) without
     changing canvas output.
  3. `new RenderTargetBitmap(plan.PixelWidth, plan.PixelHeight, plan.Dpi, plan.Dpi, PixelFormats.Pbgra32)`,
     `Render(visual)`, `Freeze()`, return it. Wrap any rendering exception in
     `AnnotationExportException(RenderFailed)`.
  The result's `PixelWidth/PixelHeight` equal the capture's, whatever the window zoom or the monitor
  scale (§8.4, §13.7).
- **Capture time on the window**: `AnnotationWindow` exposes `CaptureTime` (the moment the
  Ctrl+Shift+6 capture completed). If 0040 doesn't already store it, add a constructor parameter and
  pass it from the Ctrl+Shift+6 path in `AppCoordinator`.
- **Toolbar** (in `AnnotationWindow`'s XAML/toolbar code from 0040): at the right end, **Save**
  (Segoe Fluent Icons `\uE74E`, tooltip "Save (Ctrl+S)") then **Copy** (`\uE8C8`, tooltip
  "Copy (Ctrl+C)"), with `AutomationProperties.Name` "Save"/"Copy". If 0040 left placeholders, wire
  them. Clicking either first commits any in-progress text edit (clicking outside the box commits it,
  §8.3), then runs the command. 0044 adds Copy & Close after Copy.
- **Keyboard**: in `AnnotationWindow`'s `KeyDown` handler (bubbling, so a focused `TextBox` handles
  Ctrl+C itself first), build a `KeyChord` from the event (use `e.Key == Key.System ? e.SystemKey : e.Key`
  and `KeyInterop.VirtualKeyFromKey`) and call `AnnotationExportShortcuts.Resolve` with the canvas's
  editing/dragging state. `Save`/`CopyImage`/`Close` → run the action and set `e.Handled = true`;
  `Refuse` → `SystemSounds.Beep.Play()`, handled; `None` → leave unhandled. **Don't** use a window
  `KeyBinding` or `CommandBinding` for `ApplicationCommands.Copy`: when the `TextBox` has an empty
  selection it doesn't handle Ctrl+C, and a window binding would then copy the image.
- **Save** (`AnnotationWindow.Save()`), port of `AnnotationWindowController.swift:211-235`:
  1. Commit any in-progress text edit (0042).
  2. Resolve the default folder from `AppSettings` through 0026's `SaveFolder`/`SaveFolderPolicy`,
     creating it if missing. **If that fails, show no alert**: use `null` so the dialog opens on its
     default location (the "Couldn't save to … Choose Folder…" alert is only for Ctrl+Shift+4).
  3. `var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = true, InitialDirectory = folder, FileName = AnnotationSaveDefaults.SuggestedFileName(window.CaptureTime, folder, File.Exists) }`.
     `ShowDialog(this)` (owner-modal to the annotation window). Every Save starts from the Settings
     folder; don't write the chosen folder back to settings. The dialog's own overwrite prompt
     handles typed existing names.
  4. On OK: `AnnotationExporter.Flatten(...)`, encode with 0031's `PngEncoder` (same call and DPI
     metadata as a Ctrl+Shift+4 PNG of the same scale; pass `document.PixelsPerPoint` if its API
     takes a scale), and write atomically: if 0031 has an atomic write helper use it, otherwise write
     `<target>.tmp` in the same folder and `File.Move(tmp, target, overwrite: true)`, deleting the temp
     file on failure. Don't write a second PNG encoder.
  5. On success `document.MarkClean()`. The window **stays open**; no shutter sound, no clipboard, no
     Explorer reveal (*Screenshot destination* and *Play shutter sound* apply only to Ctrl+Shift+4).
  6. On a flatten/encode/write error: 0031's `ErrorAlerts` with title
     `UnsavedAnnotationsPolicy.SaveFailedTitle(<full chosen folder path>)` and the exception message,
     no Choose Folder… button. The document stays dirty.
  7. On Cancel: nothing changes (stays dirty).
- **Copy** (`AnnotationWindow.CopyImage()`), port of `AnnotationWindowController.swift:237-249`:
  commit text, flatten, write with 0031's `Clipboard` (PNG + DIB, same as screenshots), then
  `document.MarkClean()`; window stays open; no sound. If flattening fails or the clipboard can't be
  opened (`Clipboard` returns false or throws `ExternalException`/`COMException`), show `ErrorAlerts`
  with `CopyFailedTitle` and the error, and the document stays dirty. Structure it as
  `bool TryCopyFlattenedImage()` so 0044 can reuse it.
- **Close** (`Window.Closing` handler; covers the title-bar X, Ctrl+W via `Close()`, Alt+F4, and
  the taskbar thumbnail's Close), port of `windowShouldClose` (`:193-209`):
  1. If `closeConfirmed` or `sessionEnding` is set, allow.
  2. Commit any in-progress text edit (an empty box is removed and doesn't make the document dirty).
  3. If `CloseDecisionFor(document.IsDirty) == CloseImmediately`, allow.
  4. Otherwise show a WinForms `TaskDialog` owned by the window (`TaskDialog.ShowDialog(hwnd, page)`):
     caption "Snapper", heading "Discard changes?", warning icon, buttons **Discard** (default) and
     **Cancel** (`AllowCancel = true`, so Esc = Cancel). Discard → allow (set `closeConfirmed`);
     Cancel → `e.Cancel = true`, window and edits untouched. Use a `isPrompting` flag: a second close
     request while the dialog is up is cancelled with a beep (no second dialog).
  On close, 0040's manager drops the window as it already does.
- **`AnnotationWindowManager`** (extend 0040's): `CommitAllTextEdits()`, `int DirtyWindowCount`
  (windows whose document `IsDirty` after commit), and `CloseAllWithoutPrompt()` (sets
  `closeConfirmed` on each window and closes it).
- **Exit Snapper** (`AppCoordinator.Exit()`, extends 0025/0034), port of
  `AppCoordinator.swift:178-194`:
  1. `annotationWindowManager.CommitAllTextEdits()`.
  2. `QuitDecisionFor(DirtyWindowCount)`: on `Confirm(message)` show **one** unowned, topmost
     `TaskDialog` (caption "Snapper", heading = message, warning icon, buttons **Exit** (default) and
     **Cancel**), whatever the number of windows. Cancel → return; everything stays open.
  3. Quit (or no dirty windows) → `CloseAllWithoutPrompt()`, then continue with 0034's existing exit
     path (stop and save an active recording first, then `Application.Current.Shutdown()`). The
     windows are never asked one by one. Keep any earlier cancel logic; any cancel wins.
- **Sign-out, restart, shutdown** (§13.7) — no Snapper prompt:
  - In `AnnotationWindow.SourceInitialized`, add an `HwndSource` hook:
    - `WM_QUERYENDSESSION`: commit the text edit, then
      `SessionEndDecisionFor(document.IsDirty, isCritical: (lParam & ENDSESSION_CRITICAL) != 0)`.
      `Block` → `ShutdownBlockReasonCreate(hwnd, UnsavedAnnotationsPolicy.ShutdownBlockReason)`,
      return `0` (FALSE) with `handled = true`, so Windows' shutdown screen lists Snapper with
      "Unsaved annotations" and the user chooses Cancel or Shut down anyway. `Allow` → don't handle.
    - `WM_ENDSESSION` with `wParam == FALSE` (the user cancelled): `ShutdownBlockReasonDestroy(hwnd)`.
      With `wParam != FALSE`: set `sessionEnding = true` so no "Discard changes?" dialog can appear
      while the session ends.
    Each dirty annotation window (they are visible top-level windows) registers its own reason; the
    hook is idempotent if `WM_QUERYENDSESSION` arrives twice.
  - Whatever app-level session-end handler 0034 installed (for "Saving recording…") must **not**
    call the Exit confirmation and must not run `CloseAllWithoutPrompt`-with-prompt paths. If 0034
    routes session end through `Exit()`, split out a non-prompting variant. If 0034's
    `ShutdownBlocker` already wraps `ShutdownBlockReasonCreate/Destroy` for an HWND, reuse it.
- **`NativeMethods.txt`**: add (if not already present from 0034) `ShutdownBlockReasonCreate`,
  `ShutdownBlockReasonDestroy`, `WM_QUERYENDSESSION`, `WM_ENDSESSION`, `ENDSESSION_CRITICAL`.
- No new projects or packages. No PRD change (see Decisions).

### Decisions (ported from macOS 0020; PRD silent, consistent with its intent)

1. Save and Copy both leave the window open and both mark the document clean ("saved or copied" in
   §8.1 counts either).
2. Save ignores *Screenshot destination* and *Play shutter sound* and doesn't reveal the file.
3. If the default folder can't be created, the Save dialog still opens (default location), no alert.
4. The dialog's chosen folder isn't remembered; each Save opens on the Settings folder.
5. "Discard changes?" has Discard and Cancel only; the exit dialog has Quit and Cancel, counting
   dirty **windows**, singular for 1.
6. The toolbar buttons and Ctrl+S commit an in-progress text edit first; Ctrl+C while editing copies
   only text.
7. A placed-but-empty text box doesn't make the document dirty once committed.

## Acceptance criteria

- [ ] Ctrl+S and the Save button open an owner-modal Save dialog on the Settings save folder
      (created if missing) with `Screenshot <capture date> at <capture time>.png`, plus ` (2)`, ` (3)`…
      when that name already exists there.
- [ ] The saved PNG's pixel size equals the captured image's pixel size (for example 1.5× the
      selection's DIP size on a 150% monitor), whatever the window size/zoom and whichever monitor
      the window is on (§8.4, §13.7).
- [ ] The saved PNG and the copied image contain every shape (rectangles, arrows, outlined text) and
      no selection handles or editing chrome.
- [ ] Ctrl+C and the Copy button put the flattened, full-resolution image on the clipboard (PNG and
      DIB); the window stays open.
- [ ] While a text box is being edited, Ctrl+C copies the selected text, never the image (also with
      an empty selection).
- [ ] Closing a window with changes not saved or copied (X, Ctrl+W, Alt+F4) asks "Discard changes?"
      (Discard / Cancel). Cancel keeps the window and edits; Discard closes it.
- [ ] Closing right after a Save or Copy with no further edits doesn't prompt; editing after a Copy
      makes it prompt again.
- [ ] Exit Snapper with N ≥ 1 dirty windows shows exactly one dialog, "Exit and discard N unsaved
      annotations?" (singular for 1). Quit exits; Cancel keeps everything. With 0 dirty windows,
      Snapper exits without asking.
- [ ] Signing out with a dirty annotation window shows no Snapper dialog; Windows' shutdown screen
      lists Snapper with "Unsaved annotations". Cancel keeps Snapper and the window intact.
- [ ] Save or write errors show a short alert with the full folder path; the document stays dirty.
- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` pass.

## Edge cases

- **Mixed DPI**: a capture from a 150% monitor shown in a window on a 100% monitor (or moved between
  monitors) exports at the capture's pixel size; nothing in the export path reads the window's DPI,
  `CanvasTransform`, or `VisualTreeHelper.GetDpi`. Text uses `PixelsPerDip = 1.0` in export.
- **Source `BitmapSource` with non-96 DPI** (e.g. 144 from a 150% capture): drawn into the pixel
  rect, so it's never rescaled.
- **No shapes**: Save and Copy output the unmodified image, pixel-identical.
- **A selected shape**: exported without handles.
- **Shapes partly outside the image**: clipped to the image; no error.
- **In-progress text edit** at Save, Copy button, close, Exit, or `WM_QUERYENDSESSION`: committed
  first; empty text is removed and doesn't count as a change.
- **Ctrl+C in a text box with an empty selection**: nothing happens; never falls back to the image.
- **Ctrl+S/Ctrl+C during a mouse drag**: beep, ignored.
- **Default folder missing**: created. Uncreatable → dialog on its default location. User picks an
  unwritable folder (e.g. `C:\Windows`) → write fails, alert, still dirty, temp file cleaned up.
- **Name collision**: suggested name gets ` (2)`…; a typed existing name triggers the dialog's
  overwrite prompt.
- **Several windows from captures in the same second**: each suggests a unique name at dialog time.
- **Clipboard locked by another app**: copy fails → "Couldn't copy to clipboard" alert, still dirty.
- **Exit while a Save dialog or "Discard changes?" is open on a window**: the exit dialog still
  appears; on Quit, Snapper exits without crashing or showing more prompts.
- **Exit while recording**: unreachable from the tray (a click stops the recording, §13.3); the
  ordering (annotations confirm first, then 0034's save) still holds for any other caller.
- **Session end while recording and with dirty annotations**: both reasons ("Saving recording…" from
  0034 and "Unsaved annotations") may appear; 0034's save behavior is unchanged.
- **Critical session end** (`ENDSESSION_CRITICAL`): not blocked.
- **Intermediate state**: until 0044, Ctrl+Enter does nothing new in the window.

## Test plan

- **Unit tests** — `windows/tests/Snapper.Core.Tests/AnnotationSaveDefaultsTests.cs` (port of
  `SnapperTests/AnnotationSaveDefaultsTests.swift:5-28`; fixed UTC namer, capture time
  2026-11-02 19:18:04):
  - `CaptureDateAndCollisions`: for `taken` = 0, 1, 2 the predicate reports the first `taken` probes
    as existing → `"Screenshot 2026-11-02 at 19.18.04.png"`, `"… (2).png"`, `"… (3).png"`; every
    probed path's directory equals the given directory (e.g. `C:\Users\me\Pictures\Screenshots`).
  - `NullDirectoryNeverProbes`: `directory = null` → base name; the predicate is never called.
  - `UsesCaptureTimeNotNow`: a capture time in 2020 appears in the name.
- **Unit tests** — `windows/tests/Snapper.Core.Tests/UnsavedAnnotationsPolicyTests.cs` (port of
  `SnapperTests/UnsavedAnnotationsPolicyTests.swift:5-37`):
  - `MessagesAndDecisions`: `QuitDecisionFor(0)` is `TerminateNow`; for 1, 2, 3, 10 the message is
    `"Exit and discard {n} unsaved annotation{s}?"` (singular only for 1) and the decision is
    `Confirm(message)`; `QuitMessage(0) == "Exit and discard 0 unsaved annotations?"`.
  - `CloseDecision`: `true` → `ConfirmDiscard`, `false` → `CloseImmediately`.
  - `RealModelCloseTable` with 0039's `AnnotationDocument` (200×100, 1×) and 0042's text commit
    decision: new document → `CloseImmediately`; committing an **empty** text draft →
    `CloseImmediately`; committing "Hi" → `ConfirmDiscard`; `MarkClean()` → `CloseImmediately`; add
    a rectangle → `ConfirmDiscard`; `MarkClean()` → `CloseImmediately`; recolor the shape →
    `ConfirmDiscard`. If the model can't satisfy the empty-text row, fix it in this spec.
  - `SessionEnd`: (dirty, not critical) → `Block`; (clean, not critical) → `Allow`; (dirty,
    critical) → `Allow`; `ShutdownBlockReason == "Unsaved annotations"`.
  - `Strings`: `DiscardTitle == "Discard changes?"`, buttons "Discard"/"Quit"/"Cancel",
    `SaveFailedTitle(@"D:\Shots") == @"Couldn't save to D:\Shots"`,
    `CopyFailedTitle == "Couldn't copy to clipboard"`.
- **Unit tests** — `windows/tests/Snapper.Core.Tests/AnnotationExportPlanTests.cs`:
  - `SizeEqualsSourcePixels` (theory over pixels-per-point 1.0, 1.5, 2.0 ↔ 100/150/200% captures,
    sizes 300×200, 450×300, 600×400): `PixelWidth/PixelHeight` equal the source pixel size (not the
    DIP size), `Dpi == 96`, `PixelsPerDip == 1.0`, `ImageRect == (0, 0, w, h)`,
    `DrawSelectionChrome == false`.
  - `IndependentOfMonitor`: the plan has no monitor/zoom input; two documents with identical shapes
    whose rect was round-tripped through 0040's `CanvasTransform` at view scales 0.25 and 1.0 (and
    window DPI 1.0 / 1.5) produce equal plans and equal shape lists (port of
    `AnnotationExporterTests.swift:96-110`, model half).
  - `SizeMismatchThrows`: document 20×100 vs source 200×100 throws with `SizeMismatch`.
  - `EmptyImageThrows`: 0×100 throws with `EmptyImage`.
- **Unit tests** — `windows/tests/Snapper.Core.Tests/AnnotationExportShortcutsTests.cs` (or the
  existing annotation key router's tests):
  - Ctrl+S → `Save` (editing or not); Ctrl+S while dragging → `Refuse`.
  - Ctrl+C not editing → `CopyImage`; editing → `None`; dragging (not editing) → `Refuse`.
  - Ctrl+W → `Close` (editing or not).
  - `None` for Ctrl+Shift+S, Ctrl+Alt+C, Alt+S, Win+S, plain S/C/W, Ctrl+Enter.
- **Raster export** can't run in the `net10.0` test project (it needs WPF). The macOS raster cases
  (`AnnotationExporterTests.swift:38-133`: exact size, orientation, rect interior/stroke, stroke
  scaling with pixels-per-point, arrow, clipping, text ink, z-order, no selection chrome) are covered
  by the Windows manual checks below.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  succeed (on macOS too).
- **Manual verification** (Windows 11; ideally two monitors at 100% and 150%):
  1. On the 150% monitor press Ctrl+Shift+6 and select about 400×300 DIPs (the readout shows it).
     Draw a rectangle, an arrow, and a text box; leave the rectangle selected.
  2. Press Ctrl+S. An owner-modal Save dialog opens in `%USERPROFILE%\Pictures\Screenshots` with
     `Screenshot <date> at <time>.png` matching the **capture** time. Save.
  3. In PowerShell (Windows-only pixel-size check):
     `Add-Type -AssemblyName PresentationCore; $d=[Windows.Media.Imaging.BitmapDecoder]::Create([Uri]"$env:USERPROFILE\Pictures\Screenshots\Screenshot … .png",'None','OnLoad'); $d.Frames[0].PixelWidth; $d.Frames[0].PixelHeight`.
     The size is 1.5× the selection's DIP size (≈ 600×450), equal to a Ctrl+Shift+4 capture of the
     same area. Open the file in Paint and zoom in: all three shapes are present, the text has its
     outline, the top-left of the image is the top-left of the selection (not mirrored), and there
     are no selection handles.
  4. Drag the annotation window to the 100% monitor and resize it very small. Ctrl+S again: the
     suggested name ends in ` (2)`; save; step 3's command shows the **same** pixel size, and the two
     files look identical in Paint.
  5. Rename the Screenshots folder, press Ctrl+S: the folder is recreated and the dialog opens in
     it. Cancel; the window still prompts on close. Restore the folder.
  6. Press Ctrl+C (no text being edited). Paste into Paint: the canvas size in the status bar is the
     full pixel size and the shapes are there. Paste into Word or WordPad too (DIB path). The window
     is still open.
  7. Double-click the text, select part of it, press Ctrl+C, paste into Notepad: only the text.
     Then with the text box editing and **no** selection, press Ctrl+C and paste into Paint: the
     previously copied content, not a new image.
  8. Draw a shape, click the title-bar X: "Discard changes?" appears. Cancel: window and shape remain.
     Press Ctrl+W: Discard closes it. Repeat with Alt+F4.
  9. New annotation, draw, Ctrl+C, close: no prompt. New annotation, draw, Ctrl+C, draw again,
     close: prompt. New annotation, place a text box, type nothing, close: no prompt.
  10. Open two annotation windows and draw in both. Tray → Exit Snapper: exactly one dialog,
      "Exit and discard 2 unsaved annotations?". Cancel: both windows remain. Exit again → Exit:
      Snapper exits, the tray icon disappears, no other prompt. With one dirty window the text is
      "Exit and discard 1 unsaved annotation?"; with only clean windows Snapper exits immediately.
  11. Ctrl+S, choose `C:\Windows`, Save: an alert "Couldn't save to C:\Windows" with the error; no
      `.tmp` file is left; the window still prompts on close.
  12. Toolbar: Save and Copy sit at the right end with Segoe Fluent glyphs and tooltips
      "Save (Ctrl+S)" / "Copy (Ctrl+C)". While typing non-empty text, click Copy: the text commits and
      appears in the pasted image. Save/Copy play no sound and don't open Explorer.
  13. With a dirty annotation window, sign out (Start → user → Sign out). No Snapper dialog appears;
      Windows' "Closing apps" screen lists Snapper with "Unsaved annotations". Click Cancel: Snapper
      and the window remain, and closing the window still asks "Discard changes?". Sign out again and
      choose Sign out anyway: the session ends. With only clean windows, sign-out isn't blocked.
  14. Open a Save dialog on a dirty window, then Tray → Exit Snapper → Quit: Snapper exits without a
      crash or extra prompts.

## Out of scope

- Copy & Close (toolbar checkmark, Ctrl+Enter): 0044.
- The capture, PNG encoder, clipboard, and alert helpers (0031); the "Couldn't save to … Choose
  Folder…" flow for Ctrl+Shift+4 (0031).
- Shape model and document/undo (0038, 0039); window, canvas, toolbar, `ShapeRenderer` (0040);
  selection and styling (0041); text editing and rendering (0042). This spec only adds an export
  entry point, plus `pixelsPerDip`/`includeSelection` parameters if needed.
- Saving a recording on Exit or session end (0034).
- JPEG or other formats (PRD §2), "Save As" format options.
- The macOS app (spec 0020 already shipped it).
