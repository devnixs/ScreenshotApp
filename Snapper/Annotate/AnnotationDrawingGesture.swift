import CoreGraphics

/// Transient drawing input. Document history is touched only by the caller on completion.
struct AnnotationDrawingGesture: Sendable {
    let tool: AnnotationTool
    let start: CGPoint
    private(set) var current: CGPoint
    let color: RGBAColor
    let strokeWidth: StrokeWidth
    let transform: CanvasTransform

    init?(tool: AnnotationTool, viewPoint: CGPoint, transform: CanvasTransform, color: RGBAColor, strokeWidth: StrokeWidth) {
        guard tool.createsShapesByDragging, transform.isInsideImage(viewPoint: viewPoint),
              let start = transform.imagePoint(fromView: viewPoint) else { return nil }
        self.tool = tool
        self.start = start
        current = start
        self.transform = transform
        self.color = color
        self.strokeWidth = strokeWidth
    }

    mutating func update(viewPoint: CGPoint) {
        if let point = transform.clampedImagePoint(fromView: viewPoint) { current = point }
    }

    func shape(constrained: Bool, completing: Bool) -> AnnotationShape? {
        // Constraint geometry may project beyond the image; shorten uniformly about the anchor
        // so squares and snapped angles remain intact at all four edges.
        let proposed: CGPoint
        if tool == .rectangle {
            proposed = constrained ? ShapeGeometry.squareCorner(anchor: start, toward: current) : current
        } else {
            proposed = AnnotationShape.arrowFromDrag(start: start, current: current, constrained: constrained).end
        }
        let dx = proposed.x - start.x, dy = proposed.y - start.y
        let size = transform.imagePixelSize
        var factor: CGFloat = 1
        if dx > 0 { factor = min(factor, (size.width - start.x) / dx) }
        if dx < 0 { factor = min(factor, -start.x / dx) }
        if dy > 0 { factor = min(factor, (size.height - start.y) / dy) }
        if dy < 0 { factor = min(factor, -start.y / dy) }
        let end = CGPoint(x: start.x + dx * factor, y: start.y + dy * factor)
        let minimum = completing ? AnnotationShape.minimumDragViewPoints / transform.scale : 0
        if tool == .rectangle {
            return AnnotationShape.makeRectangle(dragFrom: start, to: end, constrained: false,
                                                 color: color, strokeWidth: strokeWidth, minimumDrag: minimum)
        }
        return AnnotationShape.makeArrow(dragFrom: start, to: end, constrained: false,
                                         color: color, strokeWidth: strokeWidth, minimumDrag: minimum)
    }
}
