import CoreGraphics
import Testing
@testable import Snapper

struct WindowDisplayRuleTests {
    private let primary = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1440, height: 900), backingScale: 2)
    private let secondary = DisplayInfo(displayID: 2, frame: CGRect(x: -1920, y: -180, width: 1920, height: 1080), backingScale: 1)

    private func choose(_ frame: CGRect, displays: [DisplayInfo]? = nil) -> DisplayInfo? {
        WindowDisplayRule.display(for: frame, displays: displays ?? [primary, secondary], primaryDisplayHeight: 900)
    }

    @Test func fullyOnPrimaryOrNegativeOriginSecondary() {
        #expect(choose(CGRect(x: 100, y: 100, width: 500, height: 300)) == primary)
        #expect(DisplayGeometry.cgRect(fromAppKit: secondary.frame, primaryHeight: 900) ==
                CGRect(x: -1920, y: 0, width: 1920, height: 1080))
        #expect(choose(CGRect(x: -1800, y: 800, width: 400, height: 200)) == secondary)
    }

    @Test func spanningWindowPicksLargestOverlap() {
        #expect(choose(CGRect(x: -700, y: 100, width: 1000, height: 500)) == secondary)
        #expect(choose(CGRect(x: -300, y: 100, width: 1000, height: 500)) == primary)
    }

    @Test func exactTieGoesToFirstDisplay() {
        let window = CGRect(x: -500, y: 100, width: 1000, height: 500)
        #expect(choose(window) == primary)
        #expect(choose(window, displays: [secondary, primary]) == secondary)
    }

    @Test func noIntersectionPicksNearestDisplayAndDistanceTieIsStable() {
        #expect(choose(CGRect(x: -2100, y: 200, width: 100, height: 100)) == secondary)
        #expect(choose(CGRect(x: 1800, y: 1200, width: 100, height: 100)) == primary)
        let same = DisplayInfo(displayID: 3, frame: primary.frame, backingScale: 1)
        #expect(choose(CGRect(x: 2000, y: 100, width: 100, height: 100), displays: [same, primary]) == same)
        #expect(choose(.zero, displays: []) == nil)
    }

    @Test func abovePrimaryAndScaleDoNotChangePointBasedChoice() {
        let above = DisplayInfo(displayID: 3, frame: CGRect(x: 100, y: 900, width: 1000, height: 700), backingScale: 1)
        #expect(choose(CGRect(x: 200, y: -600, width: 500, height: 200), displays: [primary, above]) == above)
        #expect(choose(CGRect(x: -1800, y: 800, width: 400, height: 200), displays: [primary, secondary])?.backingScale == 1)
    }
}
