import CoreGraphics

enum StrokeWidth: String, CaseIterable, Codable, Sendable {
    case s, m, l

    var points: CGFloat {
        switch self {
        case .s: 2
        case .m: 4
        case .l: 8
        }
    }

    var textPointSize: CGFloat {
        switch self {
        case .s: 16
        case .m: 24
        case .l: 36
        }
    }

    var label: String { rawValue.uppercased() }
}
