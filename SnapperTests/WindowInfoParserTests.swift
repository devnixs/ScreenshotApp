import Foundation
import CoreGraphics
import Testing
@testable import Snapper

struct WindowInfoParserTests {
    @Test func unavailableVersusGone() {
        #expect(WindowInfoParser.parse(nil, windowID: 42) == .unavailable)
        #expect(WindowInfoParser.parse([], windowID: 42) == .gone)
        #expect(WindowInfoParser.parse([[kCGWindowNumber as String: 1]], windowID: 42) == .gone)
    }

    @Test func matchingWindowBoundsAndVisibility() {
        let bounds = CGRect(x: -1820, y: -80, width: 800, height: 600)
        var entry: [String: Any] = [kCGWindowNumber as String: 42,
                                    kCGWindowBounds as String: bounds.dictionaryRepresentation]
        #expect(WindowInfoParser.parse([entry], windowID: 42) == .present(cgBounds: bounds, isOnscreen: false))
        entry[kCGWindowIsOnscreen as String] = true
        #expect(WindowInfoParser.parse([[kCGWindowNumber as String: 1], entry], windowID: 42) == .present(cgBounds: bounds, isOnscreen: true))
    }

    @Test func malformedBoundsAreTransient() {
        for value: Any in ["broken", ["X": 10], NSNull()] {
            #expect(WindowInfoParser.parse([[kCGWindowNumber as String: 42, kCGWindowBounds as String: value]], windowID: 42) == .unavailable)
        }
        #expect(WindowInfoParser.parse([[kCGWindowNumber as String: 42]], windowID: 42) == .unavailable)
    }
}
