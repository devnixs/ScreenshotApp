import CoreGraphics
import Testing
@testable import Snapper

struct UnsavedAnnotationsPolicyTests {
    @Test func messagesAndDecisions() {
        #expect(UnsavedAnnotationsPolicy.quitDecision(dirtyWindowCount: 0) == .terminateNow)
        for n in [1, 2, 3, 10] {
            let message = "Quit and discard \(n) unsaved annotation\(n == 1 ? "" : "s")?"
            #expect(UnsavedAnnotationsPolicy.quitMessage(dirtyWindowCount: n) == message)
            #expect(UnsavedAnnotationsPolicy.quitDecision(dirtyWindowCount: n) == .confirm(message: message))
        }
        #expect(UnsavedAnnotationsPolicy.quitMessage(dirtyWindowCount: 0) == "Quit and discard 0 unsaved annotations?")
    }
    @Test func realModelCloseTable() {
        var document = AnnotationDocument(imagePixelSize: CGSize(width: 200, height: 100), pixelsPerPoint: 1)
        func decision(_ doc: AnnotationDocument) -> UnsavedAnnotationsPolicy.CloseDecision {
            UnsavedAnnotationsPolicy.closeDecision(isDirtyAfterCommittingText: doc.isDirty)
        }
        #expect(decision(document) == .closeImmediately)
        var draft = TextEditDraft(original: nil, string: "", origin: .zero, color: .red,
            strokeWidth: .m, measuredSize: CGSize(width: 40, height: 30))
        document.apply(TextCommitDecision.decide(draft))
        #expect(decision(document) == .closeImmediately)
        draft.string = "Hi"
        document.apply(TextCommitDecision.decide(draft))
        #expect(decision(document) == .confirmDiscard)
        document.markClean()
        #expect(decision(document) == .closeImmediately)
        document.add(AnnotationShape(kind: .rectangle(CGRect(x: 10, y: 10, width: 30, height: 30)), color: .red, strokeWidth: .m))
        #expect(decision(document) == .confirmDiscard)
        document.markClean()
        #expect(decision(document) == .closeImmediately)
        document.replace(document.shapes[0].withColor(.blue))
        #expect(decision(document) == .confirmDiscard)
    }
}
