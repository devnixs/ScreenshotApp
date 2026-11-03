import CoreGraphics
import Testing
@testable import Snapper

struct TextMetricsTests {
    @Test func sizesUseCaptureScaleAndCanvasZoom() {
        for (width, points) in [(StrokeWidth.s, 16.0), (.m, 24.0), (.l, 36.0)] {
            for ppp in [1.0, 2.0] { #expect(TextMetrics.fontPixelSize(for: width, pixelsPerPoint: ppp) == CGFloat(points * ppp)) }
        }
        #expect(TextMetrics.editorFontSize(fontPixelSize: 48, canvasScale: 0.5) == 24)
        #expect(TextMetrics.editorFontSize(fontPixelSize: 48, canvasScale: 1) == 48)
        #expect(TextMetrics.editorFontSize(fontPixelSize: 48, canvasScale: 0.01) == 0.48)
    }
    @Test func placementClampsTopBottomAndShortImages() {
        let size = CGSize(width: 100, height: 80)
        func origin(_ x: CGFloat, _ y: CGFloat) -> CGPoint { TextMetrics.newBoxOrigin(click: CGPoint(x: x, y: y), lineHeight: 30, imageSize: size) }
        #expect(origin(50, 40) == CGPoint(x: 50, y: 25))
        #expect(origin(99, 2) == CGPoint(x: 99, y: 0))
        #expect(origin(99, 80) == CGPoint(x: 99, y: 50))
        #expect(origin(100, 80).x < 100)
        #expect(origin(-1, -10) == .zero)
        #expect(TextMetrics.newBoxOrigin(click: CGPoint(x: 10, y: 5), lineHeight: 30, imageSize: CGSize(width: 20, height: 10)).y == 0)
    }
}
