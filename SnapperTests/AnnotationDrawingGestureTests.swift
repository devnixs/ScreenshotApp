import CoreGraphics
import Testing
@testable import Snapper

struct AnnotationDrawingGestureTests {
    private let transform = CanvasTransform(imagePixelSize: CGSize(width: 1600, height: 900), pixelsPerPoint: 2, viewSize: CGSize(width: 400, height: 400))
    private func gesture(_ tool: AnnotationTool = .rectangle, start: CGPoint = CGPoint(x: 100, y: 100)) throws -> AnnotationDrawingGesture {
        try #require(AnnotationDrawingGesture(tool: tool, viewPoint: transform.viewPoint(fromImage: start), transform: transform, color: .red, strokeWidth: .m))
    }
    @Test func marginsAndPassiveToolsNeverBegin() {
        for tool in AnnotationTool.allCases {
            #expect(AnnotationDrawingGesture(tool: tool, viewPoint: .zero, transform: transform, color: .red, strokeWidth: .m) == nil)
        }
        for tool in [AnnotationTool.select, .text] {
            #expect(AnnotationDrawingGesture(tool: tool, viewPoint: CGPoint(x: 200, y: 200), transform: transform, color: .red, strokeWidth: .m) == nil)
        }
    }
    @Test func thresholdsUseViewPointsAtAnyZoom() throws {
        for ppp in [CGFloat(1), 2] {
            for viewWidth in [CGFloat(200), 800] {
                let t = CanvasTransform(imagePixelSize: CGSize(width: 800, height: 800), pixelsPerPoint: ppp, viewSize: CGSize(width: viewWidth, height: viewWidth))
                for tool in [AnnotationTool.rectangle, .arrow] {
                    var g = try #require(AnnotationDrawingGesture(tool: tool, viewPoint: t.imageRectInView.origin, transform: t, color: .red, strokeWidth: .m))
                    #expect(g.shape(constrained: false, completing: true) == nil)
                    g.update(viewPoint: CGPoint(x: t.imageRectInView.minX + 2, y: t.imageRectInView.minY + 1))
                    #expect(g.shape(constrained: false, completing: true) == nil)
                    g.update(viewPoint: CGPoint(x: t.imageRectInView.minX + 20, y: t.imageRectInView.minY + 2))
                    #expect((g.shape(constrained: false, completing: true) != nil) == (tool == .arrow))
                    g.update(viewPoint: CGPoint(x: t.imageRectInView.minX + 20, y: t.imageRectInView.minY + 20))
                    #expect(g.shape(constrained: false, completing: true) != nil)
                }
            }
        }
    }
    @Test func shiftUpdatesWithoutMovementAndCompletionIsOneUndo() throws {
        var g = try gesture()
        g.update(viewPoint: transform.viewPoint(fromImage: CGPoint(x: 300, y: 200)))
        let free = try #require(g.shape(constrained: false, completing: false))
        let square = try #require(g.shape(constrained: true, completing: false))
        #expect(free.kind == .rectangle(CGRect(x: 100, y: 100, width: 200, height: 100)))
        #expect(square.kind == .rectangle(CGRect(x: 100, y: 100, width: 200, height: 200)))
        #expect(g.shape(constrained: false, completing: false)?.kind == free.kind)
        var d = AnnotationDocument(imagePixelSize: transform.imagePixelSize, pixelsPerPoint: 2)
        #expect(d.shapes.isEmpty && !d.canUndo)
        d.add(try #require(g.shape(constrained: true, completing: true)))
        #expect(d.shapes.count == 1)
        d.undo()
        #expect(d.shapes.isEmpty && !d.canUndo)
        d.redo()
        #expect(d.shapes.first?.kind == square.kind)
        #expect(g.tool == .rectangle)
    }
    @Test func outsidePointsAndConstraintsStayInsideImage() throws {
        for tool in [AnnotationTool.rectangle, .arrow] {
            var g = try gesture(tool, start: CGPoint(x: 1500, y: 800))
            g.update(viewPoint: CGPoint(x: 900, y: 900))
            #expect(g.current == CGPoint(x: 1600, y: 900))
            for constrained in [false, true] {
                let shape = try #require(g.shape(constrained: constrained, completing: true))
                for (_, point) in shape.handles {
                    #expect(point.x >= 0 && point.x <= 1600 && point.y >= 0 && point.y <= 900)
                }
            }
        }
        var edgeSquare = try gesture(.rectangle, start: CGPoint(x: 1500, y: 100))
        edgeSquare.update(viewPoint: transform.viewPoint(fromImage: CGPoint(x: 1600, y: 500)))
        #expect(edgeSquare.shape(constrained: true, completing: true)?.kind
                == .rectangle(CGRect(x: 1500, y: 100, width: 100, height: 100)))
        var edgeArrow = try gesture(.arrow, start: CGPoint(x: 1500, y: 700))
        edgeArrow.update(viewPoint: transform.viewPoint(fromImage: CGPoint(x: 1600, y: 850)))
        let clipped = try #require(edgeArrow.shape(constrained: true, completing: true))
        guard case .arrow(_, let clippedEnd) = clipped.kind else { Issue.record("Expected arrow"); return }
        #expect(abs(clippedEnd.x - 1600) < 1e-8 && abs(clippedEnd.y - 800) < 1e-8)
        var arrow = try gesture(.arrow)
        arrow.update(viewPoint: transform.viewPoint(fromImage: CGPoint(x: 300, y: 250)))
        let snapped = try #require(arrow.shape(constrained: true, completing: true))
        guard case .arrow(let start, let end) = snapped.kind else { Issue.record("Expected arrow"); return }
        #expect(abs((end.x - start.x) - (end.y - start.y)) < 1e-8)
    }
}
