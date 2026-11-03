import CoreGraphics
import Testing
@testable import Snapper

struct WindowHitTesterTests {
    private func candidate(_ id: CGWindowID = 1, frame: CGRect = CGRect(x: 10, y: 10, width: 100, height: 100),
                           layer: Int = 0, pid: pid_t? = 20, onScreen: Bool = true, alpha: Double = 1) -> WindowCandidate {
        WindowCandidate(windowID: id, frame: frame, layer: layer, ownerPID: pid, ownerBundleID: nil,
                        isOnScreen: onScreen, alpha: alpha, title: nil)
    }

    @Test func topmostWindowWins() {
        let front = candidate(1)
        let back = candidate(2, frame: CGRect(x: 50, y: 50, width: 200, height: 200))
        let tester = WindowHitTester(candidates: [front, back], excludedPID: 99)
        #expect(tester.window(at: CGPoint(x: 60, y: 60)) == front)
        #expect(tester.window(at: CGPoint(x: 200, y: 200)) == back)
        #expect(tester.window(at: .zero) == nil)
    }

    @Test func nonSelectableWindowsAreSkippedNotBlocking() {
        let back = candidate(10)
        let excluded = [candidate(pid: 99), candidate(layer: 25), candidate(layer: 20), candidate(pid: nil),
                        candidate(onScreen: false), candidate(alpha: 0), candidate(alpha: -1)]
        for front in excluded {
            let tester = WindowHitTester(candidates: [front, back], excludedPID: 99)
            #expect(!tester.isSelectable(front))
            #expect(tester.window(at: CGPoint(x: 50, y: 50)) == back)
            #expect(tester.window(at: CGPoint(x: 500, y: 500)) == nil)
        }
    }

    @Test func zeroSizeTinyAndInvisibleExcluded() {
        let tester = WindowHitTester(candidates: [], excludedPID: 99)
        for frame in [CGRect.zero, CGRect(x: 0, y: 0, width: 2, height: 100),
                      CGRect(x: 0, y: 0, width: 100, height: 3.99)] {
            #expect(!tester.isSelectable(candidate(frame: frame)))
        }
        #expect(tester.isSelectable(candidate(frame: CGRect(x: 0, y: 0, width: 4, height: 4), alpha: 0.01)))
    }

    @Test func containsUsesHalfOpenEdges() {
        let tester = WindowHitTester(candidates: [candidate()], excludedPID: 99)
        #expect(tester.window(at: CGPoint(x: 10, y: 10)) != nil)
        #expect(tester.window(at: CGPoint(x: 110, y: 50)) == nil)
        #expect(tester.window(at: CGPoint(x: 50, y: 110)) == nil)
    }

    @Test func negativeCGCoordinates() {
        let window = candidate(frame: CGRect(x: -1920, y: -300, width: 800, height: 600))
        let tester = WindowHitTester(candidates: [window], excludedPID: 99)
        #expect(tester.window(at: CGPoint(x: -1500, y: 0)) == window)
        #expect(tester.window(at: CGPoint(x: -1000, y: 0)) == nil)
    }

    @Test func appKitCursorConversionOnNegativeOriginsAndAbovePrimary() {
        let primaryHeight: CGFloat = 900
        for frame in [CGRect(x: -500, y: 1000, width: 200, height: 100),
                      CGRect(x: 100, y: -400, width: 200, height: 100)] {
            let cgFrame = DisplayGeometry.cgRect(fromAppKit: frame, primaryHeight: primaryHeight)
            let window = candidate(frame: cgFrame)
            let tester = WindowHitTester(candidates: [window], excludedPID: 99)
            let cursor = CGPoint(x: frame.midX, y: frame.midY)
            #expect(tester.window(at: DisplayGeometry.cgPoint(fromAppKit: cursor, primaryHeight: primaryHeight)) == window)
        }
    }
}
