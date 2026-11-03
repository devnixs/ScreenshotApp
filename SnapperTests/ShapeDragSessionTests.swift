import CoreGraphics
import Testing
@testable import Snapper

struct ShapeDragSessionTests {
    let bounds = CGRect(x: 0, y: 0, width: 100, height: 100)
    let rectangle = AnnotationShape(kind: .rectangle(CGRect(x: 20, y: 20, width: 40, height: 30)), color: .red, strokeWidth: .m)
    @Test func movesClampAndNeverAccumulate() {
        let session = ShapeDragSession(original: rectangle, kind: .move, startPoint: CGPoint(x: 20, y: 30), imageBounds: bounds)
        let point = CGPoint(x: 30, y: 25)
        for _ in 0..<100 {
            #expect(session.preview(at: point, shiftHeld: false).kind == .rectangle(CGRect(x: 30, y: 15, width: 40, height: 30)))
        }
        #expect(session.preview(at: point, shiftHeld: true) == session.preview(at: point, shiftHeld: false))
        #expect(session.finish(at: session.startPoint, shiftHeld: false) == nil)
        #expect(session.preview(at: CGPoint(x: -50, y: -50), shiftHeld: false).kind == .rectangle(CGRect(x: 0, y: -10, width: 40, height: 30)))
    }
    @Test func eachCornerAnchorsOppositeAndShiftIsReversible() throws {
        for (handle, start) in rectangle.handles {
            let fixed = try #require(rectangle.handles.first { $0.0 == handle.opposite }?.1)
            let session = ShapeDragSession(original: rectangle, kind: .resize(handle), startPoint: start, imageBounds: bounds)
            let point = CGPoint(x: 10, y: 80)
            let free = session.preview(at: point, shiftHeld: false)
            #expect(free.kind == rectangle.resized(handle: handle, to: point, constrained: false).shape.kind)
            for _ in 0..<100 {
                let square = session.preview(at: point, shiftHeld: true)
                guard case .rectangle(let r) = square.kind else { Issue.record("Expected rectangle"); return }
                #expect(r.width == r.height)
                #expect(square.handles.contains { $0.1 == fixed })
                #expect(session.preview(at: point, shiftHeld: false) == free)
            }
            #expect(session.finish(at: start, shiftHeld: false) == nil)
        }
    }
    @Test func offCenterHandleClickDoesNotResize() {
        let start = CGPoint(x: 18, y: 20)
        let session = ShapeDragSession(original: rectangle, kind: .resize(.topLeft), startPoint: start, imageBounds: bounds)
        #expect(session.preview(at: start, shiftHeld: false) == rectangle)
        #expect(session.finish(at: start, shiftHeld: false) == nil)
    }
    @Test func arrowMovementAndEndpointSnapping() throws {
        let arrow = AnnotationShape(kind: .arrow(start: CGPoint(x: 20, y: 20), end: CGPoint(x: 50, y: 50)), color: .blue, strokeWidth: .s)
        let move = ShapeDragSession(original: arrow, kind: .move, startPoint: CGPoint(x: 20, y: 20), imageBounds: bounds)
        #expect(move.preview(at: CGPoint(x: 30, y: 15), shiftHeld: false).kind == .arrow(start: CGPoint(x: 30, y: 15), end: CGPoint(x: 60, y: 45)))
        for (handle, point) in arrow.handles {
            let session = ShapeDragSession(original: arrow, kind: .resize(handle), startPoint: point, imageBounds: bounds)
            let shape = session.preview(at: CGPoint(x: 90, y: 70), shiftHeld: true)
            guard case .arrow(let a, let b) = shape.kind else { Issue.record("Expected arrow"); return }
            let angle = atan2(b.y - a.y, b.x - a.x) / (.pi / 4)
            #expect(abs(angle - angle.rounded()) < 1e-8)
            #expect(shape.handles.first { $0.0 == handle.opposite }?.1 == arrow.handles.first { $0.0 == handle.opposite }?.1)
        }
    }
    @Test func previewsCommitOneHistoryStep() throws {
        var document = AnnotationDocument(imagePixelSize: bounds.size, pixelsPerPoint: 1)
        document.add(rectangle)
        document.markClean()
        document.select(id: rectangle.id)
        let session = ShapeDragSession(original: rectangle, kind: .move, startPoint: .zero, imageBounds: bounds)
        for i in 0..<100 { _ = session.preview(at: CGPoint(x: i, y: i), shiftHeld: false) }
        #expect(!document.isDirty)
        let final = try #require(session.finish(at: CGPoint(x: 10, y: 10), shiftHeld: false))
        document.replace(final)
        document.undo()
        #expect(document.shapes == [rectangle] && !document.isDirty)
        document.redo()
        #expect(document.shapes == [final])
    }
}
