import CoreGraphics

enum RectSelectionOutcome: Equatable, Sendable {
    case selected(SelectionResult)
    case tooSmall
    case ignored
}

/// Rectangle interaction in AppKit-global points; independent of backing scale and UI.
struct RectSelectionTracker: Sendable {
    static let minimumSize: CGFloat = 4
    private enum State: Sendable {
        case idle
        case dragging(startDisplay: DisplayInfo, start: CGPoint, current: CGPoint)
    }
    private var state: State = .idle

    mutating func mouseDown(at point: CGPoint, displays: [DisplayInfo]) {
        func distanceSquared(to display: DisplayInfo) -> CGFloat {
            let nearest = DisplayGeometry.clamp(point, to: display.frame)
            return pow(point.x - nearest.x, 2) + pow(point.y - nearest.y, 2)
        }
        guard let display = DisplayGeometry.display(containing: point, in: displays)
                ?? displays.min(by: { distanceSquared(to: $0) < distanceSquared(to: $1) }) else {
            reset()
            return
        }
        let start = DisplayGeometry.clamp(point, to: display.frame)
        state = .dragging(startDisplay: display, start: start, current: start)
    }

    @discardableResult
    mutating func drag(to point: CGPoint) -> CGRect? {
        guard case .dragging(let display, let start, _) = state else { return nil }
        state = .dragging(startDisplay: display, start: start,
                          current: DisplayGeometry.clamp(point, to: display.frame))
        return currentRect
    }

    var currentRect: CGRect? {
        guard case .dragging(_, let start, let current) = state else { return nil }
        return DisplayGeometry.normalizedRect(from: start, to: current)
    }

    var currentPoint: CGPoint? {
        guard case .dragging(_, _, let current) = state else { return nil }
        return current
    }

    var startDisplay: DisplayInfo? {
        guard case .dragging(let display, _, _) = state else { return nil }
        return display
    }

    mutating func mouseUp(at point: CGPoint) -> RectSelectionOutcome {
        drag(to: point)
        defer { reset() }
        guard let rect = currentRect, let display = startDisplay else { return .ignored }
        guard rect.width >= Self.minimumSize, rect.height >= Self.minimumSize else { return .tooSmall }
        return .selected(.rect(display: display, rect: rect))
    }

    mutating func reset() { state = .idle }
}
