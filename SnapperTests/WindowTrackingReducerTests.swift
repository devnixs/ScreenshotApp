import CoreGraphics
import Testing
@testable import Snapper

struct WindowTrackingReducerTests {
    private let initial = WindowTrackingState(appKitFrame: CGRect(x: 100, y: 200, width: 800, height: 600))

    @Test func unchangedAndJitter() {
        for delta: CGFloat in [0, 0.3] {
            let (state, event) = WindowTrackingReducer.reduce(initial,
                poll: .present(cgBounds: CGRect(x: 100 + delta, y: 100 + delta, width: 800, height: 600), isOnscreen: true),
                primaryDisplayHeight: 900)
            #expect(state == initial)
            #expect(event == .unchanged)
        }
    }

    @Test func movementAcrossDisplayOrigins() {
        let samples: [(CGRect, CGRect)] = [
            (CGRect(x: 100, y: 100, width: 800, height: 600), CGRect(x: 100, y: 200, width: 800, height: 600)),
            (CGRect(x: -1820, y: -80, width: 800, height: 600), CGRect(x: -1820, y: 380, width: 800, height: 600)),
            (CGRect(x: 100, y: 1000, width: 400, height: 300), CGRect(x: 100, y: -400, width: 400, height: 300))
        ]
        for (cg, expected) in samples {
            let (state, event) = WindowTrackingReducer.reduce(WindowTrackingState(appKitFrame: .zero),
                poll: .present(cgBounds: cg, isOnscreen: true), primaryDisplayHeight: 900)
            #expect(state.appKitFrame == expected)
            #expect(event == .moved(expected))
        }
    }

    @Test func unavailableAndClosureIsEmittedOnce() {
        let (same, event) = WindowTrackingReducer.reduce(initial, poll: .unavailable, primaryDisplayHeight: 900)
        #expect(same == initial)
        #expect(event == .unchanged)
        let (closed, closeEvent) = WindowTrackingReducer.reduce(initial, poll: .gone, primaryDisplayHeight: 900)
        #expect(closed.isClosed)
        #expect(closeEvent == .closed)
        for poll: WindowPollResult in [.gone, .unavailable, .present(cgBounds: .zero, isOnscreen: true)] {
            let (next, event) = WindowTrackingReducer.reduce(closed, poll: poll, primaryDisplayHeight: 900)
            #expect(next == closed)
            #expect(event == .unchanged)
        }
    }

    @Test func hidingAndRestoringTracksLatestFrame() {
        let bounds = CGRect(x: 100, y: 100, width: 800, height: 600)
        let (hidden, event) = WindowTrackingReducer.reduce(initial, poll: .present(cgBounds: bounds, isOnscreen: false), primaryDisplayHeight: 900)
        #expect(event == .hidden)
        #expect(!hidden.isVisible)
        #expect(!hidden.isClosed)
        let (stillHidden, unchanged) = WindowTrackingReducer.reduce(hidden, poll: .present(cgBounds: bounds, isOnscreen: false), primaryDisplayHeight: 900)
        #expect(unchanged == .unchanged)
        let moved = CGRect(x: 200, y: 100, width: 400, height: 300)
        let (shown, shownEvent) = WindowTrackingReducer.reduce(stillHidden, poll: .present(cgBounds: moved, isOnscreen: true), primaryDisplayHeight: 900)
        #expect(shown.isVisible)
        #expect(shownEvent == .shown(CGRect(x: 200, y: 500, width: 400, height: 300)))
    }

    @Test func largestOverlapAndOffscreenFallback() {
        let left = DisplayInfo(displayID: 1, frame: CGRect(x: -1000, y: 0, width: 1000, height: 900), backingScale: 1)
        let right = DisplayInfo(displayID: 2, frame: CGRect(x: 0, y: 0, width: 1000, height: 900), backingScale: 2)
        #expect(DisplayGeometry.display(overlapping: CGRect(x: -300, y: 100, width: 1000, height: 600), in: [left, right]) == right)
        #expect(DisplayGeometry.display(overlapping: CGRect(x: -700, y: 100, width: 1000, height: 600), in: [left, right]) == left)
        #expect(DisplayGeometry.display(overlapping: CGRect(x: 3000, y: 0, width: 100, height: 100), in: [left, right]) == nil)
        #expect(DisplayGeometry.display(overlapping: CGRect(x: 1000, y: 0, width: 100, height: 100), in: [left, right]) == nil)
    }
}
