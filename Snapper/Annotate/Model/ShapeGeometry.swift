import Foundation
import CoreGraphics

/// Pure geometry in image pixels, with a top-left origin and y increasing downward.
enum ShapeGeometry: Equatable, Sendable {
    static func normalizedRect(from a: CGPoint, to b: CGPoint) -> CGRect {
        CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
    }

    static func squareCorner(anchor: CGPoint, toward p: CGPoint) -> CGPoint {
        let dx = p.x - anchor.x, dy = p.y - anchor.y
        let side = max(abs(dx), abs(dy))
        return CGPoint(x: anchor.x + (dx < 0 ? -side : side), y: anchor.y + (dy < 0 ? -side : side))
    }

    static func snappedEndpoint(fixed: CGPoint, moving p: CGPoint) -> CGPoint {
        let dx = p.x - fixed.x, dy = p.y - fixed.y
        let length = hypot(dx, dy)
        guard length > 0 else { return fixed }
        let step = CGFloat.pi / 4
        let angle = (atan2(dy, dx) / step).rounded(.toNearestOrAwayFromZero) * step
        return CGPoint(x: fixed.x + length * cos(angle), y: fixed.y + length * sin(angle))
    }

    static func distance(from p: CGPoint, toSegment a: CGPoint, _ b: CGPoint) -> CGFloat {
        let dx = b.x - a.x, dy = b.y - a.y
        let squaredLength = dx * dx + dy * dy
        guard squaredLength > 0 else { return hypot(p.x - a.x, p.y - a.y) }
        let t = min(1, max(0, ((p.x - a.x) * dx + (p.y - a.y) * dy) / squaredLength))
        return hypot(p.x - a.x - t * dx, p.y - a.y - t * dy)
    }

    static func distance(from p: CGPoint, toOutlineOf r: CGRect) -> CGFloat {
        let rect = r.standardized
        let dx = max(rect.minX - p.x, 0, p.x - rect.maxX)
        let dy = max(rect.minY - p.y, 0, p.y - rect.maxY)
        if dx > 0 || dy > 0 { return hypot(dx, dy) }
        return min(p.x - rect.minX, rect.maxX - p.x, p.y - rect.minY, rect.maxY - p.y)
    }

    static func triangleContains(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint, _ c: CGPoint) -> Bool {
        func cross(_ a: CGPoint, _ b: CGPoint, _ c: CGPoint) -> CGFloat {
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)
        }
        // A collapsed head is only its closed segments, not the entire plane.
        if cross(a, b, c) == 0 {
            return distance(from: p, toSegment: a, b) == 0
                || distance(from: p, toSegment: b, c) == 0
                || distance(from: p, toSegment: c, a) == 0
        }
        let signs = [cross(a, b, p), cross(b, c, p), cross(c, a, p)]
        return !signs.contains(where: { $0 < 0 }) || !signs.contains(where: { $0 > 0 })
    }
}
