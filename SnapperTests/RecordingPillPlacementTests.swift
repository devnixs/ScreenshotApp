import CoreGraphics
import Testing
@testable import Snapper

struct RecordingPillPlacementTests {
    private let visible = CGRect(x: 0, y: 70, width: 1440, height: 805)
    private let size = CGSize(width: 200, height: 32)

    @Test func mainDisplayPositionsAndClamps() {
        let cases: [(CGRect, RecordingPillPlacement.Position, CGRect)] = [
            (CGRect(x: 400, y: 300, width: 600, height: 400), .below, CGRect(x: 600, y: 260, width: 200, height: 32)),
            (CGRect(x: 400, y: 110, width: 600, height: 400), .below, CGRect(x: 600, y: 70, width: 200, height: 32)),
            (CGRect(x: 400, y: 80, width: 600, height: 400), .above, CGRect(x: 600, y: 488, width: 200, height: 32)),
            (CGRect(x: 0, y: 0, width: 1440, height: 900), .inside, CGRect(x: 620, y: 78, width: 200, height: 32)),
            (visible, .inside, CGRect(x: 620, y: 78, width: 200, height: 32)),
            (CGRect(x: 0, y: 300, width: 100, height: 200), .below, CGRect(x: 0, y: 260, width: 200, height: 32)),
            (CGRect(x: 1380, y: 300, width: 60, height: 200), .below, CGRect(x: 1240, y: 260, width: 200, height: 32)),
            (CGRect(x: 700, y: 500, width: 20, height: 10), .below, CGRect(x: 610, y: 460, width: 200, height: 32))
        ]
        for (rect, position, frame) in cases {
            let result = RecordingPillPlacement.place(pillSize: size, recordedRect: rect, visibleFrame: visible)
            #expect(result.position == position)
            #expect(result.frame == frame)
        }
    }

    @Test func negativeAndStackedDisplayOrigins() {
        let visible = CGRect(x: -1920, y: -200, width: 1920, height: 1055)
        let below = RecordingPillPlacement.place(pillSize: size,
            recordedRect: CGRect(x: -1000, y: 100, width: 400, height: 300), visibleFrame: visible)
        #expect(below.position == .below)
        #expect(below.frame == CGRect(x: -900, y: 60, width: 200, height: 32))
        let above = RecordingPillPlacement.place(pillSize: size,
            recordedRect: CGRect(x: -1920, y: -200, width: 500, height: 300), visibleFrame: visible)
        #expect(above.position == .above)
        #expect(above.frame == CGRect(x: -1770, y: 108, width: 200, height: 32))
        #expect(RecordingPillPlacement.place(pillSize: size,
            recordedRect: CGRect(x: -1920, y: 300, width: 50, height: 50), visibleFrame: visible).frame.minX == -1920)
        let stacked = RecordingPillPlacement.place(pillSize: size,
            recordedRect: CGRect(x: 500, y: 920, width: 400, height: 1030),
            visibleFrame: CGRect(x: 0, y: 900, width: 1920, height: 1055))
        #expect(stacked.position == .inside)
        #expect(stacked.frame == CGRect(x: 600, y: 928, width: 200, height: 32))
    }

    @Test func roundingAndOversizedPill() {
        let result = RecordingPillPlacement.place(pillSize: size,
            recordedRect: CGRect(x: 400.4, y: 300.3, width: 600, height: 400), visibleFrame: visible)
        #expect(result.frame.origin == CGPoint(x: 600, y: 260))
        let oversized = RecordingPillPlacement.place(pillSize: CGSize(width: 2000, height: 1000),
            recordedRect: visible, visibleFrame: visible)
        #expect(oversized.frame.origin == visible.origin)
    }

    @Test(arguments: [CGFloat(1), CGFloat(2)]) func placementUsesPointsOnEitherScale(scale: CGFloat) {
        let display = DisplayInfo(displayID: 2, frame: CGRect(x: -1920, y: -200, width: 1920, height: 1080), backingScale: scale)
        let region = DisplayGeometry.captureRegion(for: CGRect(x: -1000, y: 100, width: 400, height: 300), on: display)
        let result = RecordingPillPlacement.place(pillSize: size, recordedRect: region.appKitRect,
                                                 visibleFrame: display.frame)
        #expect(result.frame == CGRect(x: -900, y: 60, width: 200, height: 32))
    }

    @Test func widerElapsedLabelStaysClamped() {
        let rect = CGRect(x: 1380, y: 300, width: 60, height: 200)
        let result = RecordingPillPlacement.place(pillSize: CGSize(width: 260, height: 32), recordedRect: rect, visibleFrame: visible)
        #expect(result.frame.maxX == visible.maxX)
    }
    @Test func hiddenPillStaysPutUntilDisplayLossMakesItUnreachable() {
        let frame = CGRect(x: -900, y: 100, width: 200, height: 40)
        let left = CGRect(x: -1000, y: 0, width: 1000, height: 900)
        #expect(RecordingPillPlacement.reachableFrame(frame, in: left) == frame)
        let remaining = CGRect(x: 0, y: 20, width: 1000, height: 880)
        #expect(RecordingPillPlacement.reachableFrame(frame, in: remaining) ==
            CGRect(x: 0, y: 100, width: 200, height: 40))
        #expect(RecordingPillPlacement.reachableFrame(CGRect(x: 900, y: 1000, width: 200, height: 40), in: remaining) ==
            CGRect(x: 800, y: 860, width: 200, height: 40))
    }
}
