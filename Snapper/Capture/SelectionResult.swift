import CoreGraphics

enum SelectionResult: Equatable, Sendable {
    /// AppKit-global points, clamped to this display, with both dimensions at least 4.
    case rect(display: DisplayInfo, rect: CGRect)
}
