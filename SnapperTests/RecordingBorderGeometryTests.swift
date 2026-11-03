import CoreGraphics
import Testing
@testable import Snapper

struct RecordingBorderGeometryTests {
    @Test func outsetAndFullDisplayClamping() {
        let display = CGRect(x: 0, y: 0, width: 1440, height: 900)
        #expect(RecordingBorderGeometry.windowFrame(recordedRect: CGRect(x: 400, y: 300, width: 600, height: 400),
            displayFrame: display) == CGRect(x: 397, y: 297, width: 606, height: 406))
        #expect(RecordingBorderGeometry.windowFrame(recordedRect: display, displayFrame: display) == display)
    }
    @Test func negativeOriginDisplayEdge() {
        #expect(RecordingBorderGeometry.windowFrame(recordedRect: CGRect(x: -1920, y: 0, width: 500, height: 500),
            displayFrame: CGRect(x: -1920, y: -200, width: 1920, height: 1080)) == CGRect(x: -1920, y: -3, width: 503, height: 506))
    }
    @Test func windowBorderSpansDisplaysWithoutClipping() {
        let rect = CGRect(x: -700, y: 100, width: 1000, height: 600)
        #expect(RecordingBorderGeometry.windowFrame(recordedRect: rect,
            displayFrame: CGRect(x: -1000, y: 0, width: 1000, height: 900), followsWindow: true) ==
            CGRect(x: -703, y: 97, width: 1006, height: 606))
    }
    @Test func strokeInset() {
        #expect(RecordingBorderGeometry.strokeRect(windowSize: CGSize(width: 606, height: 406)) ==
            CGRect(x: 1.5, y: 1.5, width: 603, height: 403))
    }
    @Test(arguments: [CGFloat(1), CGFloat(2)]) func stackedDisplayUsesPoints(scale: CGFloat) {
        let display = DisplayInfo(displayID: 2, frame: CGRect(x: 100, y: 900, width: 1920, height: 1080), backingScale: scale)
        let region = DisplayGeometry.captureRegion(for: CGRect(x: 200, y: 1000, width: 400, height: 300), on: display)
        #expect(RecordingBorderGeometry.windowFrame(recordedRect: region.appKitRect, displayFrame: display.frame) ==
            CGRect(x: 197, y: 997, width: 406, height: 306))
    }
}
