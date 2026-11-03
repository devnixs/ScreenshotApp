enum MicrophoneAuthorization: CaseIterable, Sendable {
    case notDetermined, authorized, denied, restricted
}

struct MicrophoneToggleOutcome: Equatable, Sendable {
    var storedValue: Bool
    var showsDeniedNote: Bool
    var shouldRequestAccess: Bool
}

enum MicrophoneToggleDecision {
    static func userToggled(to requested: Bool, status: MicrophoneAuthorization) -> MicrophoneToggleOutcome {
        guard requested else { return .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: false) }
        switch status {
        case .authorized: return accessRequestFinished(granted: true)
        case .notDetermined: return .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: true)
        case .denied, .restricted: return accessRequestFinished(granted: false)
        }
    }

    static func accessRequestFinished(granted: Bool) -> MicrophoneToggleOutcome {
        .init(storedValue: granted, showsDeniedNote: !granted, shouldRequestAccess: false)
    }

    static func reconcileOnAppear(stored: Bool, status: MicrophoneAuthorization) -> MicrophoneToggleOutcome {
        guard stored else { return .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: false) }
        switch status {
        case .denied, .restricted: return accessRequestFinished(granted: false)
        case .notDetermined: return .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: false)
        case .authorized: return accessRequestFinished(granted: true)
        }
    }
}

/// Keeps a permission prompt's delayed result from overriding a later toggle-off.
struct MicrophoneRequestState {
    private(set) var wantsMicrophone = false
    private(set) var isPending = false

    mutating func userToggled(to value: Bool) { wantsMicrophone = value }
    mutating func beginRequest() -> Bool {
        guard !isPending else { return false }
        isPending = true
        return true
    }
    mutating func finishRequest() -> Bool {
        isPending = false
        return wantsMicrophone
    }
}
