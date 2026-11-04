import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var coordinator: AppCoordinator?

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard !AppEnvironment.isRunningUnitTests(ProcessInfo.processInfo.environment) else { return }
        NSApp.setActivationPolicy(.accessory)
        let coordinator = AppCoordinator()
        self.coordinator = coordinator
        NSApp.mainMenu = MainMenuBuilder.makeMainMenu(
            settingsTarget: coordinator,
            settingsAction: #selector(AppCoordinator.openSettings(_:))
        )
        coordinator.start()
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        coordinator?.applicationShouldTerminate() ?? .terminateNow
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        coordinator?.handleDockReopen() ?? true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool { true }
}
