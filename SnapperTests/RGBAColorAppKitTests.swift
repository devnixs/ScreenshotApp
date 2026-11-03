import AppKit
import Testing
@testable import Snapper

struct RGBAColorAppKitTests {
    @Test func presetsRoundTrip() throws {
        for color in RGBAColor.presets {
            let result = try #require(RGBAColor(nsColor: color.nsColor))
            #expect(abs(result.red - color.red) < 1e-6)
            #expect(abs(result.green - color.green) < 1e-6)
            #expect(abs(result.blue - color.blue) < 1e-6)
            #expect(result.alpha == 1)
        }
    }
    @Test func patternColorsAreIgnored() {
        let pattern = NSColor(patternImage: NSImage(size: NSSize(width: 2, height: 2)))
        #expect(RGBAColor(nsColor: pattern) == nil)
    }
    @Test func conversionAndOpacity() throws {
        let input = NSColor(displayP3Red: 0.7, green: 0.4, blue: 0.2, alpha: 0.3)
        let result = try #require(RGBAColor(nsColor: input))
        let converted = try #require(input.usingColorSpace(.sRGB))
        #expect(abs(result.red - converted.redComponent) < 1e-6)
        #expect(abs(result.red - 0.7) > 1e-3)
        #expect(result.alpha == 1)
    }
}
