import CoreGraphics
import Testing
@testable import Snapper

struct RectSelectionTrackerTests {
    private let main = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1440, height: 900), backingScale: 2)
    private let right = DisplayInfo(displayID: 2, frame: CGRect(x: 1440, y: 0, width: 1920, height: 1080), backingScale: 1)
    private let leftBelow = DisplayInfo(displayID: 3, frame: CGRect(x: -1920, y: -200, width: 1920, height: 1080), backingScale: 1)

    @Test func forwardAndReverseDrags() {
        for (start, end) in [(CGPoint(x: 100, y: 100), CGPoint(x: 400, y: 300)),
                             (CGPoint(x: 400, y: 300), CGPoint(x: 100, y: 100))] {
            var tracker = RectSelectionTracker()
            tracker.mouseDown(at: start, displays: [main, right])
            #expect(tracker.currentRect == CGRect(origin: start, size: .zero))
            #expect(tracker.mouseUp(at: end) == .selected(.rect(display: main, rect: CGRect(x: 100, y: 100, width: 300, height: 200))))
            #expect(tracker.currentRect == nil)
            #expect(tracker.currentPoint == nil)
            #expect(tracker.startDisplay == nil)
        }
    }

    @Test func crossDisplayClamping() {
        var tracker = RectSelectionTracker()
        tracker.mouseDown(at: CGPoint(x: 1000, y: 500), displays: [main, right])
        let rect = CGRect(x: 1000, y: 500, width: 440, height: 300)
        #expect(tracker.drag(to: CGPoint(x: 2000, y: 800)) == rect)
        #expect(tracker.currentPoint == CGPoint(x: 1440, y: 800))
        #expect(tracker.startDisplay == main)
        #expect(tracker.mouseUp(at: CGPoint(x: 2000, y: 800)) == .selected(.rect(display: main, rect: rect)))
    }

    @Test func verticalClamping() {
        for (end, expected) in [
            (CGPoint(x: 300, y: -50), CGRect(x: 100, y: 0, width: 200, height: 100)),
            (CGPoint(x: 300, y: 1000), CGRect(x: 100, y: 100, width: 200, height: 800))
        ] {
            var tracker = RectSelectionTracker()
            tracker.mouseDown(at: CGPoint(x: 100, y: 100), displays: [main])
            #expect(tracker.mouseUp(at: end) == .selected(.rect(display: main, rect: expected)))
        }
    }

    @Test func startDisplayStaysFixedAtOneTimesScale() {
        var tracker = RectSelectionTracker()
        tracker.mouseDown(at: CGPoint(x: 1500, y: 100), displays: [main, right])
        let rect = CGRect(x: 1440, y: 100, width: 60, height: 400)
        #expect(tracker.drag(to: CGPoint(x: 500, y: 500)) == rect)
        #expect(tracker.startDisplay == right)
        #expect(tracker.mouseUp(at: CGPoint(x: 500, y: 500)) == .selected(.rect(display: right, rect: rect)))
    }

    @Test func negativeOrigin() {
        var tracker = RectSelectionTracker()
        tracker.mouseDown(at: CGPoint(x: -100, y: 100), displays: [main, leftBelow])
        #expect(tracker.mouseUp(at: CGPoint(x: 50, y: -500)) ==
                .selected(.rect(display: leftBelow, rect: CGRect(x: -100, y: -200, width: 100, height: 300))))
    }

    @Test func minimumDimensionsAndClick() {
        for end in [CGPoint(x: 103.9, y: 110), CGPoint(x: 110, y: 103.9), CGPoint(x: 100, y: 100)] {
            var tracker = RectSelectionTracker()
            tracker.mouseDown(at: CGPoint(x: 100, y: 100), displays: [main])
            #expect(tracker.mouseUp(at: end) == .tooSmall)
            #expect(tracker.currentRect == nil)
            tracker.mouseDown(at: CGPoint(x: 100, y: 100), displays: [main])
            #expect(tracker.mouseUp(at: CGPoint(x: 104, y: 104)) ==
                    .selected(.rect(display: main, rect: CGRect(x: 100, y: 100, width: 4, height: 4))))
        }
    }

    @Test func thresholdUsesClampedRect() {
        var tracker = RectSelectionTracker()
        tracker.mouseDown(at: CGPoint(x: 1438, y: 100), displays: [main, right])
        #expect(tracker.mouseUp(at: CGPoint(x: 1600, y: 300)) == .tooSmall)
    }

    @Test func idleResetAndEmptyDisplays() {
        var tracker = RectSelectionTracker()
        #expect(tracker.drag(to: .zero) == nil)
        #expect(tracker.mouseUp(at: .zero) == .ignored)
        tracker.mouseDown(at: CGPoint(x: 100, y: 100), displays: [main])
        tracker.reset()
        #expect(tracker.currentRect == nil)
        #expect(tracker.currentPoint == nil)
        #expect(tracker.startDisplay == nil)
        #expect(tracker.drag(to: CGPoint(x: 500, y: 500)) == nil)
        #expect(tracker.mouseUp(at: CGPoint(x: 500, y: 500)) == .ignored)
        tracker.mouseDown(at: .zero, displays: [])
        #expect(tracker.mouseUp(at: .zero) == .ignored)
        tracker.mouseDown(at: CGPoint(x: 10, y: 10), displays: [main])
        tracker.mouseDown(at: .zero, displays: [])
        #expect(tracker.currentRect == nil)
    }

    @Test func outerEdgeAndNearestDisplayFallback() {
        var tracker = RectSelectionTracker()
        tracker.mouseDown(at: CGPoint(x: 1440, y: 900), displays: [main])
        #expect(tracker.startDisplay == main)
        #expect(tracker.currentPoint == CGPoint(x: 1440, y: 900))
        #expect(tracker.mouseUp(at: CGPoint(x: 1300, y: 800)) ==
                .selected(.rect(display: main, rect: CGRect(x: 1300, y: 800, width: 140, height: 100))))
        // Above the desktop: nearest is right, even though main is first.
        tracker.mouseDown(at: CGPoint(x: 3000, y: 1200), displays: [main, right])
        #expect(tracker.startDisplay == right)
        #expect(tracker.currentPoint == CGPoint(x: 3000, y: 1080))
        #expect(tracker.mouseUp(at: CGPoint(x: 2900, y: 1000)) ==
                .selected(.rect(display: right, rect: CGRect(x: 2900, y: 1000, width: 100, height: 80))))
    }

    @Test func sharedEdgeUsesDisplayGeometryOwnership() {
        var tracker = RectSelectionTracker()
        tracker.mouseDown(at: CGPoint(x: 1440, y: 100), displays: [main, right])
        #expect(tracker.startDisplay == right)
    }
}
