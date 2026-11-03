import CoreGraphics
import Testing
@testable import Snapper

struct SelectionSessionTests {
    private let display = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1000, height: 800), backingScale: 2)
    private let start = CGPoint(x: 100, y: 100)
    private let end = CGPoint(x: 300, y: 200)

    @Test func startsInRectangleModeAndTogglesBothWays() {
        var session = SelectionSession()
        #expect(session.mode == .rectangle)
        #expect(!session.isMouseDown && !session.isPressStale && !session.isWindowClickArmed)
        session.toggleMode()
        #expect(session.mode == .window)
        session.toggleMode()
        #expect(session.mode == .rectangle)
    }

    @Test func toggleDuringDragDiscardsDragAndStaleMotion() {
        var session = SelectionSession()
        session.mouseDown(at: start, displays: [display])
        session.mouseDragged(to: end)
        #expect(session.tracker.currentRect != nil)
        session.toggleMode()
        #expect(session.tracker.currentRect == nil)
        #expect(session.isPressStale)
        session.toggleMode()
        session.mouseDragged(to: end)
        #expect(session.tracker.currentRect == nil)
        #expect(session.mouseUp(at: end) == .none)
        #expect(!session.isPressStale && !session.isMouseDown)
        session.mouseDown(at: start, displays: [display])
        session.mouseDragged(to: end)
        #expect(session.mouseUp(at: end) == .rectangle(.rect(display: display, rect: CGRect(x: 100, y: 100, width: 200, height: 100))))
    }

    @Test func stalePressDragIsIgnoredAfterToggleBack() {
        var session = SelectionSession()
        session.mouseDown(at: start, displays: [display])
        session.toggleMode()
        session.toggleMode()
        session.mouseDragged(to: end)
        #expect(session.mouseUp(at: end) == .none)
    }

    @Test func windowClickReturnsReleasePointAndDisarms() {
        var session = SelectionSession()
        session.toggleMode()
        #expect(session.mouseUp(at: start) == .none)
        session.mouseDown(at: start, displays: [display])
        session.mouseDragged(to: end)
        #expect(session.tracker.currentRect == nil)
        #expect(session.mouseUp(at: end) == .windowClick(at: end))
        #expect(!session.isWindowClickArmed)
        #expect(session.mouseUp(at: end) == .none)
    }

    @Test func rectanglePressReleasedInWindowModeDoesNotClick() {
        var session = SelectionSession()
        session.mouseDown(at: start, displays: [display])
        session.toggleMode()
        #expect(session.mouseUp(at: end) == .none)
        session.mouseDown(at: end, displays: [display])
        #expect(session.mouseUp(at: end) == .windowClick(at: end))
    }

    @Test func windowPressReleasedInRectangleModeDoesNothing() {
        var session = SelectionSession()
        session.toggleMode()
        session.mouseDown(at: start, displays: [display])
        session.toggleMode()
        #expect(session.mouseUp(at: end) == .none)
    }

    @Test func clicksAndBelowThresholdDragsStayOpen() {
        var session = SelectionSession()
        #expect(session.mouseUp(at: start) == .none)
        for point in [start, CGPoint(x: 103, y: 200), CGPoint(x: 200, y: 103)] {
            session.mouseDown(at: start, displays: [display])
            #expect(session.mouseUp(at: point) == .none)
        }
    }

    @Test func rectangleStillClampsToStartingNegativeOriginDisplayAtOneTimesScale() {
        let left = DisplayInfo(displayID: 2, frame: CGRect(x: -1000, y: -200, width: 1000, height: 800), backingScale: 1)
        var session = SelectionSession()
        session.toggleMode()
        session.toggleMode()
        session.mouseDown(at: CGPoint(x: -100, y: 100), displays: [display, left])
        #expect(session.mouseUp(at: CGPoint(x: 100, y: -300)) ==
                .rectangle(.rect(display: left, rect: CGRect(x: -100, y: -200, width: 100, height: 300))))
    }
}
