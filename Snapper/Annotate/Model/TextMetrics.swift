import CoreGraphics

enum TextMetrics {
    static func fontPixelSize(for width: StrokeWidth, pixelsPerPoint: CGFloat) -> CGFloat {
        width.textPointSize * pixelsPerPoint
    }
    static func editorFontSize(fontPixelSize: CGFloat, canvasScale: CGFloat) -> CGFloat { fontPixelSize * canvasScale }
    static func newBoxOrigin(click: CGPoint, lineHeight: CGFloat, imageSize: CGSize) -> CGPoint {
        CGPoint(x: min(max(0, click.x), max(0, imageSize.width.nextDown)),
                y: min(max(0, click.y - lineHeight / 2), max(0, imageSize.height - lineHeight)))
    }
}
