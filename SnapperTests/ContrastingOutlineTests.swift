import Foundation
import Testing
@testable import Snapper

struct ContrastingOutlineTests {
    @Test func luminanceAndPresets() {
        for (color, luminance) in [(RGBAColor.black, 0.0), (.white, 1.0),
            (RGBAColor(red: 1, green: 0, blue: 0), 0.2126),
            (RGBAColor(red: 0, green: 1, blue: 0), 0.7152),
            (RGBAColor(red: 0, green: 0, blue: 1), 0.0722)] {
            #expect(abs(ContrastingOutline.relativeLuminance(of: color) - luminance) < 1e-9)
        }
        for preset in ColorPreset.allCases {
            #expect(ContrastingOutline.color(for: preset.color) == ([ColorPreset.yellow, .white].contains(preset) ? .black : .white))
        }
    }
    @Test func thresholdAlphaClampingAndThickness() {
        func gray(_ luminance: Double) -> RGBAColor {
            let channel = 1.055 * pow(luminance, 1 / 2.4) - 0.055
            return RGBAColor(red: channel, green: channel, blue: channel)
        }
        #expect(ContrastingOutline.color(for: gray(0.49999)) == .white)
        #expect(ContrastingOutline.color(for: gray(0.50001)) == .black)
        #expect(ContrastingOutline.color(for: RGBAColor(red: 1.2, green: -1, blue: 0, alpha: 0.4)) == RGBAColor(red: 1, green: 1, blue: 1, alpha: 0.4))
        #expect(ContrastingOutline.strokeWidthPercent == 2 * ContrastingOutline.outlineFractionOfFontSize * 100)
    }
}
