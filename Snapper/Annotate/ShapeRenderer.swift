import CoreGraphics

/// The destination CTM must map top-left, y-down image pixels into the destination.
enum ShapeRenderer {
    static func drawImage(_ image: CGImage, in context: CGContext) {
        context.saveGState()
        context.interpolationQuality = .high
        context.translateBy(x: 0, y: CGFloat(image.height))
        context.scaleBy(x: 1, y: -1)
        context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        context.restoreGState()
    }

    static func draw(_ shapes: [AnnotationShape], pixelsPerPoint: CGFloat, in context: CGContext) {
        for shape in shapes { draw(shape, pixelsPerPoint: pixelsPerPoint, in: context) }
    }

    static func draw(_ shape: AnnotationShape, pixelsPerPoint: CGFloat, in context: CGContext) {
        context.saveGState()
        defer { context.restoreGState() }
        context.setStrokeColor(shape.color.cgColor)
        context.setFillColor(shape.color.cgColor)
        context.setLineWidth(shape.strokePixels(pixelsPerPoint: pixelsPerPoint))
        switch shape.kind {
        case .rectangle(let rect):
            context.setLineJoin(.miter)
            context.stroke(rect)
        case .arrow:
            guard let head = shape.arrowGeometry(pixelsPerPoint: pixelsPerPoint) else { return }
            context.setLineCap(.butt)
            if head.shaftStart != head.baseCenter {
                context.move(to: head.shaftStart)
                context.addLine(to: head.baseCenter)
                context.strokePath()
            }
            context.move(to: head.tip)
            context.addLine(to: head.baseLeft)
            context.addLine(to: head.baseRight)
            context.closePath()
            context.fillPath()
        case .text: break // Text rendering is supplied by 0019.
        }
    }
}
