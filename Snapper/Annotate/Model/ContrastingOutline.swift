import Foundation

enum ContrastingOutline {
    static let outlineFractionOfFontSize = 0.0625
    static let strokeWidthPercent = 12.5
    static func relativeLuminance(of color: RGBAColor) -> Double {
        func linear(_ c: Double) -> Double {
            let c = min(1, max(0, c))
            return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)
        }
        return 0.2126 * linear(color.red) + 0.7152 * linear(color.green) + 0.0722 * linear(color.blue)
    }
    static func color(for color: RGBAColor) -> RGBAColor {
        let channel: Double = relativeLuminance(of: color) >= 0.5 ? 0 : 1
        return RGBAColor(red: channel, green: channel, blue: channel, alpha: color.alpha)
    }
}
