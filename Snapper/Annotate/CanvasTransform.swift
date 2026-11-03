import CoreGraphics

/// Canvas and image pixels both use a top-left origin, y down; no screen-space Y flip applies.
struct CanvasTransform: Equatable, Sendable {
    let imagePixelSize: CGSize
    let scale: CGFloat
    let imageRectInView: CGRect

    init(imagePixelSize: CGSize, pixelsPerPoint: CGFloat, viewSize: CGSize) {
        self.imagePixelSize = imagePixelSize
        let values = [imagePixelSize.width, imagePixelSize.height, pixelsPerPoint, viewSize.width, viewSize.height]
        guard values.allSatisfy({ $0.isFinite && $0 > 0 }) else {
            scale = 0
            imageRectInView = .zero
            return
        }
        scale = min(viewSize.width / imagePixelSize.width, viewSize.height / imagePixelSize.height, 1 / pixelsPerPoint)
        let size = CGSize(width: imagePixelSize.width * scale, height: imagePixelSize.height * scale)
        imageRectInView = CGRect(x: (viewSize.width - size.width) / 2, y: (viewSize.height - size.height) / 2,
                                 width: size.width, height: size.height)
    }

    func imagePoint(fromView p: CGPoint) -> CGPoint? {
        guard scale > 0 else { return nil }
        return CGPoint(x: (p.x - imageRectInView.minX) / scale, y: (p.y - imageRectInView.minY) / scale)
    }
    func viewPoint(fromImage p: CGPoint) -> CGPoint {
        CGPoint(x: p.x * scale + imageRectInView.minX, y: p.y * scale + imageRectInView.minY)
    }
    func viewRect(fromImage r: CGRect) -> CGRect {
        CGRect(origin: viewPoint(fromImage: r.origin), size: CGSize(width: r.width * scale, height: r.height * scale))
    }
    func imageRect(fromView r: CGRect) -> CGRect? {
        guard let origin = imagePoint(fromView: r.origin) else { return nil }
        return CGRect(origin: origin, size: CGSize(width: r.width / scale, height: r.height / scale))
    }
    func viewLength(fromImage length: CGFloat) -> CGFloat { length * scale }
    func isInsideImage(viewPoint p: CGPoint) -> Bool {
        scale > 0 && p.x >= imageRectInView.minX && p.x <= imageRectInView.maxX
            && p.y >= imageRectInView.minY && p.y <= imageRectInView.maxY
    }
    func clampedImagePoint(fromView p: CGPoint) -> CGPoint? {
        guard let point = imagePoint(fromView: p) else { return nil }
        return CGPoint(x: min(imagePixelSize.width, max(0, point.x)), y: min(imagePixelSize.height, max(0, point.y)))
    }
}
