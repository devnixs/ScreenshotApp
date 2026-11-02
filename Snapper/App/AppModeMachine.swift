enum AppMode: Equatable, Sendable {
    case idle
    case selecting(CapturePurpose)
    /// Includes startup and asynchronous finalization, until `recordingEnded`.
    case recording
}

enum StatusItemButton: Equatable, Sendable {
    case left, right
}

enum AppModeEvent: Equatable, Sendable {
    case shortcut(CapturePurpose)
    case menuItem(CapturePurpose)
    case statusItemClicked(StatusItemButton)
    case selectionFinished
    case selectionCancelled
    case recordingEnded
}

enum AppModeAction: Equatable, Sendable {
    case none
    case showOverlay(CapturePurpose)
    case completeSelection(CapturePurpose)
    /// Remains recording until `recordingEnded`; the session must handle repeated stops idempotently.
    /// Pill Stop / Cancel call the session directly and finish with the same lifecycle event.
    case stopRecording
    case openMenu
    case openSettings
}

/// Owns capture routing policy independently of UI and capture services.
/// Menu items obey the same guards as shortcuts; status clicks are also ignored during selection.
struct AppModeMachine: Sendable {
    private(set) var mode: AppMode = .idle

    /// Applies `event`, updates `mode`, and returns the single action the caller must perform.
    mutating func handle(_ event: AppModeEvent) -> AppModeAction {
        switch mode {
        case .idle:
            switch event {
            case .shortcut(let purpose), .menuItem(let purpose):
                mode = .selecting(purpose)
                return .showOverlay(purpose)
            case .statusItemClicked(.left): return .openMenu
            case .statusItemClicked(.right): return .openSettings
            case .selectionFinished, .selectionCancelled, .recordingEnded: return .none
            }
        case .selecting(let purpose):
            switch event {
            case .selectionFinished:
                // Enter recording before stream startup so another overlay cannot open during startup.
                // A failed startup must send recordingEnded, just like a completed recording.
                mode = purpose == .record ? .recording : .idle
                return .completeSelection(purpose)
            case .selectionCancelled:
                mode = .idle
                return .none
            case .shortcut, .menuItem, .statusItemClicked, .recordingEnded: return .none
            }
        case .recording:
            switch event {
            case .shortcut(.record), .menuItem(.record), .statusItemClicked: return .stopRecording
            case .recordingEnded:
                mode = .idle
                return .none
            case .shortcut, .menuItem, .selectionFinished, .selectionCancelled: return .none
            }
        }
    }
}
