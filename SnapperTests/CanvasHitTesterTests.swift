import CoreGraphics
import Testing
@testable import Snapper

struct CanvasHitTesterTests {
    func transform(_ scale: CGFloat) -> CanvasTransform {
        CanvasTransform(imagePixelSize: CGSize(width: 400, height: 400), pixelsPerPoint: 1 / scale, viewSize: CGSize(width: 400 * scale, height: 400 * scale))
    }
    @Test func distancesScaleInViewPoints() {
        for scale in [CGFloat(0.25), 0.5, 1, 2] {
            #expect(CanvasHitTester.imagePixels(fromViewPoints: 6, transform: transform(scale)) == 6 / scale)
        }
    }
    @Test func selectedHandlesWinAndKeepConstantRadius() {
        var d = AnnotationDocument(imagePixelSize: CGSize(width: 400, height: 400), pixelsPerPoint: 1)
        let lower = AnnotationShape(kind: .rectangle(CGRect(x: 100, y: 100, width: 150, height: 150)), color: .red, strokeWidth: .m)
        let upper = AnnotationShape(kind: .rectangle(CGRect(x: 100, y: 100, width: 170, height: 170)), color: .green, strokeWidth: .m)
        d.add(lower); d.add(upper)
        #expect(CanvasHitTester.hitTest(CGPoint(x: 100, y: 100), in: d, transform: transform(0.5)) == .shape(upper.id))
        d.select(id: lower.id)
        for scale in [CGFloat(0.25), 0.5, 1, 2] {
            #expect(CanvasHitTester.hitTest(CGPoint(x: 100 - 5 / scale, y: 100), in: d, transform: transform(scale)) == .handle(shapeID: lower.id, .topLeft))
            let hit = CanvasHitTester.hitTest(CGPoint(x: 100 - 7 / scale, y: 100), in: d, transform: transform(scale))
            if case .handle = hit { Issue.record("Outside handle radius") }
        }
        #expect(CanvasHitTester.hitTest(CGPoint(x: 180, y: 180), in: d, transform: transform(1)) == .none)
        #expect(CanvasHitTester.hitTest(.zero, in: d, transform: transform(1)) == .none)
    }
}
