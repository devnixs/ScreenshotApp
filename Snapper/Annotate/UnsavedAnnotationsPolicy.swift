enum UnsavedAnnotationsPolicy {
    enum CloseDecision: Equatable { case closeImmediately, confirmDiscard }
    enum QuitDecision: Equatable { case terminateNow, confirm(message: String) }

    static let discardTitle = "Discard changes?"
    static let discardButton = "Discard"
    static let quitButton = "Quit"
    static let cancelButton = "Cancel"

    static func closeDecision(isDirtyAfterCommittingText: Bool) -> CloseDecision {
        isDirtyAfterCommittingText ? .confirmDiscard : .closeImmediately
    }
    static func quitDecision(dirtyWindowCount: Int) -> QuitDecision {
        dirtyWindowCount > 0 ? .confirm(message: quitMessage(dirtyWindowCount: dirtyWindowCount)) : .terminateNow
    }
    static func quitMessage(dirtyWindowCount: Int) -> String {
        "Quit and discard \(dirtyWindowCount) unsaved annotation\(dirtyWindowCount == 1 ? "" : "s")?"
    }
}
