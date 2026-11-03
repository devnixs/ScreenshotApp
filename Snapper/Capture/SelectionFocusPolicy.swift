enum SelectionFocusPolicy {
    enum Outcome: Equatable, Sendable {
        case selected, cancelled
    }

    static func restoresPreviousApplication(purpose: CapturePurpose, outcome: Outcome) -> Bool {
        if outcome == .cancelled { return true }
        switch purpose {
        case .screenshot, .record: return true
        case .annotate: return false
        }
    }
}
