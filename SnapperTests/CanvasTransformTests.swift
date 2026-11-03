import CoreGraphics
import Testing
@testable import Snapper

struct CanvasTransformTests {
    @Test func exactFitAndRoundTrips() throws {
        let t = CanvasTransform(imagePixelSize: CGSize(width: 400, height: 300), pixelsPerPoint: 1, viewSize: CGSize(width: 400, height: 300))
        #expect(t.scale == 1)
        #expect(t.imageRectInView == CGRect(x: 0, y: 0, width: 400, height: 300))
        for point in [CGPoint.zero, CGPoint(x: 400, y: 300), CGPoint(x: 200, y: 150)] {
            #expect(t.viewPoint(fromImage: try #require(t.imagePoint(fromView: point))) == point)
        }
    }
    @Test func letterboxesAndClampsBothAxes() throws {
        let t = CanvasTransform(imagePixelSize: CGSize(width: 1600, height: 900), pixelsPerPoint: 2, viewSize: CGSize(width: 400, height: 400))
        #expect(t.scale == 0.25)
        #expect(t.imageRectInView == CGRect(x: 0, y: 87.5, width: 400, height: 225))
        #expect(t.imagePoint(fromView: CGPoint(x: 200, y: 200)) == CGPoint(x: 800, y: 450))
        #expect(t.imagePoint(fromView: CGPoint(x: 0, y: 87.5)) == .zero)
        #expect(t.isInsideImage(viewPoint: CGPoint(x: 200, y: 200)))
        #expect(!t.isInsideImage(viewPoint: .zero))
        #expect(t.clampedImagePoint(fromView: CGPoint(x: -20, y: -20)) == .zero)
        #expect(t.clampedImagePoint(fromView: CGPoint(x: 500, y: 500)) == CGPoint(x: 1600, y: 900))
        let rect = CGRect(x: 50, y: 60, width: 300, height: 100)
        #expect(t.imageRect(fromView: t.viewRect(fromImage: rect)) == rect)
        #expect(t.viewLength(fromImage: 8) == 2)
        let portrait = CanvasTransform(imagePixelSize: CGSize(width: 600, height: 1200), pixelsPerPoint: 1, viewSize: CGSize(width: 600, height: 300))
        #expect(portrait.imageRectInView == CGRect(x: 225, y: 0, width: 150, height: 300))
    }
    @Test(arguments: [CGFloat(1), 2]) func neverUpscales(ppp: CGFloat) {
        let t = CanvasTransform(imagePixelSize: CGSize(width: 200, height: 100), pixelsPerPoint: ppp, viewSize: CGSize(width: 800, height: 800))
        #expect(t.scale == 1 / ppp)
        #expect(t.imageRectInView.midX == 400 && t.imageRectInView.midY == 400)
        if ppp == 2 { #expect(t.imageRectInView == CGRect(x: 350, y: 375, width: 100, height: 50)) }
    }
    @Test func invalidInputsAreSafe() {
        for bad in [CGSize.zero, CGSize(width: -1, height: 100), CGSize(width: CGFloat.infinity, height: 100)] {
            for t in [CanvasTransform(imagePixelSize: bad, pixelsPerPoint: 1, viewSize: CGSize(width: 100, height: 100)),
                      CanvasTransform(imagePixelSize: CGSize(width: 100, height: 100), pixelsPerPoint: 1, viewSize: bad)] {
                #expect(t.scale == 0)
                #expect(t.imagePoint(fromView: .zero) == nil)
                #expect(t.clampedImagePoint(fromView: .zero) == nil)
                #expect(t.imageRect(fromView: .zero) == nil)
                #expect(!t.isInsideImage(viewPoint: .zero))
            }
        }
        #expect(CanvasTransform(imagePixelSize: CGSize(width: 1, height: 1), pixelsPerPoint: 0, viewSize: CGSize(width: 1, height: 1)).scale == 0)
    }
}
