import AppKit
import KeyboardShortcuts
import os

@MainActor
final class AppCoordinator: NSObject {
    let settings = AppSettings()
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "AppMode")
    private let settingsWindowController = SettingsWindowController()
    private var statusItemController: StatusItemController?
    private var modeMachine = AppModeMachine()

    var mode: AppMode { modeMachine.mode }

    func start() {
        guard statusItemController == nil else { return }
        statusItemController = StatusItemController(coordinator: self)
        for purpose in CapturePurpose.allCases {
            KeyboardShortcuts.onKeyDown(for: purpose.shortcutName) { [weak self] in
                self?.send(.shortcut(purpose))
            }
        }
    }

    func send(_ event: AppModeEvent) {
        let oldMode = mode
        let action = modeMachine.handle(event)
        logger.info("\(String(describing: event), privacy: .public): \(String(describing: oldMode), privacy: .public) → \(String(describing: self.mode), privacy: .public), action: \(String(describing: action), privacy: .public)")
        perform(action)
    }

    private func perform(_ action: AppModeAction) {
        switch action {
        case .none: break
        case .showOverlay(let purpose): beginSelection(for: purpose)
        case .completeSelection(let purpose):
            logger.info("Selection completed for \(purpose.rawValue, privacy: .public) (pipeline not implemented yet)")
        case .stopRecording:
            logger.info("Stop recording requested (not implemented until 0011)")
        case .openMenu: statusItemController?.showMenu()
        case .openSettings: openSettings()
        }
    }

    private func beginSelection(for purpose: CapturePurpose) {
        // Permission gate (0006): if Screen Recording permission is missing, open onboarding and send(.selectionCancelled).
        logger.info("Overlay requested for \(purpose.rawValue, privacy: .public) (not implemented until 0007)")
        send(.selectionCancelled)
    }

    func openSettings() { settingsWindowController.show() }
    @objc func openSettings(_ sender: Any?) { openSettings() }
    func quit() { NSApp.terminate(nil) }
}
