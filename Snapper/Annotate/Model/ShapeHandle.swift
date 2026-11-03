import CoreGraphics

/// Corners are named in top-left-origin, y-down image pixel space.
enum ShapeHandle: CaseIterable, Equatable, Sendable {
    case topLeft, topRight, bottomLeft, bottomRight, start, end

    var opposite: Self {
        switch self {
        case .topLeft: .bottomRight
        case .topRight: .bottomLeft
        case .bottomLeft: .topRight
        case .bottomRight: .topLeft
        case .start: .end
        case .end: .start
        }
    }
}
