import CoreGraphics

enum SelectionResult: Equatable, Sendable {
    /// AppKit-global points, clamped to this display, with both dimensions at least 4.
    case rect(display: DisplayInfo, rect: CGRect)
    /// Whole window frame in CG-global points; display is chosen by largest overlap.
    case window(windowID: CGWindowID, frame: CGRect, display: DisplayInfo)
}
