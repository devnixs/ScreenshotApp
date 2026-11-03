import CoreGraphics

enum AnnotationWindowSizing {
    static func frame(imagePixelSize: CGSize, pixelsPerPoint: CGFloat, screenVisibleFrame: CGRect,
                      chromeHeight: CGFloat, minContentSize: CGSize) -> (contentSize: CGSize, windowFrame: CGRect) {
        let visible = screenVisibleFrame
        let chrome = max(0, chromeHeight)
        let availableHeight = max(0, visible.height - chrome)
        let image = CGSize(width: imagePixelSize.width / pixelsPerPoint, height: imagePixelSize.height / pixelsPerPoint)
        let fit = max(0, min(1, visible.width * 0.8 / image.width, (visible.height * 0.8 - chrome) / image.height))
        let size = CGSize(width: min(visible.width, max(minContentSize.width, image.width * fit)).rounded(.down),
                          height: min(availableHeight, max(minContentSize.height, image.height * fit)).rounded(.down))
        let frame = CGRect(x: (visible.midX - size.width / 2).rounded(),
                           y: (visible.midY - (size.height + chrome) / 2).rounded(),
                           width: size.width, height: size.height + chrome)
        return (size, frame)
    }

    /// Existing origins are window top-left points, in AppKit global coordinates.
    static func cascaded(_ frame: CGRect, avoiding existingOrigins: [CGPoint], within visibleFrame: CGRect) -> CGRect {
        var result = frame
        while existingOrigins.contains(CGPoint(x: result.minX, y: result.maxY)) {
            let next = result.offsetBy(dx: 24, dy: -24)
            guard visibleFrame.contains(next) else { break }
            result = next
        }
        return result
    }
}
