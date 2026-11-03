import CoreGraphics

enum RecordingBorderGeometry {
    static let outset: CGFloat = 3
    static let strokeInset: CGFloat = 1.5

    static func windowFrame(recordedRect: CGRect, displayFrame: CGRect) -> CGRect {
        recordedRect.insetBy(dx: -outset, dy: -outset).intersection(displayFrame)
    }

    static func strokeRect(windowSize: CGSize) -> CGRect {
        CGRect(origin: .zero, size: windowSize).insetBy(dx: strokeInset, dy: strokeInset)
    }
}
