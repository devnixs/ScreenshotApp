using System.Collections.Immutable;
using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

/// <summary>An image-space annotation session. Callers serialize access on the UI dispatcher.
/// Immutable snapshots capture selection alongside edits, but selection alone is never an edit.</summary>
public sealed class AnnotationDocument
{
    public readonly record struct State(ImmutableArray<AnnotationShape> Shapes, Guid? SelectedShapeId, int Revision);

    private State current = new([], null, 0);
    private readonly List<State> undo = [], redo = [];
    private State? sessionStart;
    private int nextRevision = 1, cleanRevision;

    public AnnotationDocument(PixelSize imagePixelSize, double pixelsPerDip)
    {
        if (imagePixelSize.Width <= 0 || imagePixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(imagePixelSize));
        if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        ImagePixelSize = imagePixelSize;
        PixelsPerDip = pixelsPerDip;
    }

    public PixelSize ImagePixelSize { get; }
    public double PixelsPerDip { get; }
    public event EventHandler? Changed;
    public ImmutableArray<AnnotationShape> Shapes => current.Shapes;
    public Guid? SelectedShapeId => current.SelectedShapeId;
    public AnnotationShape? SelectedShape => SelectedShapeId is { } id ? ShapeWithId(id) : null;
    public AnnotationShape? ShapeWithId(Guid id) => Shapes.FirstOrDefault(shape => shape.Id == id);
    public AnnotationShape? TopmostShapeAt(PointD point, double tolerance)
    {
        for (var index = Shapes.Length - 1; index >= 0; index--)
            if (Shapes[index].HitTest(point, PixelsPerDip, tolerance)) return Shapes[index];
        return null;
    }

    public bool IsEditing => sessionStart is not null;
    public bool CanUndo => !IsEditing && undo.Count > 0;
    public bool CanRedo => !IsEditing && redo.Count > 0;
    public bool IsDirty => current.Revision != cleanRevision ||
        sessionStart is { } start && !SameShapes(Shapes, start.Shapes);

    public void Add(AnnotationShape shape)
    {
        if (ShapeWithId(shape.Id) is not null) throw new InvalidOperationException("Shape IDs must be unique.");
        Mutate(() => Edit(() => current = current with { Shapes = Shapes.Add(shape) }));
    }
    public void Delete(Guid id) => Mutate(() =>
    {
        var index = IndexOfId(id);
        if (index < 0) return;
        Edit(() => current = current with
        {
            Shapes = Shapes.RemoveAt(index),
            SelectedShapeId = SelectedShapeId == id ? null : SelectedShapeId
        });
    });
    public void Replace(AnnotationShape shape) => Mutate(() =>
    {
        var index = IndexOfId(shape.Id);
        if (index >= 0) Edit(() => current = current with { Shapes = Shapes.SetItem(index, shape) });
    });
    public void Select(Guid? id) => Mutate(() =>
    {
        current = current with { SelectedShapeId = id };
        EnforceSelectionInvariant();
    });
    public void BeginEdit()
    {
        if (IsEditing) throw new InvalidOperationException("An edit is already open.");
        Mutate(() => sessionStart = current);
    }
    public void CommitEdit() => Mutate(Commit);
    public void CancelEdit() => Mutate(() =>
    {
        if (sessionStart is not { } start) return;
        sessionStart = null;
        Restore(start);
    });
    public void Undo() => Mutate(() =>
    {
        if (!CanUndo) return;
        redo.Add(current);
        Restore(Pop(undo));
    });
    public void Redo() => Mutate(() =>
    {
        if (!CanRedo) return;
        undo.Add(current);
        Restore(Pop(redo));
    });
    public void MarkClean()
    {
        if (IsEditing) throw new InvalidOperationException("Commit the active edit before marking the document clean.");
        Mutate(() => cleanRevision = current.Revision);
    }

    /// <summary>History and active edits are independent; immutable shape snapshots may safely be shared.</summary>
    public AnnotationDocument Clone()
    {
        var copy = new AnnotationDocument(ImagePixelSize, PixelsPerDip)
        {
            current = current, sessionStart = sessionStart, nextRevision = nextRevision, cleanRevision = cleanRevision
        };
        copy.undo.AddRange(undo);
        copy.redo.AddRange(redo);
        return copy;
    }

    private void Edit(Action mutation)
    {
        var atomic = !IsEditing;
        if (atomic) sessionStart = current;
        mutation();
        if (atomic) Commit();
    }
    private void Commit()
    {
        if (sessionStart is not { } start) return;
        sessionStart = null;
        if (SameShapes(Shapes, start.Shapes)) return;
        undo.Add(start);
        current = current with { Revision = nextRevision++ };
        redo.Clear();
    }
    private void Restore(State state) { current = state; EnforceSelectionInvariant(); }
    private void EnforceSelectionInvariant()
    {
        if (SelectedShapeId is { } id && ShapeWithId(id) is null)
            current = current with { SelectedShapeId = null };
    }
    private int IndexOfId(Guid id)
    {
        for (var index = 0; index < Shapes.Length; index++)
            if (Shapes[index].Id == id) return index;
        return -1;
    }
    private static State Pop(List<State> stack)
    {
        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }
    private static bool SameShapes(ImmutableArray<AnnotationShape> a, ImmutableArray<AnnotationShape> b) => a.SequenceEqual(b);
    private readonly record struct Observable(ImmutableArray<AnnotationShape> Shapes, Guid? SelectedShapeId,
        bool IsEditing, bool CanUndo, bool CanRedo, bool IsDirty);
    private Observable Observe() => new(Shapes, SelectedShapeId, IsEditing, CanUndo, CanRedo, IsDirty);
    private void Mutate(Action mutation)
    {
        var before = Observe();
        mutation();
        var after = Observe();
        if (!SameShapes(before.Shapes, after.Shapes) || before.SelectedShapeId != after.SelectedShapeId ||
            before.IsEditing != after.IsEditing || before.CanUndo != after.CanUndo ||
            before.CanRedo != after.CanRedo || before.IsDirty != after.IsDirty)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
