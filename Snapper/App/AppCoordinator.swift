import AppKit
import os

@MainActor
final class AppCoordinator: NSObject {
    let settings = AppSettings()
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "app")
    private let settingsWindowController = SettingsWindowController()
    private var statusItemController: StatusItemController?

    func start() {
        statusItemController = StatusItemController(coordinator: self)
    }

    func startScreenshot() { logger.info("startScreenshot() not implemented yet") }
    func startRecording() { logger.info("startRecording() not implemented yet") }
    func startAnnotate() { logger.info("startAnnotate() not implemented yet") }
    func openSettings() { settingsWindowController.show() }
    @objc func openSettings(_ sender: Any?) { openSettings() }
    func quit() { NSApp.terminate(nil) }
}
