import Foundation
import CoreGraphics

/// Shared by bounds, hit-testing and rendering: butt-capped shaft, unstroked filled head.
struct ArrowGeometry: Equatable, Sendable {
    let tip: CGPoint
    let baseCenter: CGPoint
    let baseLeft: CGPoint
    let baseRight: CGPoint
    let shaftStart: CGPoint
    let strokePixels: CGFloat

    static func make(start: CGPoint, end: CGPoint, strokeWidth: StrokeWidth, pixelsPerPoint: CGFloat) -> Self {
        let stroke = strokeWidth.points * pixelsPerPoint
        let length = hypot(end.x - start.x, end.y - start.y)
        guard length > 0 else {
            return Self(tip: start, baseCenter: start, baseLeft: start, baseRight: start,
                        shaftStart: start, strokePixels: stroke)
        }
        let ux = (end.x - start.x) / length, uy = (end.y - start.y) / length
        let headLength = min(length, 3 * stroke + 8 * pixelsPerPoint)
        let halfWidth = 0.4 * headLength
        let base = CGPoint(x: end.x - ux * headLength, y: end.y - uy * headLength)
        return Self(tip: end, baseCenter: base,
                    baseLeft: CGPoint(x: base.x - uy * halfWidth, y: base.y + ux * halfWidth),
                    baseRight: CGPoint(x: base.x + uy * halfWidth, y: base.y - ux * halfWidth),
                    shaftStart: start, strokePixels: stroke)
    }

    var bounds: CGRect {
        let dx = tip.x - shaftStart.x, dy = tip.y - shaftStart.y
        let length = hypot(dx, dy)
        // A zero-length butt-capped shaft paints no additional area.
        let nx = length > 0 ? -dy / length * strokePixels / 2 : 0
        let ny = length > 0 ? dx / length * strokePixels / 2 : 0
        var points = [tip, baseLeft, baseRight]
        if shaftStart != baseCenter {
            for p in [shaftStart, baseCenter] {
                points.append(CGPoint(x: p.x + nx, y: p.y + ny))
                points.append(CGPoint(x: p.x - nx, y: p.y - ny))
            }
        }
        return points.reduce(CGRect(origin: tip, size: .zero)) { result, point in
            ShapeGeometry.normalizedRect(from: CGPoint(x: min(result.minX, point.x), y: min(result.minY, point.y)),
                                         to: CGPoint(x: max(result.maxX, point.x), y: max(result.maxY, point.y)))
        }
    }
}
