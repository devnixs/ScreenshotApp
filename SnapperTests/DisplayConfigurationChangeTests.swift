import CoreGraphics
import Testing
@testable import Snapper

struct DisplayConfigurationChangeTests {
    private let main = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1440, height: 900), backingScale: 2)
    private let other = DisplayInfo(displayID: 2, frame: CGRect(x: -1920, y: -200, width: 1920, height: 1080), backingScale: 1)

    @Test func unchangedAndReorderedDisplays() {
        #expect(!DisplayConfigurationChange.requiresCancel(old: [main, other], new: [main, other]))
        #expect(!DisplayConfigurationChange.requiresCancel(old: [main, other], new: [other, main]))
        #expect(!DisplayConfigurationChange.requiresCancel(old: [], new: []))
    }

    @Test func connectionDisconnectionAndReplacement() {
        #expect(DisplayConfigurationChange.requiresCancel(old: [main], new: [main, other]))
        #expect(DisplayConfigurationChange.requiresCancel(old: [main, other], new: [main]))
        #expect(DisplayConfigurationChange.requiresCancel(old: [main], new: []))
        #expect(DisplayConfigurationChange.requiresCancel(old: [], new: [main]))
        let replacement = DisplayInfo(displayID: 3, frame: other.frame, backingScale: other.backingScale)
        #expect(DisplayConfigurationChange.requiresCancel(old: [main, other], new: [main, replacement]))
    }

    @Test func arrangementResolutionAndScaleChanges() {
        for changed in [
            DisplayInfo(displayID: other.displayID, frame: other.frame.offsetBy(dx: 10, dy: -20), backingScale: 1),
            DisplayInfo(displayID: other.displayID, frame: CGRect(x: -1920, y: -200, width: 1280, height: 720), backingScale: 1),
            DisplayInfo(displayID: main.displayID, frame: main.frame, backingScale: 1)
        ] {
            let old = changed.displayID == main.displayID ? main : other
            #expect(DisplayConfigurationChange.requiresCancel(old: [old], new: [changed]))
        }
    }
}
