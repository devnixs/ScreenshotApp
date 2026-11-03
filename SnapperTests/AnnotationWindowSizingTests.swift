import CoreGraphics
import Testing
@testable import Snapper

struct AnnotationWindowSizingTests {
    private func size(_ image: CGSize, ppp: CGFloat = 2, screen: CGRect = CGRect(x: 0, y: 0, width: 1440, height: 875)) -> (contentSize: CGSize, windowFrame: CGRect) {
        AnnotationWindowSizing.frame(imagePixelSize: image, pixelsPerPoint: ppp, screenVisibleFrame: screen, chromeHeight: 52, minContentSize: CGSize(width: 480, height: 300))
    }
    @Test func fitsAndNeverUpscales() {
        #expect(size(CGSize(width: 5120, height: 2880)).contentSize == CGSize(width: 1152, height: 648))
        #expect(size(CGSize(width: 5120, height: 2000)).contentSize == CGSize(width: 1152, height: 450))
        #expect(size(CGSize(width: 800, height: 600)).contentSize == CGSize(width: 480, height: 300))
        #expect(size(CGSize(width: 20, height: 20)).contentSize == CGSize(width: 480, height: 300))
        #expect(size(CGSize(width: 1000, height: 3000), ppp: 1).contentSize == CGSize(width: 480, height: 648))
        let screen = CGRect(x: 0, y: 0, width: 2560, height: 1415)
        #expect(size(CGSize(width: 1200, height: 800), ppp: 1, screen: screen).contentSize == CGSize(width: 1200, height: 800))
        #expect(size(CGSize(width: 2000, height: 1000), screen: screen).contentSize == CGSize(width: 1000, height: 500))
    }
    @Test func negativeOriginAndSmallScreen() {
        for screen in [CGRect(x: -1920, y: -200, width: 1920, height: 1055), CGRect(x: -300, y: -400, width: 320, height: 240)] {
            let result = size(CGSize(width: 5120, height: 2880), screen: screen)
            #expect(screen.contains(result.windowFrame))
            #expect(abs(result.windowFrame.midX - screen.midX) <= 0.5)
            #expect(abs(result.windowFrame.midY - screen.midY) <= 0.5)
            #expect(result.windowFrame.height == result.contentSize.height + 52)
        }
    }
    @Test func cascadeOnlyCollisionsAndStayOnScreen() {
        let screen = CGRect(x: -1000, y: -500, width: 1000, height: 800)
        let frame = CGRect(x: -800, y: -300, width: 480, height: 352)
        let top = CGPoint(x: frame.minX, y: frame.maxY)
        #expect(AnnotationWindowSizing.cascaded(frame, avoiding: [], within: screen) == frame)
        #expect(AnnotationWindowSizing.cascaded(frame, avoiding: [top], within: screen) == frame.offsetBy(dx: 24, dy: -24))
        #expect(AnnotationWindowSizing.cascaded(frame, avoiding: [top, CGPoint(x: top.x + 24, y: top.y - 24)], within: screen) == frame.offsetBy(dx: 48, dy: -48))
        let edge = CGRect(x: -480, y: -500, width: 480, height: 352)
        #expect(AnnotationWindowSizing.cascaded(edge, avoiding: [CGPoint(x: edge.minX, y: edge.maxY)], within: screen) == edge)
    }
}
