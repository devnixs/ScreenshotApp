import AppKit

enum SystemSettingsLinks {
    // These URL literals are known-valid System Settings constants.
    static let screenRecording = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!
    static let keyboardShortcuts = URL(string: "x-apple.systempreferences:com.apple.preference.keyboard?Shortcuts")!
    static let microphone = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone")!

    @MainActor
    static func open(_ url: URL) {
        NSWorkspace.shared.open(url)
    }
}
