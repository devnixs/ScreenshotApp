import CoreGraphics

struct ShapeDragSession: Sendable {
    enum Kind: Equatable, Sendable { case move, resize(ShapeHandle) }
    let original: AnnotationShape
    let kind: Kind
    let startPoint: CGPoint
    let imageBounds: CGRect

    func preview(at point: CGPoint, shiftHeld: Bool) -> AnnotationShape {
        // A handle can be grabbed anywhere inside its hit radius. A plain click
        // must never snap that handle to the pointer or create an edit.
        guard point != startPoint else { return original }
        let point = CGPoint(x: min(imageBounds.maxX, max(imageBounds.minX, point.x)),
                            y: min(imageBounds.maxY, max(imageBounds.minY, point.y)))
        switch kind {
        case .move:
            return original.moved(by: CGVector(dx: point.x - startPoint.x, dy: point.y - startPoint.y))
        case .resize(let handle):
            return original.resized(handle: handle, to: point, constrained: shiftHeld).shape
        }
    }
    func finish(at point: CGPoint, shiftHeld: Bool) -> AnnotationShape? {
        let shape = preview(at: point, shiftHeld: shiftHeld)
        return shape == original ? nil : shape
    }
}
