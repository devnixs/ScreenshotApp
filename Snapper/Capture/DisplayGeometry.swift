import CoreGraphics
import Foundation

/// A display in AppKit global points, with its own backing-store scale.
struct DisplayInfo: Sendable, Equatable, Hashable {
    let displayID: CGDirectDisplayID
    let frame: CGRect
    let backingScale: CGFloat

    func hash(into hasher: inout Hasher) {
        hasher.combine(displayID)
        hasher.combine(frame.origin.x)
        hasher.combine(frame.origin.y)
        hasher.combine(frame.size.width)
        hasher.combine(frame.size.height)
        hasher.combine(backingScale)
    }
}

struct PixelSize: Sendable, Equatable {
    let width: Int
    let height: Int
}

struct CaptureRegion: Sendable, Equatable {
    let displayID: CGDirectDisplayID
    let appKitRect: CGRect
    let sourceRect: CGRect
    let pixelSize: PixelSize
}

/// Shared coordinate math. Inputs use finite points and display frames, and positive scales.
enum DisplayGeometry {
    static func primaryDisplay(in displays: [DisplayInfo]) -> DisplayInfo? {
        displays.first { $0.frame.origin == .zero } ?? displays.first
    }

    static func primaryHeight(in displays: [DisplayInfo]) -> CGFloat? {
        primaryDisplay(in: displays)?.frame.height
    }

    static func cgPoint(fromAppKit point: CGPoint, primaryHeight: CGFloat) -> CGPoint {
        CGPoint(x: point.x, y: primaryHeight - point.y)
    }

    static func appKitPoint(fromCG point: CGPoint, primaryHeight: CGFloat) -> CGPoint {
        cgPoint(fromAppKit: point, primaryHeight: primaryHeight)
    }

    static func cgRect(fromAppKit rect: CGRect, primaryHeight: CGFloat) -> CGRect {
        let rect = rect.standardized
        return CGRect(x: rect.minX, y: primaryHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    static func appKitRect(fromCG rect: CGRect, primaryHeight: CGFloat) -> CGRect {
        cgRect(fromAppKit: rect, primaryHeight: primaryHeight)
    }

    static func display(containing point: CGPoint, in displays: [DisplayInfo]) -> DisplayInfo? {
        // Resolve shared edges before considering the desktop's closed outer edges.
        displays.first {
            let frame = $0.frame
            return frame.minX <= point.x && point.x < frame.maxX
                && frame.minY <= point.y && point.y < frame.maxY
        } ?? displays.first {
            let frame = $0.frame
            return frame.minX <= point.x && point.x <= frame.maxX
                && frame.minY <= point.y && point.y <= frame.maxY
        }
    }

    static func clamp(_ point: CGPoint, to frame: CGRect) -> CGPoint {
        CGPoint(x: min(max(point.x, frame.minX), frame.maxX),
                y: min(max(point.y, frame.minY), frame.maxY))
    }

    static func clamp(_ rect: CGRect, to frame: CGRect) -> CGRect {
        let rect = rect.standardized
        let lower = clamp(CGPoint(x: rect.minX, y: rect.minY), to: frame)
        let upper = clamp(CGPoint(x: rect.maxX, y: rect.maxY), to: frame)
        return normalizedRect(from: lower, to: upper)
    }

    static func normalizedRect(from start: CGPoint, to end: CGPoint) -> CGRect {
        CGRect(x: min(start.x, end.x), y: min(start.y, end.y),
               width: abs(end.x - start.x), height: abs(end.y - start.y))
    }

    static func dragRect(from start: CGPoint, to current: CGPoint, on display: DisplayInfo) -> CGRect {
        normalizedRect(from: clamp(start, to: display.frame), to: clamp(current, to: display.frame))
    }

    /// Caller clamps first; this conversion does not depend on the primary display.
    static func displayLocalTopLeftRect(_ rect: CGRect, on display: DisplayInfo) -> CGRect {
        let rect = rect.standardized
        return CGRect(x: rect.minX - display.frame.minX, y: display.frame.maxY - rect.maxY,
                      width: rect.width, height: rect.height)
    }

    static func pixelAligned(_ rect: CGRect, on display: DisplayInfo) -> CGRect {
        let rect = rect.standardized
        let scale = display.backingScale
        let epsilon: CGFloat = 1e-6

        func alignedEdges(minimum: CGFloat, maximum: CGFloat, origin: CGFloat) -> (CGFloat, CGFloat) {
            let localMin = (minimum - origin) * scale
            if maximum - minimum <= epsilon / scale {
                let edge = localMin.rounded(.toNearestOrAwayFromZero) / scale + origin
                return (edge, edge)
            }
            return (floor(localMin + epsilon) / scale + origin,
                    ceil((maximum - origin) * scale - epsilon) / scale + origin)
        }

        let (minX, maxX) = alignedEdges(minimum: rect.minX, maximum: rect.maxX, origin: display.frame.minX)
        let (minY, maxY) = alignedEdges(minimum: rect.minY, maximum: rect.maxY, origin: display.frame.minY)
        return clamp(CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY), to: display.frame)
    }

    static func pixelSize(of rect: CGRect, scale: CGFloat) -> PixelSize {
        PixelSize(width: Int((rect.width * scale).rounded()), height: Int((rect.height * scale).rounded()))
    }

    static func captureRegion(for rect: CGRect, on display: DisplayInfo) -> CaptureRegion {
        let aligned = pixelAligned(clamp(rect, to: display.frame), on: display)
        return CaptureRegion(displayID: display.displayID, appKitRect: aligned,
                             sourceRect: displayLocalTopLeftRect(aligned, on: display),
                             pixelSize: pixelSize(of: aligned, scale: display.backingScale))
    }
}
