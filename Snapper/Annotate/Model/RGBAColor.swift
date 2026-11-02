import CoreGraphics

struct RGBAColor: Codable, Equatable, Hashable, Sendable {
    let red: Double
    let green: Double
    let blue: Double
    let alpha: Double

    init(red: Double, green: Double, blue: Double, alpha: Double = 1) {
        self.red = Self.clamp(red)
        self.green = Self.clamp(green)
        self.blue = Self.clamp(blue)
        self.alpha = Self.clamp(alpha, fallback: 1)
    }

    private static func clamp(_ value: Double, fallback: Double = 0) -> Double {
        value.isFinite ? min(1, max(0, value)) : fallback
    }

    private enum CodingKeys: String, CodingKey {
        case red, green, blue, alpha
    }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        func component(_ key: CodingKeys) throws -> Double {
            guard container.contains(key) else {
                throw DecodingError.dataCorruptedError(forKey: key, in: container, debugDescription: "Missing color component")
            }
            let value = try container.decode(Double.self, forKey: key)
            guard value.isFinite, (0...1).contains(value) else {
                throw DecodingError.dataCorruptedError(forKey: key, in: container, debugDescription: "Color component must be in 0...1")
            }
            return value
        }
        red = try component(.red)
        green = try component(.green)
        blue = try component(.blue)
        alpha = try component(.alpha)
    }

    var cgColor: CGColor {
        CGColor(srgbRed: red, green: green, blue: blue, alpha: alpha)
    }

    init?(cgColor: CGColor) {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let converted = cgColor.converted(to: space, intent: .defaultIntent, options: nil),
              let components = converted.components, components.count == 4 else { return nil }
        self.init(red: Double(components[0]), green: Double(components[1]),
                  blue: Double(components[2]), alpha: Double(components[3]))
    }

    static var red: Self { ColorPreset.red.color }
    static var orange: Self { ColorPreset.orange.color }
    static var yellow: Self { ColorPreset.yellow.color }
    static var green: Self { ColorPreset.green.color }
    static var blue: Self { ColorPreset.blue.color }
    static var purple: Self { ColorPreset.purple.color }
    static var black: Self { ColorPreset.black.color }
    static var white: Self { ColorPreset.white.color }
    static let presets = ColorPreset.allCases.map(\.color)
}

enum ColorPreset: String, CaseIterable, Sendable {
    case red, orange, yellow, green, blue, purple, black, white

    var displayName: String { rawValue.capitalized }

    var color: RGBAColor {
        switch self {
        case .red: RGBAColor(red: 255 / 255, green: 59 / 255, blue: 48 / 255)
        case .orange: RGBAColor(red: 255 / 255, green: 149 / 255, blue: 0)
        case .yellow: RGBAColor(red: 255 / 255, green: 204 / 255, blue: 0)
        case .green: RGBAColor(red: 52 / 255, green: 199 / 255, blue: 89 / 255)
        case .blue: RGBAColor(red: 0, green: 122 / 255, blue: 255 / 255)
        case .purple: RGBAColor(red: 175 / 255, green: 82 / 255, blue: 222 / 255)
        case .black: RGBAColor(red: 0, green: 0, blue: 0)
        case .white: RGBAColor(red: 255 / 255, green: 255 / 255, blue: 255 / 255)
        }
    }
}
