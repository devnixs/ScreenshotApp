import Testing
@testable import Snapper

struct DockPresenceTests {
    private final class Token {}
    private let a = Token(), b = Token(), c = Token()
    private var idA: ObjectIdentifier { ObjectIdentifier(a) }
    private var idB: ObjectIdentifier { ObjectIdentifier(b) }
    private var idC: ObjectIdentifier { ObjectIdentifier(c) }

    @Test func startsAccessory() {
        let presence = DockPresence()
        #expect(!presence.wantsRegularApp)
        #expect(presence.openWindows.isEmpty)
    }

    @Test func firstOpenBecomesRegular() {
        var presence = DockPresence()
        #expect(presence.windowOpened(idA) == .becomeRegular)
        #expect(presence.wantsRegularApp)
    }

    @Test func secondOpenIsNoChange() {
        var presence = DockPresence()
        _ = presence.windowOpened(idA)
        #expect(presence.windowOpened(idB) == nil)
        #expect(presence.openWindows == [idA, idB])
    }

    @Test func reopenSameWindowIsNoChange() {
        var presence = DockPresence()
        #expect(presence.windowOpened(idA) == .becomeRegular)
        #expect(presence.windowOpened(idA) == nil)
        #expect(presence.openWindows.count == 1)
    }

    @Test func closingOneOfTwoIsNoChange() {
        var presence = DockPresence()
        _ = presence.windowOpened(idA)
        _ = presence.windowOpened(idB)
        #expect(presence.windowClosed(idA) == nil)
        #expect(presence.wantsRegularApp)
    }

    @Test func closingLastBecomesAccessory() {
        var presence = DockPresence()
        _ = presence.windowOpened(idA)
        _ = presence.windowOpened(idB)
        #expect(presence.windowClosed(idA) == nil)
        #expect(presence.windowClosed(idB) == .becomeAccessory)
        #expect(!presence.wantsRegularApp)
    }

    @Test func closingUntrackedIsNoChange() {
        var empty = DockPresence()
        #expect(empty.windowClosed(idC) == nil)
        #expect(empty == DockPresence())

        var presence = DockPresence()
        _ = presence.windowOpened(idA)
        let before = presence
        #expect(presence.windowClosed(idC) == nil)
        #expect(presence == before)
        #expect(presence.wantsRegularApp)
    }

    @Test func closingTwiceIsNoChange() {
        var presence = DockPresence()
        _ = presence.windowOpened(idA)
        #expect(presence.windowClosed(idA) == .becomeAccessory)
        #expect(presence.windowClosed(idA) == nil)
    }

    @Test func reopenAfterAllClosedBecomesRegularAgain() {
        var presence = DockPresence()
        _ = presence.windowOpened(idA)
        _ = presence.windowClosed(idA)
        #expect(presence.windowOpened(idA) == .becomeRegular)
    }

    @Test(arguments: [
        (true, false, DockReopenAction.bringToFront),
        (false, true, DockReopenAction.deminiaturizeOne),
        (false, false, DockReopenAction.nothing),
        (true, true, DockReopenAction.bringToFront)
    ])
    func reopenDecision(visible: Bool, minimized: Bool, expected: DockReopenAction) {
        #expect(DockReopenDecision.action(hasVisibleWindows: visible,
                                          hasMinimizedTrackedWindow: minimized) == expected)
    }
}
