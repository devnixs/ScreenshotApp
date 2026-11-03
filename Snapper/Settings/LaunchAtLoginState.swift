enum LaunchAtLoginStatus: CaseIterable, Sendable {
    case notRegistered, enabled, requiresApproval, notFound
}

struct LaunchAtLoginState: Equatable, Sendable {
    var isOn: Bool
    var note: Note?

    enum Note: Equatable, Sendable {
        case requiresApproval, unavailable, failed(String)
    }

    static func state(for status: LaunchAtLoginStatus, error: String? = nil) -> Self {
        let state: Self
        switch status {
        case .enabled: state = .init(isOn: true, note: nil)
        case .requiresApproval: state = .init(isOn: true, note: .requiresApproval)
        case .notRegistered: state = .init(isOn: false, note: nil)
        case .notFound: state = .init(isOn: false, note: .unavailable)
        }
        return .init(isOn: state.isOn, note: error.map(Note.failed) ?? state.note)
    }
}
