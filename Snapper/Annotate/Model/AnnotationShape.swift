import Foundation
import CoreGraphics

/// Value geometry in image pixels. Widths use image points times the capture's backing scale;
/// callers convert view-point tolerances to image pixels at the current zoom.
struct AnnotationShape: Identifiable, Equatable, Sendable {
    enum Kind: Equatable, Sendable {
        case rectangle(CGRect)
        case arrow(start: CGPoint, end: CGPoint)
        case text(TextBox)

        fileprivate var normalized: Self {
            if case .rectangle(let rect) = self { return .rectangle(rect.standardized) }
            return self
        }
    }

    static let hitSlopViewPoints: CGFloat = 4
    static let handleRadiusViewPoints: CGFloat = 6
    static let minimumDragViewPoints: CGFloat = 3

    let id: UUID
    var kind: Kind { didSet { kind = kind.normalized } }
    var color: RGBAColor
    var strokeWidth: StrokeWidth

    init(id: UUID = UUID(), kind: Kind, color: RGBAColor, strokeWidth: StrokeWidth) {
        self.id = id
        self.kind = kind.normalized
        self.color = color
        self.strokeWidth = strokeWidth
    }

    func strokePixels(pixelsPerPoint: CGFloat) -> CGFloat { strokeWidth.points * pixelsPerPoint }
    func fontPixelSize(pixelsPerPoint: CGFloat) -> CGFloat { strokeWidth.textPointSize * pixelsPerPoint }

    func arrowGeometry(pixelsPerPoint: CGFloat) -> ArrowGeometry? {
        guard case .arrow(let start, let end) = kind else { return nil }
        return .make(start: start, end: end, strokeWidth: strokeWidth, pixelsPerPoint: pixelsPerPoint)
    }

    func bounds(pixelsPerPoint: CGFloat) -> CGRect {
        switch kind {
        case .rectangle(let rect):
            let half = strokePixels(pixelsPerPoint: pixelsPerPoint) / 2
            return rect.insetBy(dx: -half, dy: -half)
        case .arrow(let start, let end):
            return ArrowGeometry.make(start: start, end: end, strokeWidth: strokeWidth,
                                      pixelsPerPoint: pixelsPerPoint).bounds
        case .text(let box): return CGRect(origin: box.origin, size: box.measuredSize)
        }
    }

    func hitTest(_ p: CGPoint, pixelsPerPoint: CGFloat, slop: CGFloat) -> Bool {
        let threshold = strokePixels(pixelsPerPoint: pixelsPerPoint) / 2 + slop
        switch kind {
        case .rectangle(let rect): return ShapeGeometry.distance(from: p, toOutlineOf: rect) <= threshold
        case .arrow(let start, let end):
            let head = ArrowGeometry.make(start: start, end: end, strokeWidth: strokeWidth,
                                          pixelsPerPoint: pixelsPerPoint)
            return ShapeGeometry.distance(from: p, toSegment: start, end) <= threshold
                || ShapeGeometry.triangleContains(p, head.tip, head.baseLeft, head.baseRight)
        case .text(let box):
            let rect = CGRect(origin: box.origin, size: box.measuredSize)
            return p.x >= rect.minX && p.x <= rect.maxX && p.y >= rect.minY && p.y <= rect.maxY
        }
    }

    var handles: [(ShapeHandle, CGPoint)] {
        switch kind {
        case .rectangle(let r):
            return [(.topLeft, CGPoint(x: r.minX, y: r.minY)), (.topRight, CGPoint(x: r.maxX, y: r.minY)),
                    (.bottomLeft, CGPoint(x: r.minX, y: r.maxY)), (.bottomRight, CGPoint(x: r.maxX, y: r.maxY))]
        case .arrow(let start, let end): return [(.start, start), (.end, end)]
        case .text: return []
        }
    }

    func handleHitTest(_ p: CGPoint, radius: CGFloat) -> ShapeHandle? {
        var nearest: ShapeHandle?
        var minimum = CGFloat.infinity
        for (handle, position) in handles {
            let distance = hypot(p.x - position.x, p.y - position.y)
            if distance <= radius && distance < minimum {
                nearest = handle
                minimum = distance
            }
        }
        return nearest
    }

    func moved(by delta: CGVector) -> Self {
        func translated(_ p: CGPoint) -> CGPoint { CGPoint(x: p.x + delta.dx, y: p.y + delta.dy) }
        var copy = self
        switch kind {
        case .rectangle(let r): copy.kind = .rectangle(CGRect(origin: translated(r.origin), size: r.size))
        case .arrow(let start, let end): copy.kind = .arrow(start: translated(start), end: translated(end))
        case .text(var box):
            box.origin = translated(box.origin)
            copy.kind = .text(box)
        }
        return copy
    }

    /// Pass the returned handle into the next event so the opposite corner stays fixed.
    /// A handle belonging to another kind is a programmer error; it safely leaves the shape unchanged.
    func resized(handle: ShapeHandle, to p: CGPoint, constrained: Bool) -> (shape: Self, handle: ShapeHandle) {
        var copy = self
        switch kind {
        case .rectangle:
            guard let fixed = handles.first(where: { $0.0 == handle.opposite })?.1 else { return (self, handle) }
            let corner = constrained ? ShapeGeometry.squareCorner(anchor: fixed, toward: p) : p
            copy.kind = .rectangle(ShapeGeometry.normalizedRect(from: fixed, to: corner))
            let left = corner.x < fixed.x, top = corner.y < fixed.y
            let next: ShapeHandle = top ? (left ? .topLeft : .topRight) : (left ? .bottomLeft : .bottomRight)
            return (copy, next)
        case .arrow(let start, let end):
            guard handle == .start || handle == .end else { return (self, handle) }
            let fixed = handle == .start ? end : start
            let moving = constrained ? ShapeGeometry.snappedEndpoint(fixed: fixed, moving: p) : p
            copy.kind = handle == .start ? .arrow(start: moving, end: end) : .arrow(start: start, end: moving)
            return (copy, handle)
        case .text: return (self, handle)
        }
    }

    static func rectangleFromDrag(start: CGPoint, current: CGPoint, constrained: Bool) -> CGRect {
        ShapeGeometry.normalizedRect(from: start, to: constrained ? ShapeGeometry.squareCorner(anchor: start, toward: current) : current)
    }

    static func arrowFromDrag(start: CGPoint, current: CGPoint, constrained: Bool) -> (start: CGPoint, end: CGPoint) {
        (start, constrained ? ShapeGeometry.snappedEndpoint(fixed: start, moving: current) : current)
    }

    static func makeRectangle(dragFrom start: CGPoint, to current: CGPoint, constrained: Bool,
                              color: RGBAColor, strokeWidth: StrokeWidth, minimumDrag: CGFloat) -> Self? {
        let rect = rectangleFromDrag(start: start, current: current, constrained: constrained)
        guard rect.width >= minimumDrag, rect.height >= minimumDrag, rect.width > 0, rect.height > 0 else { return nil }
        return Self(kind: .rectangle(rect), color: color, strokeWidth: strokeWidth)
    }

    static func makeArrow(dragFrom start: CGPoint, to current: CGPoint, constrained: Bool,
                          color: RGBAColor, strokeWidth: StrokeWidth, minimumDrag: CGFloat) -> Self? {
        let arrow = arrowFromDrag(start: start, current: current, constrained: constrained)
        let length = hypot(arrow.end.x - start.x, arrow.end.y - start.y)
        guard length >= minimumDrag, length > 0 else { return nil }
        return Self(kind: .arrow(start: start, end: arrow.end), color: color, strokeWidth: strokeWidth)
    }

    static func makeText(origin: CGPoint, color: RGBAColor, strokeWidth: StrokeWidth) -> Self {
        Self(kind: .text(TextBox(origin: origin, string: "", measuredSize: .zero)), color: color, strokeWidth: strokeWidth)
    }

    func withColor(_ color: RGBAColor) -> Self {
        var copy = self
        copy.color = color
        return copy
    }

    /// Text callers must also supply a newly measured size in the same document edit.
    func withStrokeWidth(_ width: StrokeWidth) -> Self {
        var copy = self
        copy.strokeWidth = width
        return copy
    }

    /// Text callers must also supply a newly measured size in the same document edit.
    func withText(_ string: String) -> Self {
        guard case .text(var box) = kind else { return self }
        var copy = self
        box.string = string
        copy.kind = .text(box)
        return copy
    }

    func withMeasuredSize(_ size: CGSize) -> Self {
        guard case .text(var box) = kind else { return self }
        var copy = self
        box.measuredSize = size
        copy.kind = .text(box)
        return copy
    }
}
