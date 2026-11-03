import CoreGraphics
import Testing
@testable import Snapper

struct TextEditingUndoTests {
    @Test func addDiscardNoChangeAndDeleteHistory() {
        var document = AnnotationDocument(imagePixelSize: CGSize(width: 100, height: 100), pixelsPerPoint: 2)
        let fixture = TextCommitDecisionTests()
        document.apply(.discard)
        document.apply(.noChange)
        #expect(!document.canUndo && !document.isDirty)
        document.apply(TextCommitDecision.decide(fixture.draft()))
        #expect(document.shapes.count == 1 && document.isDirty)
        let original = document.shapes[0]
        document.undo()
        #expect(document.shapes.isEmpty && !document.canUndo)
        document.redo()
        #expect(document.shapes == [original])
        document.markClean()
        document.apply(.noChange)
        #expect(!document.isDirty)
        document.apply(.delete(original.id))
        #expect(document.shapes.isEmpty)
        document.undo()
        #expect(document.shapes == [original] && !document.isDirty)
    }
    @Test func completeSessionAndSelectedWidthAreAtomic() {
        var document = AnnotationDocument(imagePixelSize: CGSize(width: 100, height: 100), pixelsPerPoint: 2)
        let fixture = TextCommitDecisionTests()
        let original = fixture.shape()
        document.add(original)
        document.markClean()
        var draft = fixture.draft(original: original, string: "Hello")
        draft.color = .blue; draft.strokeWidth = .l; draft.measuredSize = CGSize(width: 100, height: 70)
        document.apply(TextCommitDecision.decide(draft))
        let updated = document.shapes[0]
        document.undo()
        #expect(document.shapes == [original] && !document.isDirty)
        document.redo()
        #expect(document.shapes == [updated])
        document.select(id: updated.id)
        let small = updated.withStrokeWidth(.s, pixelsPerPoint: document.pixelsPerPoint, measurer: FakeTextMeasurer())
        document.replace(small)
        guard case .text(let box) = document.shapes[0].kind else { Issue.record("Expected text"); return }
        #expect(box.measuredSize == CGSize(width: 160, height: 32))
        document.undo()
        #expect(document.shapes == [updated])
        document.undo()
        #expect(document.shapes == [original])
    }
}
