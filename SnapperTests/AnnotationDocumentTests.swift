import Foundation
import CoreGraphics
import Testing
@testable import Snapper

struct AnnotationDocumentTests {
    private func document() -> AnnotationDocument {
        AnnotationDocument(imagePixelSize: CGSize(width: 2000, height: 1200), pixelsPerPoint: 2)
    }
    private func rect(_ x: CGFloat = 100) -> AnnotationShape {
        AnnotationShape(kind: .rectangle(CGRect(x: x, y: 100, width: 200, height: 100)),
                        color: .red, strokeWidth: .m)
    }
    private func text(_ string: String = "") -> AnnotationShape {
        AnnotationShape.makeText(origin: CGPoint(x: 10, y: 20), color: .red, strokeWidth: .m)
            .withText(string).withMeasuredSize(CGSize(width: 80, height: 40))
    }
    private func moved(_ shape: AnnotationShape, _ distance: CGFloat = 20) -> AnnotationShape {
        shape.moved(by: CGVector(dx: distance, dy: distance))
    }

    @Test func testNewDocumentIsEmptyCleanAndHasNoHistory() {
        let d = document()
        #expect(d.shapes.isEmpty)
        #expect(d.selectedShapeID == nil && d.selectedShape == nil)
        #expect(!d.canUndo && !d.canRedo && !d.isDirty && !d.isEditing)
        #expect(d.imagePixelSize == CGSize(width: 2000, height: 1200))
        #expect(d.pixelsPerPoint == 2)
    }

    @Test func testAddAppendsOnTopAndIsOneUndoStep() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b)
        #expect(d.shapes == [a, b])
        #expect(d.selectedShapeID == nil)
        #expect(d.canUndo)
        d.undo(); #expect(d.shapes == [a])
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo)
    }

    @Test func testDeleteRemovesShapeAndUndoRestoresAtSameIndex() {
        var d = document()
        let a = rect(), b = rect(200), c = rect(300)
        d.add(a); d.add(b); d.add(c); d.delete(id: b.id)
        #expect(d.shapes == [a, c])
        d.undo(); #expect(d.shapes == [a, b, c])
        d.redo(); #expect(d.shapes == [a, c])
    }

    @Test func testDeleteOfSelectedShapeClearsSelectionAndUndoReselects() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.select(id: b.id); d.delete(id: b.id)
        #expect(d.selectedShapeID == nil)
        d.undo()
        #expect(d.selectedShape == b && d.shapes == [a, b])
        d.redo(); #expect(d.selectedShape == nil)
    }

    @Test func testDeleteOfUnselectedShapeKeepsSelection() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.select(id: a.id); d.delete(id: b.id)
        #expect(d.selectedShape == a)
    }

    @Test func testReplaceKeepsZOrderAndIsUndoable() {
        let a = rect(), b = rect(200), t = text("Hello")
        let changes = [moved(a), a.withColor(.blue), a.withStrokeWidth(.l),
                       a.resized(handle: .bottomRight, to: CGPoint(x: 400, y: 250), constrained: false).shape]
        for change in changes {
            var d = document()
            d.add(a); d.add(b); d.replace(change)
            #expect(d.shapes == [change, b])
            d.undo(); #expect(d.shapes == [a, b])
            d.redo(); #expect(d.shapes == [change, b])
        }
        var d = document()
        d.add(t)
        let edited = t.withText("Edited").withMeasuredSize(CGSize(width: 100, height: 40))
        d.replace(edited)
        d.undo(); #expect(d.shapes == [t])
        d.redo(); #expect(d.shapes == [edited])
    }

    @Test func testReplaceWithIdenticalValuePushesNothing() {
        var d = document()
        let a = rect()
        d.add(a); d.markClean(); d.replace(a)
        #expect(!d.isDirty)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo)
    }

    @Test func testUnknownIDDeleteAndReplaceAreNoOps() {
        var d = document()
        let unknown = rect()
        d.delete(id: unknown.id); d.replace(unknown)
        #expect(d.shapes.isEmpty && !d.canUndo && !d.isDirty)
        let a = rect()
        d.add(a); d.undo()
        d.delete(id: unknown.id); d.replace(unknown)
        #expect(d.canRedo && !d.canUndo && !d.isDirty)
        d.redo(); #expect(d.shapes == [a])
    }

    @Test func testUndoRedoSequence() {
        var d = document()
        let a = rect(), b = rect(200), changed = moved(b)
        d.add(a); d.add(b); d.replace(changed)
        d.undo(); #expect(d.shapes == [a, b])
        d.undo(); #expect(d.shapes == [a])
        d.undo(); #expect(d.shapes.isEmpty)
        d.redo(); #expect(d.shapes == [a])
        d.redo(); #expect(d.shapes == [a, b])
        d.redo(); #expect(d.shapes == [a, changed] && !d.canRedo)
    }

    @Test func testNewEditClearsRedo() {
        var d = document()
        let a = rect(), b = rect(200), c = rect(300)
        d.add(a); d.add(b); d.undo(); d.add(c)
        #expect(!d.canRedo && d.shapes == [a, c])
        d.redo(); #expect(d.shapes == [a, c])
    }

    @Test func testCoalescedDragIsOneStep() {
        var d = document()
        let a = rect()
        d.add(a); d.beginEdit()
        for distance in 1...20 { d.replace(moved(a, CGFloat(distance))) }
        d.commitEdit()
        #expect(d.shapes == [moved(a, 20)])
        d.undo(); #expect(d.shapes == [a] && d.canUndo)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo)
        d.redo(); d.redo(); #expect(d.shapes == [moved(a, 20)])
    }

    @Test func testCreateDragSessionIsOneStep() {
        var d = document()
        let prior = rect(), a = rect(200)
        d.add(prior); d.select(id: prior.id)
        d.beginEdit(); d.add(a)
        for distance in 1...5 { d.replace(moved(a, CGFloat(distance))) }
        d.select(id: a.id); d.commitEdit()
        d.undo()
        #expect(d.shapes == [prior] && d.selectedShape == prior)
        d.redo()
        #expect(d.shapes == [prior, moved(a, 5)] && d.selectedShapeID == a.id)
    }

    @Test func testNoOpCommitPushesNothingAndKeepsRedo() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.undo(); d.markClean()
        d.beginEdit(); d.replace(moved(a)); d.replace(a); d.commitEdit()
        #expect(d.canRedo && !d.isDirty)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo)
        d.redo(); d.redo(); #expect(d.shapes == [a, b])
    }

    @Test func testCancelEditRestoresShapesAndSelection() {
        var d = document()
        let a = rect(), b = rect(200), c = rect(300)
        d.add(a); d.add(c); d.undo(); d.select(id: a.id); d.markClean()
        d.beginEdit(); d.replace(moved(a)); d.add(b); d.select(id: b.id); d.cancelEdit()
        #expect(d.shapes == [a] && d.selectedShapeID == a.id)
        #expect(!d.isEditing && !d.isDirty && d.canRedo)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo)
    }

    @Test func testUndoRedoUnavailableWhileEditing() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.undo(); d.beginEdit(); d.replace(moved(a))
        #expect(!d.canUndo && !d.canRedo && d.isEditing)
        d.undo(); d.redo(); #expect(d.shapes == [moved(a)])
        d.cancelEdit(); #expect(d.canUndo && d.canRedo)
        d.beginEdit(); d.commitEdit(); #expect(d.canUndo && d.canRedo)
    }

    @Test func testCommitAndCancelWithoutSessionAreNoOps() {
        var d = document()
        d.undo(); d.redo(); d.commitEdit(); d.cancelEdit()
        #expect(d.shapes.isEmpty && !d.isDirty && !d.canUndo && !d.canRedo && !d.isEditing)
        let a = rect()
        d.add(a); d.undo(); d.commitEdit(); d.cancelEdit()
        #expect(d.canRedo && !d.canUndo && !d.isDirty)
    }

    @Test func testSelectionIsNotUndoableAndNotDirty() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.markClean()
        d.select(id: a.id); #expect(d.selectedShape == a)
        d.select(id: b.id); d.select(id: nil); d.select(id: UUID())
        #expect(!d.isDirty && d.selectedShapeID == nil)
        d.undo(); #expect(d.shapes == [a])
        d.select(id: a.id); #expect(d.canRedo)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo && d.selectedShapeID == nil)
    }

    @Test func testRedoRestoresSelectionAtCommit() {
        var d = document()
        let a = rect()
        d.beginEdit(); d.add(a); d.select(id: a.id); d.commitEdit()
        d.undo(); #expect(d.shapes.isEmpty && d.selectedShapeID == nil)
        d.redo(); #expect(d.shapes == [a] && d.selectedShape == a)
    }

    @Test func testTopmostShapeWinsOnOverlap() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b)
        #expect(d.shape(withID: a.id) == a)
        #expect(d.shape(withID: UUID()) == nil)
        #expect(d.topmostShape(at: CGPoint(x: 250, y: 100), tolerance: 0) == b)
        #expect(d.topmostShape(at: CGPoint(x: 150, y: 100), tolerance: 0) == a)
        #expect(d.topmostShape(at: CGPoint(x: 50, y: 50), tolerance: 0) == nil)
        #expect(d.topmostShape(at: CGPoint(x: 250, y: 108), tolerance: 4) == b)
        #expect(d.topmostShape(at: CGPoint(x: 250, y: 109), tolerance: 4) == nil)
        d.delete(id: b.id)
        #expect(d.topmostShape(at: CGPoint(x: 250, y: 100), tolerance: 0) == a)
        let t = text("Hi")
        d.add(t)
        #expect(d.topmostShape(at: CGPoint(x: 30, y: 30), tolerance: 0) == t)
        let arrow = AnnotationShape(kind: .arrow(start: .zero, end: CGPoint(x: 100, y: 0)),
                                    color: .blue, strokeWidth: .m)
        d.add(arrow)
        #expect(d.topmostShape(at: CGPoint(x: 65, y: 10), tolerance: 0) == arrow)
    }

    @Test func testDirtyLifecycle() {
        var d = document()
        let a = rect()
        #expect(!d.isDirty)
        d.add(a); #expect(d.isDirty)
        d.markClean(); #expect(!d.isDirty)
        d.replace(moved(a)); #expect(d.isDirty)
        d.undo(); #expect(!d.isDirty)
        d.redo(); #expect(d.isDirty)
        d.undo(); #expect(!d.isDirty)
    }

    @Test func testEditAfterUndoToCleanIsDirty() {
        var d = document()
        let a = rect()
        d.add(a); d.markClean(); d.replace(moved(a)); d.undo()
        #expect(!d.isDirty)
        d.replace(moved(a, 40)); #expect(d.isDirty && !d.canRedo)
        // Regression: save, undo, branch to the same history depth must not reuse a revision.
        d.markClean(); d.undo(); d.replace(moved(a, 60))
        #expect(d.isDirty && !d.canRedo)
    }

    @Test func testMarkCleanMidHistory() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.undo(); d.markClean()
        #expect(!d.isDirty)
        d.redo(); #expect(d.isDirty)
        d.undo(); #expect(!d.isDirty)
        d.undo(); #expect(d.isDirty)
        d.redo(); #expect(!d.isDirty)
    }

    @Test func testDirtyWhileEditingWithUncommittedChange() {
        var d = document()
        let a = rect()
        d.add(a); d.markClean(); d.beginEdit()
        #expect(!d.isDirty)
        d.replace(moved(a)); #expect(d.isDirty)
        d.replace(a); #expect(!d.isDirty)
        d.replace(moved(a)); d.cancelEdit(); #expect(!d.isDirty)
    }

    @Test func testAbandonedNewEmptyTextBoxLeavesNoTrace() {
        var d = document()
        let a = rect(), t = text()
        d.add(a); d.undo(); d.markClean()
        d.beginEdit(); d.add(t); d.select(id: t.id); d.replace(t.withText(""))
        d.delete(id: t.id); d.commitEdit()
        #expect(d.canRedo && !d.canUndo && !d.isDirty && d.selectedShapeID == nil)
        d.redo(); #expect(d.shapes == [a])
    }

    @Test func testReEditingTextToEmptyIsOneUndoStep() {
        var d = document()
        let t = text("Hi"), r = rect()
        d.add(t); d.add(r); d.select(id: t.id); d.markClean()
        d.beginEdit(); d.replace(t.withText("")); d.delete(id: t.id); d.commitEdit()
        #expect(d.shapes == [r] && d.isDirty)
        d.undo(); #expect(d.shapes == [t, r] && d.selectedShape == t && !d.isDirty)
        d.redo(); #expect(d.shapes == [r] && d.selectedShapeID == nil)
    }

    @Test func testTextKeystrokesAreOneEdit() {
        var d = document()
        let t = text("Hi")
        d.add(t); d.markClean(); d.beginEdit()
        for string in ["H", "Hello", "Hello\nworld"] { d.replace(t.withText(string)) }
        d.commitEdit()
        #expect(d.shapes == [t.withText("Hello\nworld")])
        d.undo(); #expect(d.shapes == [t] && !d.isDirty)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo)
    }

    @Test func testSelectionOnlySessionKeepsSelectionAndRedo() {
        var d = document()
        let a = rect(), b = rect(200)
        d.add(a); d.add(b); d.undo(); d.markClean()
        d.beginEdit(); d.select(id: a.id); d.commitEdit()
        #expect(d.selectedShape == a && !d.isDirty && d.canRedo)
        d.undo(); #expect(d.shapes.isEmpty && !d.canUndo && d.selectedShapeID == nil)
    }

    @Test func testDocumentCopiesHaveIndependentHistoryAndCleanMarkers() {
        var original = document()
        let a = rect()
        original.add(a); original.markClean()
        var copy = original
        copy.beginEdit(); copy.replace(moved(a)); copy.commitEdit()
        #expect(original.shapes == [a] && !original.isDirty)
        #expect(copy.shapes == [moved(a)] && copy.isDirty)
        copy.undo(); copy.undo()
        #expect(copy.shapes.isEmpty && original.shapes == [a])
        original.undo(); #expect(original.shapes.isEmpty && original.isDirty)
    }
}
