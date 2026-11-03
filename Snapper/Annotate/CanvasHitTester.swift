import CoreGraphics

enum CanvasHit: Equatable {
    case handle(shapeID: AnnotationShape.ID, ShapeHandle)
    case shape(AnnotationShape.ID)
    case none
}
enum CanvasHitTester {
    static let handleHitRadiusInViewPoints: CGFloat = 6
    static let shapeHitToleranceInViewPoints: CGFloat = 4
    static func imagePixels(fromViewPoints points: CGFloat, transform: CanvasTransform) -> CGFloat {
        transform.scale > 0 ? points / transform.scale : 0
    }
    static func hitTest(_ point: CGPoint, in document: AnnotationDocument, transform: CanvasTransform) -> CanvasHit {
        guard transform.scale > 0 else { return .none }
        if let shape = document.selectedShape,
           let handle = shape.handleHitTest(point, radius: imagePixels(fromViewPoints: handleHitRadiusInViewPoints, transform: transform)) {
            return .handle(shapeID: shape.id, handle)
        }
        if let shape = document.topmostShape(at: point, tolerance: imagePixels(fromViewPoints: shapeHitToleranceInViewPoints, transform: transform)) {
            return .shape(shape.id)
        }
        return .none
    }
}
