import CoreGraphics
import Testing
@testable import Snapper

struct FakeTextMeasurer: TextMeasuring {
    func layoutSize(of text: String, fontPixelSize: CGFloat) -> CGSize { CGSize(width: CGFloat(text.count) * fontPixelSize, height: fontPixelSize) }
    func lineHeight(fontPixelSize: CGFloat) -> CGFloat { fontPixelSize }
}

struct TextCommitDecisionTests {
    func draft(original: AnnotationShape? = nil, string: String = "Hi") -> TextEditDraft {
        TextEditDraft(original: original, string: string, origin: CGPoint(x: 10, y: 20), color: .red, strokeWidth: .m, measuredSize: CGSize(width: 30, height: 28))
    }
    func shape() -> AnnotationShape {
        AnnotationShape(kind: .text(TextBox(origin: CGPoint(x: 10, y: 20), string: "Hi", measuredSize: CGSize(width: 30, height: 28))), color: .red, strokeWidth: .m)
    }
    @Test func newEmptyDiscardedAndTextPreserved() {
        for string in ["", "  \n\t", "\n"] { #expect(TextCommitDecision.decide(draft(string: string)) == .discard) }
        let text = " Hi\n"
        guard case .add(let shape) = TextCommitDecision.decide(draft(string: text)), case .text(let box) = shape.kind else { Issue.record("Expected add"); return }
        #expect(box.string == text)
        #expect(box.origin == CGPoint(x: 10, y: 20))
    }
    @Test func existingDeleteNoChangeAndUpdates() {
        let original = shape()
        for text in ["", "\n"] { #expect(TextCommitDecision.decide(draft(original: original, string: text)) == .delete(original.id)) }
        #expect(TextCommitDecision.decide(draft(original: original)) == .noChange)
        var color = draft(original: original); color.color = .blue
        var width = draft(original: original); width.strokeWidth = .l; width.measuredSize = CGSize(width: 80, height: 50)
        for value in [color, width, draft(original: original, string: "Hello")] {
            guard case .update(let updated) = TextCommitDecision.decide(value), case .text(let box) = updated.kind else { Issue.record("Expected update"); continue }
            #expect(updated.id == original.id)
            #expect(box.string == value.string)
            #expect(box.measuredSize == value.measuredSize)
        }
    }
}
