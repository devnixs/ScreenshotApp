import Foundation
import CoreGraphics

/// An annotation session in image pixels, independent of its window and captured image.
/// Selection accompanies history snapshots but never creates an edit on its own.
struct AnnotationDocument: Sendable {
    struct State: Equatable, Sendable {
        var shapes: [AnnotationShape]
        var selectedShapeID: UUID?
        var revision: Int
    }

    let imagePixelSize: CGSize
    let pixelsPerPoint: CGFloat

    private var current = State(shapes: [], selectedShapeID: nil, revision: 0)
    private var undoStack: [State] = []
    private var redoStack: [State] = []
    private var sessionStart: State?
    private var nextRevision = 1
    private var cleanRevision = 0

    /// The captured image dimensions and its backing scale must all be positive.
    init(imagePixelSize: CGSize, pixelsPerPoint: CGFloat) {
        assert(imagePixelSize.width > 0 && imagePixelSize.height > 0 && pixelsPerPoint > 0)
        self.imagePixelSize = imagePixelSize
        self.pixelsPerPoint = pixelsPerPoint
    }

    /// Array order is back to front: the last shape is topmost.
    var shapes: [AnnotationShape] { current.shapes }
    var selectedShapeID: UUID? { current.selectedShapeID }
    var selectedShape: AnnotationShape? {
        guard let id = selectedShapeID else { return nil }
        return shape(withID: id)
    }

    func shape(withID id: UUID) -> AnnotationShape? {
        shapes.first { $0.id == id }
    }

    /// Both the point and tolerance are in image pixels.
    func topmostShape(at point: CGPoint, tolerance: CGFloat) -> AnnotationShape? {
        shapes.reversed().first { $0.hitTest(point, pixelsPerPoint: pixelsPerPoint, slop: tolerance) }
    }

    mutating func add(_ shape: AnnotationShape) {
        guard self.shape(withID: shape.id) == nil else {
            assertionFailure("Shape IDs must be unique")
            return
        }
        edit { $0.shapes.append(shape) }
    }

    mutating func delete(id: UUID) {
        guard let index = shapes.firstIndex(where: { $0.id == id }) else { return }
        edit {
            $0.shapes.remove(at: index)
            if $0.selectedShapeID == id { $0.selectedShapeID = nil }
        }
    }

    mutating func replace(_ shape: AnnotationShape) {
        guard let index = shapes.firstIndex(where: { $0.id == shape.id }) else { return }
        edit { $0.shapes[index] = shape }
    }

    mutating func select(id: UUID?) {
        current.selectedShapeID = id
        enforceSelectionInvariant()
    }

    var isEditing: Bool { sessionStart != nil }

    mutating func beginEdit() {
        guard !isEditing else {
            assertionFailure("An edit is already open")
            return
        }
        sessionStart = current
    }

    mutating func commitEdit() {
        guard let start = sessionStart else { return }
        sessionStart = nil
        guard current.shapes != start.shapes else { return }
        undoStack.append(start)
        current.revision = nextRevision
        nextRevision += 1
        redoStack.removeAll()
    }

    mutating func cancelEdit() {
        guard let start = sessionStart else { return }
        sessionStart = nil
        restore(start)
    }

    var canUndo: Bool { !isEditing && !undoStack.isEmpty }
    var canRedo: Bool { !isEditing && !redoStack.isEmpty }

    mutating func undo() {
        guard canUndo, let previous = undoStack.popLast() else { return }
        redoStack.append(current)
        restore(previous)
    }

    mutating func redo() {
        guard canRedo, let next = redoStack.popLast() else { return }
        undoStack.append(current)
        restore(next)
    }

    var isDirty: Bool {
        current.revision != cleanRevision
            || sessionStart.map { current.shapes != $0.shapes } == true
    }

    /// Called only after a successful save or copy, after committing any active edit.
    mutating func markClean() {
        assert(!isEditing, "Commit the active edit before marking the document clean")
        cleanRevision = current.revision
    }

    /// Single mutations share exactly the same commit rules as explicit gestures.
    private mutating func edit(_ mutation: (inout State) -> Void) {
        let atomic = !isEditing
        if atomic { beginEdit() }
        mutation(&current)
        if atomic { commitEdit() }
    }

    private mutating func restore(_ state: State) {
        current = state
        enforceSelectionInvariant()
    }

    private mutating func enforceSelectionInvariant() {
        if let id = selectedShapeID, shape(withID: id) == nil {
            current.selectedShapeID = nil
        }
    }
}
