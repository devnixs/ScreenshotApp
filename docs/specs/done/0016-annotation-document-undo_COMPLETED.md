# 0016 — Add the pure annotation document with coalesced undo/redo and dirty tracking

> Source: docs/PRD.md §8.1 (unsaved-changes tracking, lines 116–117), §8.2 (Undo/Redo and Delete rows, lines 130–131; recolor/restroke applies to the selection, line 137), §8.3 (empty text boxes removed on commit, line 143), §8.4 (shapes stored in image coordinates, lines 149–150), §12 (lines 199, 206)
> Depends on: 0015-annotation-shape-model

## Context

Spec 0015 provides the pure shape model in `Snapper/Annotate/Model/`:
`AnnotationShape` (rectangle / arrow / text, each with a stable `id: UUID`, a color, a stroke
width, geometry in **image pixel coordinates**), `ShapeHandle`, per-shape hit-testing, move, and
resize. This spec adds the owner of a whole annotation session: the ordered list of
shapes, which one is selected, the undo/redo history, and whether the window has unsaved changes.

The PRD requires:

- §8.2: Undo/Redo (⌘Z / ⇧⌘Z) "covers every edit, including moves, resizes, color changes, and
  text edits". Delete (⌫) "deletes the selected shape". "Changing the color or stroke width while a
  shape is selected applies the change to that shape."
- §8.1: "Closing a window with changes that have not been saved **or copied** asks 'Discard
  changes?'", and quitting with N such windows asks once.
- §8.3: on commit, "empty text boxes are removed".
- §8.4 / AGENTS.md: shapes are stored in image pixel coordinates; export never depends on window
  zoom.

AGENTS.md ("Architecture rules") requires the annotation model, **including undo/redo**, to be
pure Swift with unit tests, with views only rendering it and forwarding input. This spec delivers
that model: `AnnotationDocument`. It contains no AppKit, no drawing, and no `UndoManager`.

## User story

As a Snapper user annotating a capture, every edit I make (drawing, moving, resizing, recoloring,
changing stroke width, editing text, deleting) is one step I can undo and redo, a whole drag counts
as a single step, and Snapper knows exactly whether my annotation has changes I haven't saved or
copied — including when I undo back to the state I last saved.

(The user-visible wiring of this model lands in 0017–0020; this spec is the pure foundation.)

## Design decisions

These are engineering decisions inside the PRD's behavior. They are fixed for the implementer.

1. **Value type.** `AnnotationDocument` is a `struct` (`Sendable`), not a class. The window
   controller from 0017 owns it as a `var document: AnnotationDocument` and re-renders after each
   mutation. Value semantics make snapshots free (copy-on-write arrays), make tests trivial, and
   avoid shared mutable state across actors. It is **not** `@MainActor`; it is plain pure Swift.
2. **The document does not hold the image.** The `CGImage` stays with the controller (0017). The
   document only knows `imagePixelSize: CGSize` and `pixelsPerPoint: CGFloat` (e.g. `2` for a
   Retina capture), which later specs use to bound shapes and convert "image points" (stroke widths,
   text sizes from 0003's `StrokeWidth`) to pixels.
3. **Snapshot-based undo, not command-based.** Each undo/redo entry is a full `State`
   (`shapes: [AnnotationShape]`, `selectedShapeID: UUID?`, `revision: Int`). Justification:
   - Shapes are small value types; a document holds tens of them. Copying the array is cheap, and
     Swift's copy-on-write shares storage between unchanged snapshots.
   - Every edit kind (add, delete, move, resize, recolor, restroke, text change, delete-on-empty
     text) is undone by the same code path. Command-based undo would need an inverse for each, and
     each inverse is a place for bugs (z-order on undo of delete, multi-step gestures, etc.).
   - Coalescing a gesture is just "remember the state at `beginEdit()`", and no-op detection is just
     array equality (`AnnotationShape` is `Equatable` per 0015).
4. **No history limit.** The undo stack is unbounded for the lifetime of the window. Snapshots are
   tiny and windows are short-lived; a cap would add a case (undo past the cap vs. the clean marker)
   with no user benefit.
5. **Gesture coalescing** via `beginEdit()` / `commitEdit()` / `cancelEdit()`:
   - `beginEdit()` records the current state as the session's starting point. While a session is
     open, `add` / `delete` / `replace` / `select` mutate the live state (so the canvas can render
     the drag live) but push nothing.
   - `commitEdit()` closes the session. If `shapes` differ from the session start, it pushes the
     start state as **one** undo entry, assigns a new revision, and clears the redo stack. If
     `shapes` are equal to the start (nothing changed), it pushes **nothing**, keeps the revision,
     and leaves the redo stack intact.
   - `cancelEdit()` restores the session's starting `shapes` and `selectedShapeID` exactly and
     pushes nothing.
   - **Outside a session**, each `add` / `delete(id:)` / `replace(_:)` is its own atomic edit: it
     behaves like `beginEdit()` + the mutation + `commitEdit()`. This is what 0018 uses for ⌫ and
     for recolor/restroke of the selected shape (each one undo step); drags and text-editing
     sessions (0017–0019) use explicit sessions.
6. **Selection is not undoable on its own.** `select(id:)` never pushes an undo entry, never clears
   the redo stack, and never changes `isDirty`. But each undo entry captures the selection that
   existed **at that step**, so:
   - `undo()` restores both the shapes and the selection as they were *before* the undone edit
     (e.g. undoing a delete brings the shape back at its old z-index **and selected**, because it was
     selected when ⌫ was pressed; undoing a shape creation removes it and restores the prior
     selection).
   - `redo()` restores the shapes and selection as they were when that edit was committed.
   - After any restore, if the restored `selectedShapeID` doesn't exist in `shapes`, it becomes
     `nil` (invariant below).
7. **Dirty tracking via unique revisions.** Every committed state carries a `revision: Int`. A new
   commit gets `nextRevision` (a counter that only ever increases, so a revision number is never
   reused). Undo/redo restore the revision stored in the entry. `markClean()` sets
   `cleanRevision = revision`. `isDirty` is `revision != cleanRevision` (plus uncommitted changes in
   an open session, see API).
   - Undoing back to the last saved/copied state makes the document **clean again**, so closing the
     window then doesn't ask "Discard changes?" (PRD §8.1 asks only when there are changes not saved
     or copied; after undoing, there are none).
   - Unique revisions (rather than a plain change counter that goes +1 on edit and −1 on undo) avoid
     the classic bug: save → undo → make a *different* edit. A plain counter would return to the
     clean value and wrongly report clean; a unique revision does not.
   - A freshly created document is clean (`revision == cleanRevision == 0`): a capture opened and
     closed without edits closes without prompting.
8. **No Foundation `UndoManager` in the model.** 0018 bridges the Edit menu's Undo/Redo items
   (⌘Z / ⇧⌘Z, from 0001's main menu) to `undo()` / `redo()`, and uses `canUndo` / `canRedo` for
   menu validation.

## Scope & changes

- **Pure — new file `Snapper/Annotate/Model/AnnotationDocument.swift`** containing:

  ```swift
  struct AnnotationDocument: Sendable {
      struct State: Equatable, Sendable {      // also used as the undo/redo entry
          var shapes: [AnnotationShape]
          var selectedShapeID: UUID?
          var revision: Int
      }

      let imagePixelSize: CGSize
      let pixelsPerPoint: CGFloat

      init(imagePixelSize: CGSize, pixelsPerPoint: CGFloat)

      // Read access
      private(set) var shapes: [AnnotationShape]      // z-order = array order; last = topmost
      private(set) var selectedShapeID: UUID?
      var selectedShape: AnnotationShape? { get }
      func shape(withID id: UUID) -> AnnotationShape?
      func topmostShape(at point: CGPoint, tolerance: CGFloat) -> AnnotationShape?

      // Edits (atomic undo step outside a session; coalesced inside one)
      mutating func add(_ shape: AnnotationShape)
      mutating func delete(id: UUID)
      mutating func replace(_ shape: AnnotationShape)

      // Selection (never undoable on its own)
      mutating func select(id: UUID?)

      // Gesture coalescing
      var isEditing: Bool { get }
      mutating func beginEdit()
      mutating func commitEdit()
      mutating func cancelEdit()

      // History
      var canUndo: Bool { get }
      var canRedo: Bool { get }
      mutating func undo()
      mutating func redo()

      // Unsaved-changes tracking
      var isDirty: Bool { get }
      mutating func markClean()
  }
  ```

  Exact private storage is up to the implementer (suggested: `current: State`, `undoStack: [State]`,
  `redoStack: [State]`, `sessionStart: State?`, `nextRevision: Int`, `cleanRevision: Int`). The
  public surface above is what 0017–0020 rely on. Behavior of each member:

  - `init`: empty `shapes`, no selection, empty stacks, `revision = 0`, `cleanRevision = 0`,
    `nextRevision = 1`. `imagePixelSize` width/height and `pixelsPerPoint` must be > 0; check with
    `assert` (debug) and document it — callers (0017) always pass the captured image's real size.
  - `add(_:)`: appends the shape at the top of the z-order. Does **not** change the selection
    (callers that want the new shape selected call `select(id:)` inside the same session). A shape
    whose `id` already exists is a programmer error: `assertionFailure` and ignore (no change, no
    undo step).
  - `delete(id:)`: removes that shape. If it was selected, `selectedShapeID` becomes `nil`. Unknown
    `id` → no change, no undo step.
  - `replace(_:)`: replaces the shape with the same `id` **at the same index** (z-order unchanged).
    Used for move, resize, recolor, restroke, and text changes. Unknown `id` → no change. Replacing
    with an identical value → no undo step (no-op commit rule).
  - `select(id:)`: sets the selection; `nil` deselects; an `id` not in `shapes` sets `nil`. Never
    touches the stacks, the revision, or `isDirty`.
  - `selectedShape`, `shape(withID:)`: lookups.
  - `topmostShape(at:tolerance:)`: iterates `shapes` from last to first and returns the first shape
    whose 0015 hit-test predicate accepts `point` (image pixel coordinates) with `tolerance` (pixels);
    `nil` if none. Use exactly the hit-test API 0015 defines on `AnnotationShape` — do not
    re-implement per-shape geometry here.
  - `beginEdit()`: stores the current state as the session start. Calling it while a session is
    already open is a programmer error: `assertionFailure`, and the open session continues
    unchanged.
  - `commitEdit()` / `cancelEdit()`: as in Design decision 5. With no open session they do nothing.
  - `isEditing`: `true` between `beginEdit()` and `commitEdit()`/`cancelEdit()`.
  - `canUndo` / `canRedo`: `false` while `isEditing`; otherwise whether the respective stack is
    non-empty.
  - `undo()`: no-op if `!canUndo`. Otherwise push the current state onto the redo stack, pop the
    undo stack, and restore it (shapes, selection, revision), then enforce the selection invariant.
  - `redo()`: mirror image of `undo()`.
  - `isDirty`: `revision != cleanRevision`, **or** `isEditing` and the live `shapes` differ from
    the session start's `shapes` (so a window closed mid-gesture is treated as modified).
  - `markClean()`: `cleanRevision = revision`. Called by 0020 after a successful save **or** copy.
    It must be called with no open session (`assert(!isEditing)`); 0020 commits any in-progress
    text edit before exporting.
  - **Invariants** (hold after every public mutating call): shape `id`s are unique; `selectedShapeID`
    is `nil` or the `id` of a shape in `shapes`; any successful commit clears the redo stack; a
    no-op commit leaves both stacks untouched.

- **Empty text boxes (PRD §8.3) — contract for 0019, enforced by tests here.** The document has no
  special text rule; the no-op commit rule produces the required behavior as long as 0019 performs
  the removal **inside** the editing session:
  - *New text box:* 0019 calls `beginEdit()`, `add(textShape)`, `select(id:)`, `replace(...)` while
    typing; on Esc / click-outside with empty text it calls `delete(id:)` and then `commitEdit()`.
    The shapes equal the session start, so **no undo step is pushed, the redo stack survives, and
    `isDirty` is unchanged**. Placing and abandoning an empty text box leaves no trace in history.
  - *Re-editing existing text to empty:* same sequence starting with `beginEdit()` on the existing
    shape; the commit removes the shape, which is a real change → exactly **one** undo step whose
    undo restores the original text shape at its original z-index.
  - *Normal text session:* all keystrokes between `beginEdit()` and `commitEdit()` are one undo
    step. (Per-keystroke undo inside the live text field is the field editor's own business, 0019.)

- **Tests — new file `SnapperTests/AnnotationDocumentTests.swift`** (see Test plan). Use the test
  framework set up by 0001. Build shape fixtures through 0015's public `AnnotationShape`
  constructors (a small private helper such as `makeRect(_:)` / `makeText(_:)` in the test file is
  fine).

- **Project:** both files live in folders already covered by `project.yml` sources (0001), so no
  `project.yml` edit is expected; run `xcodegen generate` so the new files are in the generated
  project, then build and test.

- **No PRD change.** All decisions above are implementation choices within PRD §8.1–8.4.

## Acceptance criteria

- [x] `Snapper/Annotate/Model/AnnotationDocument.swift` exists, imports only Foundation/CoreGraphics
      (no AppKit, no `UndoManager`), and exposes the API above.
- [x] Every edit kind (add, delete, move/resize/recolor/restroke/text change via `replace`) is
      undoable and redoable, matching PRD §8.2 "covers every edit".
- [x] A coalesced session with many intermediate `replace` calls produces exactly one undo step.
- [x] A commit with no net shape change pushes nothing and does not clear redo.
- [x] `cancelEdit()` restores shapes and selection exactly and pushes nothing.
- [x] Any committed edit clears the redo stack.
- [x] Selection changes alone are never undo steps and never make the document dirty; undo/redo
      restore the selection captured with each step.
- [x] Undoing a delete restores the shape at its original z-index.
- [x] `isDirty` is false for a new document, true after an edit, false after `markClean()`, false
      again after undoing back to the clean revision, and true after a different edit following such
      an undo.
- [x] Placing and abandoning an empty new text box (sequence above) leaves no undo step and no dirty
      state.
- [x] `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and
      `xcodebuild -scheme Snapper test` all succeed.

## Edge cases

- `undo()` / `redo()` with empty stacks → no-op (no crash, no state change).
- `undo()` / `redo()` while a session is open → no-op (`canUndo`/`canRedo` are `false`).
- `commitEdit()` / `cancelEdit()` without a session → no-op.
- Nested `beginEdit()` → `assertionFailure` in debug; release keeps the original session start.
- `delete(id:)` / `replace(_:)` with unknown id → no-op, no undo step, not dirty.
- `add(_:)` with a duplicate id → `assertionFailure`, ignored.
- Deleting the selected shape clears the selection; deleting an unselected shape leaves the
  selection alone.
- A restored state whose selected id no longer exists → selection `nil`.
- A session that adds then deletes the same shape → no-op commit.
- A session that only changes the selection → no-op commit (selection stays as changed; nothing
  pushed).
- Undo to clean, then redo → dirty again (revision differs from clean).
- `markClean()` after undoing (e.g. save in the middle of history) → that earlier revision becomes
  the clean one; redoing makes it dirty, undoing back to it makes it clean.
- Overlapping shapes at the hit point → the last in `shapes` (topmost, newest unless reordered)
  wins; a point outside every shape (beyond tolerance) → `nil`.

## Test plan

- **Unit tests** (`SnapperTests/AnnotationDocumentTests.swift`), using a document built with
  `imagePixelSize: 2000×1200, pixelsPerPoint: 2`:
  1. `testNewDocumentIsEmptyCleanAndHasNoHistory` — `shapes` empty, `selectedShapeID == nil`,
     `canUndo == false`, `canRedo == false`, `isDirty == false`, `isEditing == false`;
     `imagePixelSize` / `pixelsPerPoint` echo the init values.
  2. `testAddAppendsOnTopAndIsOneUndoStep` — add A, add B → `shapes.map(\.id) == [A, B]`;
     `canUndo`; undo → `[A]`; undo → `[]`; `canUndo == false`.
  3. `testDeleteRemovesShapeAndUndoRestoresAtSameIndex` — add A, B, C; delete B →
     `[A, C]`; undo → `[A, B, C]` (B back at index 1, value equal to original).
  4. `testDeleteOfSelectedShapeClearsSelectionAndUndoReselects` — add A, B; select B; delete B →
     `selectedShapeID == nil`; undo → B restored and `selectedShapeID == B`.
  5. `testDeleteOfUnselectedShapeKeepsSelection` — select A, delete B → selection still A.
  6. `testReplaceKeepsZOrderAndIsUndoable` — add A, B; replace A with a moved/recolored copy →
     index 0 holds the new value; undo → original A value; redo → new value.
  7. `testReplaceWithIdenticalValuePushesNothing` — after one add, replace A with A → undo stack
     depth unchanged (one undo empties the document), `isDirty` unchanged.
  8. `testUnknownIDDeleteAndReplaceAreNoOps` — no change to shapes, `canUndo`, or `isDirty`.
  9. `testUndoRedoSequence` — add A, add B, replace B→B′; undo ×3 → `[]`; redo ×3 → `[A, B′]`;
     `canRedo == false` at the end.
  10. `testNewEditClearsRedo` — add A, add B; undo; add C → `canRedo == false`, `shapes == [A, C]`.
  11. `testCoalescedDragIsOneStep` — add A; `beginEdit()`; `replace` A 20 times with successive
      positions; `commitEdit()` → final position kept; one `undo()` returns A to its original
      position; `canUndo` is then true only for the add (one more undo empties).
  12. `testCreateDragSessionIsOneStep` — `beginEdit()`, `add(A)`, several `replace(A…)`,
      `select(A)`, `commitEdit()` → one undo removes A and restores the previous selection (`nil`).
  13. `testNoOpCommitPushesNothingAndKeepsRedo` — add A, add B, undo (redo available);
      `beginEdit()`; replace A with a moved copy then back to the original; `commitEdit()` →
      `canRedo` still true, undo depth unchanged, `isDirty` unchanged.
  14. `testCancelEditRestoresShapesAndSelection` — add A, select A; `beginEdit()`; replace A moved,
      add B, select B; `cancelEdit()` → `shapes == [A original]`, `selectedShapeID == A`,
      `isEditing == false`, undo depth unchanged.
  15. `testUndoRedoUnavailableWhileEditing` — with history present, `beginEdit()` →
      `canUndo == false`, `canRedo == false`; `undo()` changes nothing; after `commitEdit()` they
      return to normal.
  16. `testCommitAndCancelWithoutSessionAreNoOps`.
  17. `testSelectionIsNotUndoableAndNotDirty` — add A, B; `markClean()`; select A, select B,
      select nil → `isDirty == false`, undo depth unchanged; `select(id: unknownID)` → `nil`.
  18. `testRedoRestoresSelectionAtCommit` — session: add A + select A, commit; undo → selection nil;
      redo → A present and selected.
  19. `testTopmostShapeWinsOnOverlap` — add two overlapping rectangles A then B; point in the
      overlap → B; point only inside A's outline → A; point far from both → `nil`; after deleting B,
      the overlap point → A. (Use points on the rectangle outlines, or whatever 0015's hit-test
      treats as a hit for outline-only rectangles, with an explicit tolerance.)
  20. `testDirtyLifecycle` — new → clean; add A → dirty; `markClean()` → clean; replace A → dirty;
      undo → clean (back to the marked revision); redo → dirty; undo → clean.
  21. `testEditAfterUndoToCleanIsDirty` — add A; `markClean()`; replace A→A′; undo (clean); replace
      A→A″ → dirty (revision differs even though history depth equals the clean one).
  22. `testMarkCleanMidHistory` — add A, add B; undo; `markClean()` → clean; redo → dirty; undo →
      clean.
  23. `testDirtyWhileEditingWithUncommittedChange` — clean doc with A; `beginEdit()`; replace A
      moved → `isDirty == true`; `cancelEdit()` → `isDirty == false`.
  24. `testAbandonedNewEmptyTextBoxLeavesNoTrace` — add A; undo (so redo exists); `markClean()`;
      `beginEdit()`, add empty text T, select T, replace T (still empty), delete T, `commitEdit()` →
      `canRedo` still true, undo depth unchanged, `isDirty == false`, `selectedShapeID == nil`.
  25. `testReEditingTextToEmptyIsOneUndoStep` — add text T ("Hi") then rectangle R; session:
      replace T with "", delete T, commit → `shapes == [R]`; one undo → `[T("Hi"), R]` with T at
      index 0.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build`
  and `xcodebuild -scheme Snapper test` succeed with all existing tests still green.
- **Manual verification:** N/A for this spec. It adds only a pure model with no UI, hotkey,
  permission, capture, or window code, and nothing in the running app calls it yet; every behavior
  is covered by the unit tests above. The user-visible undo/redo, Delete, recolor, and "Discard
  changes?" checks are listed in 0017–0020, which wire this model into the annotation window. Just
  confirm the app still launches to the menu bar as before (no regression).

## Out of scope

- Shape geometry, hit-testing per shape, handles, move/resize math, Shift constraints →
  0015-annotation-shape-model.
- The annotation window, canvas, `CanvasTransform`, `ShapeRenderer`, rectangle/arrow tools, and
  when drag sessions begin/commit → 0017-annotation-window-canvas.
- Selection/move/resize UI, ⌫, Esc-deselect, color/stroke controls applying to the selection,
  remembered color/width, and bridging ⌘Z / ⇧⌘Z menu items to `undo()`/`redo()` with
  `canUndo`/`canRedo` validation → 0018-annotation-editing.
- Text placement/editing, field-editor undo while typing, and performing the empty-text removal
  inside the session → 0019-annotation-text-tool.
- Export, ⌘S / ⌘C, calling `markClean()` after a successful save or copy, the "Discard changes?"
  prompt on close, and the quit prompt using `isDirty` → 0020-annotation-export.
- Persisting history across app launches (not required by the PRD).

## Verification status

- Verified on **2026-11-03**: `xcodegen generate` succeeded.
- `xcodebuild -scheme Snapper -configuration Debug build`: **BUILD SUCCEEDED**.
- `xcodebuild -scheme Snapper test`: **TEST SUCCEEDED** — **303 tests in 45 suites passed**,
  including **28 new AnnotationDocumentTests** covering atomic edits, gesture coalescing,
  selection snapshots, z-order, delegated hit-testing, no-op/cancel behavior, unique clean
  revisions, empty-text removal, text-session coalescing and independent value copies.
- No PRD or AGENTS.md change was needed; this pure model implements the existing behavior and
  architecture. Window, hotkey and export integration remains in 0017–0020.
- No GUI paths were manually verified.
- [ ] not yet run by user — Confirm Snapper still launches to the menu bar as before.
