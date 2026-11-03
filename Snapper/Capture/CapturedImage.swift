import CoreGraphics

struct CapturedImage: Sendable {
    let cgImage: CGImage
    let pixelsPerPoint: CGFloat

    var pointSize: CGSize {
        CGSize(width: CGFloat(cgImage.width) / pixelsPerPoint,
               height: CGFloat(cgImage.height) / pixelsPerPoint)
    }
}
