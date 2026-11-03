# 0039 — Port the annotation document with coalesced undo/redo and dirty tracking to Windows

> Source: docs/PRD.md §8.1 ("changes that have not been saved or copied", line 128; quit with N
> unsaved annotations, line 129), §8.2 (Undo/Redo row "covers every edit, including moves, resizes,
> color changes, and text edits", line 143; Delete; recolor/restroke applies to the selection),
> §8.3 (empty text boxes are removed on commit, line 162), §8.4 (shapes in image coordinates),
> §12 (lines 223, 231), §13.2 (Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z; points → DIPs; backing scale →
> monitor scale factor), §13.7 (section 8 applies on Windows).
> macOS reference: docs/specs/done/0016-annotation-document-undo_COMPLETED.md (commit `b7ed6f2`).
> Depends on: 0038-windows-annotation-shape-model (and, through it, 0027 for `RgbaColor` /
> `StrokeWidth` and 0028 for the `Snapper.Core.Geometry` primitives).

## Context

The macOS app has a pure, unit-tested annotation document:
`Snapper/Annotate/Model/AnnotationDocument.swift:1-143` (struct `AnnotationDocument`), tested by
`SnapperTests/AnnotationDocumentTests.swift:1-329` (28 tests). It owns one annotation session: the
z-ordered shape list, the selection, a snapshot-based undo/redo history with gesture coalescing
(`beginEdit`/`commitEdit`/`cancelEdit`), and dirty tracking through unique revisions. No other
commit changed those two files after 0016. The macOS window code drives it like this
(`Snapper/Annotate/AnnotationCanvasView.swift:5,30,131,136`): one document per window, recreated
from the captured image, mutated by the canvas, and re-rendered after each mutation.

On Windows, `windows/` is created by 0025. Spec 0038 ports the pure shape model into
`windows/src/Snapper.Core/Annotate/Model/` (`AnnotationShape` with a stable `Guid Id`, value
equality, `HitTest`, move/resize/`With…` helpers, `TextBox`, `ShapeHandle`, …). Nothing on Windows
owns a whole session yet; that is this spec. The annotation window (0040), editing (0041), text
(0042), and export/close prompts (0043) all build on it.

**What 0019 (macOS text tool) added on top of the document, and where it goes.** 0019 did not
modify `AnnotationDocument.swift`. It added `TextEditDraft`, `TextCommitDecision`, and an
`extension AnnotationDocument { mutating func apply(_ decision: TextCommitDecision) }`
(`Snapper/Annotate/Model/TextCommit.swift:29-38`), tested by
`SnapperTests/TextEditingUndoTests.swift:1-51`. That extension depends on the text-commit types,
which the ownership table assigns to **0042**, so the `Apply(TextCommitDecision)` extension method
and its tests are **left to 0042**. Everything 0042 needs from the document itself (sessions,
no-op commits, `Delete` inside a session, `Replace` outside one, `MarkClean`) is a pure document
feature and is delivered and tested here, including the 0016 tests that pin the text-session
contract (`testAbandonedNewEmptyTextBoxLeavesNoTrace`, `testReEditingTextToEmptyIsOneUndoStep`,
`testTextKeystrokesAreOneEdit`).

macOS facts that don't apply on Windows: there is no `UndoManager`/Edit menu bridge (the window in
0041 maps Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z directly); "points" and "Retina 2×" become DIPs and the
monitor scale factor (PRD §13.2). There is no Y flip anywhere in this spec: image pixel coordinates
are top-left, y-down, on both platforms.

## User story

As a Snapper user annotating a capture on Windows, every edit I make (drawing, moving, resizing,
recoloring, changing stroke width, editing text, deleting) is one step I can undo with Ctrl+Z and
redo with Ctrl+Y or Ctrl+Shift+Z, a whole drag counts as a single step, and Snapper knows exactly
whether my annotation has changes I haven't saved or copied, including when I undo back to the
state I last saved. (The visible wiring lands in 0040–0043; this spec is the pure foundation.)

## Design decisions

These port 0016's "Design decisions" (docs/specs/done/0016-annotation-document-undo_COMPLETED.md,
items 1–8) unchanged in behavior. Only the C#-specific adaptations below differ, and they are
fixed for the implementer.

1. **Reference type with explicit `Clone()`, not a mutable struct.** Swift's `struct` with
   copy-on-write gives value semantics for free. A C# mutable `struct` would silently mutate
   copies (for example `window.Document.Add(x)` through a property getter), and a struct holding
   `List<T>` fields would share history between copies. So `AnnotationDocument` is a
   `public sealed class`. Snapshots are immutable (`ImmutableArray<AnnotationShape>` from
   `System.Collections.Immutable`, which is part of the .NET BCL, so no NuGet package), so storing
   one in the undo stack is free and can never be changed from outside. The macOS value-copy test
   (`AnnotationDocumentTests.swift:317-328`) is ported as a `Clone()` test: a clone has its own
   independent shapes, selection, history, session, and clean marker.
2. **Change notification.** macOS re-renders through `didSet` on the struct property
   (`AnnotationCanvasView.swift:5`). A class has no `didSet`, so the document raises
   `public event EventHandler? Changed` once at the end of every public mutating call that changed
   any observable value (`Shapes`, `SelectedShapeId`, `IsEditing`, `CanUndo`, `CanRedo`,
   `IsDirty`). Calls that change nothing (no-ops, rejected programmer errors) raise nothing.
   Implement it by comparing a small "observable snapshot" before and after the call; don't
   sprinkle raises through branches. The event is raised synchronously on the caller's thread.
3. **Programmer errors throw instead of asserting.** Swift uses `assert`/`assertionFailure`
   (crash in debug, ignored in release). In .NET, a failed `Debug.Assert` without a debugger calls
   `Environment.FailFast` and kills the xUnit host, so those paths would be untestable. On Windows
   these cases throw `InvalidOperationException` **before changing any state** (and raise no
   `Changed`): `Add` of a shape whose `Id` already exists, `BeginEdit` while a session is open,
   and `MarkClean` while a session is open. The constructor throws `ArgumentOutOfRangeException`
   for a non-positive image width/height or scale. Callers in 0040–0043 never hit these by design
   (0043 commits any open text edit before calling `MarkClean`, as on macOS).
4. **Scale name.** macOS `pixelsPerPoint` (2 on Retina) becomes `PixelsPerDip`: image pixels per
   DIP, which is the capture monitor's scale factor (1.0 at 100 %, 1.5 at 150 %, 2.0 at 200 %;
   PRD §13.2). It's passed straight through to 0038's `AnnotationShape.HitTest` scale parameter.
   If 0038 has already named this same quantity differently in its public API (for example
   `pixelsPerPoint`), use 0038's name here as well, so there is one name across `Annotate/Model`.
5. **Not thread-safe, no UI types.** The document is plain `Snapper.Core` code with no WPF, WinRT,
   or interop references. It's used only from the WPF dispatcher thread (AGENTS.md "Threading").
   It holds no image, only the image's pixel size and scale (macOS decision 2).

Everything else is identical to macOS: snapshot-based undo (decision 3), unbounded history
(decision 4), gesture coalescing with the no-op-commit and cancel rules (decision 5), selection
captured with each step but never undoable on its own (decision 6), unique-revision dirty tracking
(decision 7), and no framework undo manager (decision 8).

## Scope & changes

- **Pure (Snapper.Core) — new file
  `windows/src/Snapper.Core/Annotate/Model/AnnotationDocument.cs`**, namespace
  `Snapper.Core.Annotate.Model`. Port of `Snapper/Annotate/Model/AnnotationDocument.swift:1-143`.
  Public surface (exact names; the 0040–0043 specs rely on them):

  ```csharp
  public sealed class AnnotationDocument
  {
      // Undo/redo entry and live state. Equality of Shapes is element-wise (see below).
      public readonly record struct State(
          ImmutableArray<AnnotationShape> Shapes, Guid? SelectedShapeId, int Revision);

      public AnnotationDocument(PixelSize imagePixelSize, double pixelsPerDip);

      public PixelSize ImagePixelSize { get; }
      public double PixelsPerDip { get; }

      public event EventHandler? Changed;

      // Read access. Order is back to front: the last shape is topmost.
      public ImmutableArray<AnnotationShape> Shapes { get; }
      public Guid? SelectedShapeId { get; }
      public AnnotationShape? SelectedShape { get; }
      public AnnotationShape? ShapeWithId(Guid id);
      public AnnotationShape? TopmostShapeAt(PointD point, double tolerance); // image pixels

      // Edits: an atomic undo step outside a session, coalesced inside one.
      public void Add(AnnotationShape shape);
      public void Delete(Guid id);
      public void Replace(AnnotationShape shape);

      // Selection: never an undo step on its own, never dirties.
      public void Select(Guid? id);

      // Gesture coalescing.
      public bool IsEditing { get; }
      public void BeginEdit();
      public void CommitEdit();
      public void CancelEdit();

      // History.
      public bool CanUndo { get; }
      public bool CanRedo { get; }
      public void Undo();
      public void Redo();

      // Unsaved-changes tracking.
      public bool IsDirty { get; }
      public void MarkClean();

      // Independent deep copy of the whole session (C# replacement for Swift value copies).
      public AnnotationDocument Clone();
  }
  ```

  `PixelSize` and `PointD` are 0028's `Snapper.Core.Geometry` primitives (`PixelSize` is integer
  width/height, matching the captured bitmap). Private storage mirrors macOS
  (`AnnotationDocument.swift:16-21`): `State _current` (initially empty shapes, no selection,
  revision 0), `List<State> _undo`, `List<State> _redo` (or `Stack<State>`), `State? _sessionStart`,
  `int _nextRevision = 1`, `int _cleanRevision = 0`. Member behavior, each a line-by-line port:

  - Constructor (`:23-28`): stores the size and scale; throws `ArgumentOutOfRangeException` if
    `Width <= 0`, `Height <= 0`, or `pixelsPerDip` is not a finite number > 0 (decision 3).
  - `Shapes`, `SelectedShapeId`, `SelectedShape`, `ShapeWithId` (`:30-40`): lookups on `_current`.
  - `TopmostShapeAt` (`:42-45`): iterate `Shapes` from last to first and return the first shape
    whose 0038 `HitTest(point, PixelsPerDip, slop: tolerance)` is true; `null` if none. Don't
    re-implement per-shape geometry.
  - `Add` (`:47-53`): throws `InvalidOperationException` if a shape with that `Id` exists
    (decision 3); otherwise appends on top via `Edit`. Doesn't change the selection.
  - `Delete` (`:55-61`): unknown id → no-op. Otherwise removes it via `Edit`, and clears the
    selection if that shape was selected.
  - `Replace` (`:63-66`): unknown id → no-op. Otherwise replaces at the same index via `Edit`
    (z-order unchanged). An identical value produces no undo step (no-op commit rule).
  - `Select` (`:68-71`): sets the selection, then enforces the invariant (an id not in `Shapes`
    becomes `null`). Never touches the stacks, the revision, or `IsDirty`.
  - `IsEditing` (`:73`): `_sessionStart != null`.
  - `BeginEdit` (`:75-81`): throws `InvalidOperationException` if a session is open (decision 3);
    otherwise `_sessionStart = _current`.
  - `CommitEdit` (`:83-91`): no session → no-op. Otherwise close the session; if `_current.Shapes`
    equal the start's shapes (element-wise), push nothing, keep the revision, keep redo. Else push
    the start state to undo, set `_current.Revision = _nextRevision++`, clear redo.
  - `CancelEdit` (`:93-97`): no session → no-op. Otherwise close it and restore the start state
    (shapes, selection, revision) exactly; push nothing.
  - `CanUndo` / `CanRedo` (`:99-100`): `false` while editing, else the stack is non-empty.
  - `Undo` / `Redo` (`:102-112`): no-op unless `CanUndo`/`CanRedo`. Push `_current` to the other
    stack, pop and restore (shapes, selection, revision), enforce the selection invariant.
  - `IsDirty` (`:114-117`): `_current.Revision != _cleanRevision`, or a session is open and the
    live shapes differ from the session start's shapes.
  - `MarkClean` (`:119-123`): throws `InvalidOperationException` while editing (decision 3);
    otherwise `_cleanRevision = _current.Revision`. Called by 0043/0044 only after a successful save
    or copy.
  - Private `Edit(Action<…> mutation)` (`:125-131`): if no session is open, wrap the mutation in
    `BeginEdit` + `CommitEdit` (atomic step); inside a session just mutate. Private `Restore` and
    `EnforceSelectionInvariant` (`:133-142`) as on macOS.
  - `Clone()`: returns a new document with the same size, scale, `_current`, copies of both stacks,
    `_sessionStart`, `_nextRevision`, and `_cleanRevision`, and **no** `Changed` subscribers.
    Because `State` and its `ImmutableArray` are immutable, copying the stack lists is a full deep
    copy.
  - `Changed`: decision 2. Compare (`Shapes` element-wise, `SelectedShapeId`, `IsEditing`,
    `CanUndo`, `CanRedo`, `IsDirty`) before and after each public mutating call, and raise once if
    any differ. (Note: `BeginEdit` changes `IsEditing` and usually `CanUndo`, so it does raise.)
  - **Shape equality.** Compare shape arrays with `ImmutableArray.SequenceEqual` (via a private
    `SameShapes(a, b)` helper), relying on 0038's `AnnotationShape` value equality (the port of
    Swift `Equatable`, including the text string, measured size, color, and stroke width). Don't
    use `State`'s compiler-generated `Equals` for shapes, because `ImmutableArray<T>` equality is
    by reference. If 0038's `AnnotationShape` lacks value equality, that's a defect in 0038 to fix
    there, not something to work around here.
  - **Invariants** after every public call (same as 0016): shape ids are unique;
    `SelectedShapeId` is `null` or the id of a shape in `Shapes`; a successful commit clears redo;
    a no-op commit leaves both stacks untouched.

- **Empty-text contract for 0042 (enforced by tests here, no special code).** As 0016 describes
  ("Empty text boxes" bullet): a new text box is `BeginEdit`, `Add`, `Select`, `Replace`… and on
  commit with empty text `Delete` + `CommitEdit` inside the same session, which is a no-op commit
  (no undo step, redo survives, not dirty). Re-editing existing text to empty is exactly one undo
  step. All keystrokes of a session are one undo step. 0042 adds `Apply(TextCommitDecision)` as an
  extension method in its own file; it's **not** part of this spec.

- **Tests — new file `windows/tests/Snapper.Core.Tests/AnnotationDocumentTests.cs`** (xUnit, see
  Test plan). Fixture helpers ported from `AnnotationDocumentTests.swift:7-20`: `NewDocument()` =
  `new AnnotationDocument(new PixelSize(2000, 1200), 2.0)`; `Rect(double x = 100)` = a rectangle
  shape `RectD(x, 100, 200, 100)`, red, `StrokeWidth.M`; `Text(string s = "")` = 0038's
  `MakeText(origin (10, 20), red, M).WithText(s).WithMeasuredSize(80 × 40)`;
  `Moved(shape, double d = 20)` = 0038's move by `(d, d)`. Use 0038's exact C# names for these
  shape constructors and helpers (the ports of `makeText`, `withText`, `withMeasuredSize`,
  `moved(by:)`, `withColor`, `withStrokeWidth`, `resized(handle:to:constrained:)`).

- **No project changes.** Both files are in folders the SDK-style projects from 0025 already glob.
  No new NuGet packages (`System.Collections.Immutable` ships with .NET 10). No `NativeMethods.txt`
  changes.

- **No PRD or AGENTS.md change.** AGENTS.md "Architecture rules (Windows)" already lists "the
  annotation model and undo" as pure, tested Core logic.

## Acceptance criteria

- [ ] `windows/src/Snapper.Core/Annotate/Model/AnnotationDocument.cs` exists with the public
      surface above and no WPF/WinForms/WinRT/interop references.
- [ ] Every edit kind (add, delete, and move/resize/recolor/restroke/text change via `Replace`) is
      undoable and redoable (PRD §8.2 "covers every edit").
- [ ] A session with many intermediate `Replace` calls is exactly one undo step. A commit with no
      net shape change pushes nothing and keeps redo. `CancelEdit` restores shapes and selection
      exactly and pushes nothing. Any real commit clears redo.
- [ ] Selection changes alone are never undo steps and never dirty the document. Undo/redo restore
      the selection captured with each step. Undoing a delete restores the shape at its original
      z-index.
- [ ] `IsDirty` is false for a new document, true after an edit, false after `MarkClean`, false
      again after undoing back to the clean revision, and true after a different edit following
      such an undo (unique revisions).
- [ ] Placing and abandoning an empty new text box leaves no undo step and no dirty state.
- [ ] `Clone()` produces a fully independent document; `Changed` fires exactly once per
      state-changing call and never for no-ops; programmer errors throw without changing state.
- [ ] All 28 ported macOS cases plus the Windows-specific cases below pass, and
      `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` are green
      with all existing tests still passing.

## Edge cases

Same as 0016 "Edge cases", with the Windows adaptations:

- `Undo`/`Redo` with empty stacks, or while a session is open → no-op, no `Changed`.
- `CommitEdit`/`CancelEdit` without a session → no-op, no `Changed`.
- Nested `BeginEdit` → `InvalidOperationException`; the original session start is kept.
- `Delete`/`Replace` with an unknown id → no-op, no undo step, not dirty, no `Changed`.
- `Add` with a duplicate id → `InvalidOperationException`, nothing changes.
- `MarkClean` while editing → `InvalidOperationException`, the clean marker is unchanged.
- Deleting the selected shape clears the selection; deleting another shape keeps it.
- A restored state whose selected id no longer exists → selection `null`.
- A session that adds then deletes the same shape, or only changes the selection → no-op commit
  (the selection stays as changed, nothing is pushed, redo survives).
- Undo to clean, then redo → dirty. `MarkClean` mid-history makes that earlier revision the clean
  one.
- Overlapping shapes → the last in `Shapes` wins; a point beyond every shape's tolerance → `null`.
- `Changed` subscribers that read the document during the event see the final state of the call.
- Scale: `PixelsPerDip` of 1.0, 1.5, and 2.0 are all valid; non-integer scales (150 %) must work
  in `TopmostShapeAt` (it only forwards the value to 0038's hit test).
- `PixelsPerDip` `0`, negative, `NaN`, or `Infinity`, or a 0-pixel image dimension → constructor
  throws.

## Test plan

- **Extra port (left here by 0038 because it needs `AnnotationDocument`):** `ShapeDragSessionTests.previewsCommitOneHistoryStep` (`SnapperTests/ShapeDragSessionTests.swift:54-68`). Put it in `windows/tests/Snapper.Core.Tests/ShapeDragSessionTests.cs` as `PreviewsCommitOneHistoryStep`: a drag previews without touching the document, and committing creates exactly one undo step.


- **Unit tests** (`windows/tests/Snapper.Core.Tests/AnnotationDocumentTests.cs`, xUnit `[Fact]`,
  one test per macOS test, same name without the `test` prefix, same steps and assertions as the
  cited Swift lines; `#expect(a == b)` becomes `Assert.Equal`/`Assert.True`, and shape lists are
  compared with `Assert.Equal(expected, d.Shapes)` (element-wise)):
  1. `NewDocumentIsEmptyCleanAndHasNoHistory` (`AnnotationDocumentTests.swift:22-29`) — empty, no
     selection, `!CanUndo && !CanRedo && !IsDirty && !IsEditing`, size 2000×1200 and scale 2 echoed.
  2. `AddAppendsOnTopAndIsOneUndoStep` (`:31-40`).
  3. `DeleteRemovesShapeAndUndoRestoresAtSameIndex` (`:42-49`) — `[a, c]`, undo `[a, b, c]`, redo
     `[a, c]`.
  4. `DeleteOfSelectedShapeClearsSelectionAndUndoReselects` (`:51-59`).
  5. `DeleteOfUnselectedShapeKeepsSelection` (`:61-66`).
  6. `ReplaceKeepsZOrderAndIsUndoable` (`:68-85`) — for each of moved, `WithColor(blue)`,
     `WithStrokeWidth(L)`, and resize of the bottom-right handle to `(400, 250)` unconstrained:
     `[change, b]`, undo `[a, b]`, redo `[change, b]`; then a text shape edited to "Edited" with
     measured size 100×40 undoes and redoes.
  7. `ReplaceWithIdenticalValuePushesNothing` (`:87-93`).
  8. `UnknownIdDeleteAndReplaceAreNoOps` (`:95-105`).
  9. `UndoRedoSequence` (`:107-117`).
  10. `NewEditClearsRedo` (`:119-125`).
  11. `CoalescedDragIsOneStep` (`:127-137`) — 20 replaces in one session, one undo returns to `a`.
  12. `CreateDragSessionIsOneStep` (`:139-150`) — undo restores `[prior]` with `prior` selected;
      redo restores `[prior, moved(a, 5)]` with `a` selected.
  13. `NoOpCommitPushesNothingAndKeepsRedo` (`:152-160`).
  14. `CancelEditRestoresShapesAndSelection` (`:162-170`).
  15. `UndoRedoUnavailableWhileEditing` (`:172-180`).
  16. `CommitAndCancelWithoutSessionAreNoOps` (`:182-189`).
  17. `SelectionIsNotUndoableAndNotDirty` (`:191-201`) — selecting an unknown `Guid.NewGuid()`
      yields `null`.
  18. `RedoRestoresSelectionAtCommit` (`:203-209`).
  19. `TopmostShapeWinsOnOverlap` (`:211-231`) — same points and tolerances: `(250,100)` tol 0 → b;
      `(150,100)` tol 0 → a; `(50,50)` → null; `(250,108)` tol 4 → b; `(250,109)` tol 4 → null;
      after deleting b, `(250,100)` → a; text "Hi" hit at `(30,30)`; arrow `(0,0)→(100,0)` blue M
      hit at `(65,10)`. `ShapeWithId(a.Id) == a`, unknown id → null. (These values assume 0038's
      exact port of the 0015 hit test with scale 2.)
  20. `DirtyLifecycle` (`:233-243`).
  21. `EditAfterUndoToCleanIsDirty` (`:245-254`) — including the regression branch (mark clean,
      undo, different edit at the same depth → dirty, no redo).
  22. `MarkCleanMidHistory` (`:256-265`).
  23. `DirtyWhileEditingWithUncommittedChange` (`:267-275`).
  24. `AbandonedNewEmptyTextBoxLeavesNoTrace` (`:277-285`).
  25. `ReEditingTextToEmptyIsOneUndoStep` (`:287-295`).
  26. `TextKeystrokesAreOneEdit` (`:297-306`) — replaces with "H", "Hello", "Hello\nworld" in one
      session → one undo step back to "Hi" and clean.
  27. `SelectionOnlySessionKeepsSelectionAndRedo` (`:308-315`).
  28. `CloneHasIndependentHistoryAndCleanMarker` (port of
      `testDocumentCopiesHaveIndependentHistoryAndCleanMarkers`, `:317-328`) — `copy =
      original.Clone()` instead of `var copy = original`; same assertions.

  Windows-specific cases (decisions 1–4):
  29. `ConstructorRejectsInvalidSizeOrScale` — `Assert.Throws<ArgumentOutOfRangeException>` for
      width 0, height 0, scale 0, −1, `double.NaN`, and `double.PositiveInfinity`; scales 1.0, 1.5,
      and 2.0 construct and echo `PixelsPerDip`.
  30. `AddDuplicateIdThrowsAndChangesNothing` — add a; `Add(a)` again throws
      `InvalidOperationException`; `Shapes == [a]`, one undo empties the document, no `Changed`
      raised by the failed call.
  31. `NestedBeginEditThrowsAndKeepsOriginalSession` — add a; `BeginEdit`; replace a moved;
      second `BeginEdit` throws; `CancelEdit` → `Shapes == [a]` (the original start was kept).
  32. `MarkCleanWhileEditingThrows` — add a; `BeginEdit`; `MarkClean` throws; `CommitEdit`;
      `IsDirty` still true (marker unchanged).
  33. `ChangedFiresOncePerStateChangingCall` — subscribe a counter: `Add` → +1; `Select(a)` → +1;
      `Select(a)` again → +0; `Replace(a)` (identical) → +0; `Delete(unknown)` → +0; `Undo` → +1;
      `Undo` with empty stack → +0; `Redo` → +1 (a is back); `BeginEdit` → +1; three `Replace`
      calls with `Moved(a, 1)`, `Moved(a, 2)`, `Moved(a, 3)` inside the session → +1 each (live
      shapes change); `CommitEdit` → +1; `CommitEdit` without session → +0;
      `MarkClean` on a dirty document → +1; `MarkClean` again → +0.
  34. `ChangedHandlerSeesFinalState` — in the handler for `Delete` of the selected shape, the
      shape is gone, `SelectedShapeId` is null, and `CanUndo` is true.
  35. `CloneDoesNotCopySubscribersOrShareSession` — clone a document with an open session and a
      `Changed` subscriber; `CommitEdit` on the clone doesn't raise the original's handler and
      leaves `original.IsEditing == true`.
  36. `ShapesSnapshotIsImmutableHistory` — read `var before = d.Shapes`, then `Replace` and `Add`;
      `before` still holds the old values and `Undo` restores exactly `before`.
  37. `TopmostShapeAtForwardsFractionalScale` — document with `PixelsPerDip = 1.5`: the topmost
      hit for a rectangle outline point matches 0038's `HitTest(point, 1.5, tol)` directly (and a
      point just past the scaled half-stroke + tolerance returns null). Compute the boundary from
      0038's stroke-pixel helper rather than hard-coding it.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test
  windows/Snapper.slnx` succeed, with all existing tests still green.
- **Manual verification:** none needed for this spec. It adds only pure Core logic. No window,
  hotkey, tray, capture, or interop code calls it yet (0040 is the first caller), and every
  behavior is covered by the unit tests above. On a Windows 11 machine, just confirm there's no
  regression:
  1. Launch the Debug build of `Snapper.Windows`. The tray icon appears and its menu opens as
     before (0025/0029 behavior). Ctrl+Shift+4 still takes a screenshot.

## Intermediate state

Until 0040 lands, nothing in the running app uses `AnnotationDocument`. Ctrl+Shift+6 behaves as
whatever earlier specs left it (0025's stub action). This spec changes no runtime behavior.

## Out of scope

- Shape geometry, per-shape hit-testing, handles, move/resize math, Shift constraints,
  `RgbaColor`/`StrokeWidth` → 0038 (and 0027).
- The annotation window, canvas, renderer, drawing tools, and when drag sessions begin and commit
  → 0040.
- Selection/move/resize UI, Delete/Backspace, Esc deselect, recolor/restroke of the selection, and
  mapping Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z to `Undo`/`Redo` with `CanUndo`/`CanRedo` → 0041.
- Text placement and editing, `TextEditDraft`, `TextCommitDecision`, the
  `AnnotationDocument.Apply(TextCommitDecision)` extension method, and the port of
  `SnapperTests/TextEditingUndoTests.swift` → 0042.
- Export, Ctrl+S / Ctrl+C, calling `MarkClean` after a successful save or copy, the "Discard
  changes?" prompt, the quit confirmation, and the "Unsaved annotations" shutdown block reason
  driven by `IsDirty` → 0043. Copy & Close → 0044.
- Persisting history across launches (not required by the PRD).

## Verification status

Automated verification on 2026-11-03:

- `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**.
- `dotnet test windows/Snapper.slnx` passed **1,015 tests**, with **0 failures and 0 skips**.
- Added **40 tests**: the 28 Swift document cases, nine Windows-specific cases, two additional
  clone/notification regressions, and the deferred drag-preview history test from 0038.
- `git diff --check` passed. No package, project, native, PRD, or AGENTS.md changes were needed.

The immutable snapshot document, coalesced undo/redo, unique-revision dirty tracking, independent
cloning, and final-state change notifications are covered by Core tests. This pure slice is not
wired into the app yet, so no feature-specific GUI verification is needed or claimed.

- [ ] not yet run by user (optional Windows 11 regression): Launch the Debug Windows app, confirm
  the tray menu opens, and take a screenshot with Ctrl+Shift+4.
