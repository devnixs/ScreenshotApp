import CoreGraphics

enum SelectionMode: Equatable, Sendable { case rectangle, window }

enum SelectionSessionOutcome: Equatable, Sendable {
    case none
    case rectangle(SelectionResult)
    case windowClick(at: CGPoint)
}

/// Interaction state in AppKit-global points. Mode changes abandon the current press.
struct SelectionSession: Sendable {
    private(set) var mode: SelectionMode = .rectangle
    private(set) var tracker = RectSelectionTracker()
    private(set) var isMouseDown = false
    private(set) var isPressStale = false
    private(set) var isWindowClickArmed = false

    mutating func toggleMode() {
        mode = mode == .rectangle ? .window : .rectangle
        tracker = RectSelectionTracker()
        isWindowClickArmed = false
        isPressStale = isMouseDown
    }

    mutating func mouseDown(at point: CGPoint, displays: [DisplayInfo]) {
        isMouseDown = true
        isPressStale = false
        switch mode {
        case .rectangle: tracker.mouseDown(at: point, displays: displays)
        case .window: isWindowClickArmed = true
        }
    }

    mutating func mouseDragged(to point: CGPoint) {
        if mode == .rectangle && !isPressStale { tracker.drag(to: point) }
    }

    mutating func mouseUp(at point: CGPoint) -> SelectionSessionOutcome {
        isMouseDown = false
        defer { isWindowClickArmed = false }
        if isPressStale { isPressStale = false; return .none }
        switch mode {
        case .rectangle:
            if case .selected(let result) = tracker.mouseUp(at: point) { return .rectangle(result) }
            return .none
        case .window:
            return isWindowClickArmed ? .windowClick(at: point) : .none
        }
    }
}
