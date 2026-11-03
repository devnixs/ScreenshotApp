import CoreGraphics

protocol TextMeasuring {
    func layoutSize(of text: String, fontPixelSize: CGFloat) -> CGSize
    func lineHeight(fontPixelSize: CGFloat) -> CGFloat
}

extension AnnotationShape {
    func withStrokeWidth(_ width: StrokeWidth, pixelsPerPoint: CGFloat, measurer: any TextMeasuring) -> Self {
        var copy = withStrokeWidth(width)
        if case .text(var box) = kind {
            box.measuredSize = measurer.layoutSize(of: box.string,
                fontPixelSize: TextMetrics.fontPixelSize(for: width, pixelsPerPoint: pixelsPerPoint))
            copy.kind = .text(box)
        }
        return copy
    }
}
