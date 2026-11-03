import CoreGraphics
import Testing
@testable import Snapper

struct TextInteractionDecisionTests {
    @Test func textClickReentryTopmostAndDrawingTools() {
        var document = AnnotationDocument(imagePixelSize: CGSize(width: 100, height: 100), pixelsPerPoint: 2)
        let first = TextCommitDecisionTests().shape()
        var second = AnnotationShape(kind: first.kind, color: .blue, strokeWidth: .m)
        second.kind = first.kind
        document.add(first); document.add(second)
        let point = CGPoint(x: 20, y: 30)
        func decision(_ tool: AnnotationTool, _ clicks: Int = 1, _ editing: Bool = false, _ point: CGPoint? = point) -> TextInteractionDecision {
            .decide(isEditing: editing, tool: tool, clickCount: clicks, point: point, document: document)
        }
        #expect(decision(.text) == .edit(second.id))
        #expect(decision(.text, 2) == .edit(second.id))
        #expect(decision(.select, 2) == .edit(second.id))
        for tool in [AnnotationTool.rectangle, .arrow, .select] { #expect(decision(tool) == .none) }
        #expect(decision(.arrow, 2) == .none)
        #expect(decision(.rectangle, 2) == .none)
        #expect(decision(.text, 1, false, CGPoint(x: 90, y: 90)) == .beginNew(CGPoint(x: 90, y: 90)))
        #expect(decision(.text, 1, false, nil) == .none)
        #expect(decision(.text, 1, true, nil) == .commit)
        #expect(decision(.rectangle, 1, true) == .commit)
    }
}
