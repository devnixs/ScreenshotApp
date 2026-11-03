import ServiceManagement

@MainActor
enum LaunchAtLogin {
    static var status: LaunchAtLoginStatus {
        switch SMAppService.mainApp.status {
        case .notRegistered: .notRegistered
        case .enabled: .enabled
        case .requiresApproval: .requiresApproval
        case .notFound: .notFound
        @unknown default: .notRegistered
        }
    }

    static func setEnabled(_ on: Bool) -> String? {
        do {
            if on { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
            return nil
        } catch { return error.localizedDescription }
    }

    static func openLoginItemsSettings() { SMAppService.openSystemSettingsLoginItems() }
}
