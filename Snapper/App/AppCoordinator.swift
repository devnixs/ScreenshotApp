import AppKit
import KeyboardShortcuts
import os

@MainActor
final class AppCoordinator: NSObject {
    let settings = AppSettings()
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "AppMode")
    private let selectionLogger = Logger(subsystem: "com.thera-engineering.snapper", category: "selection")
    private let overlay = SelectionOverlayController()
    private let settingsWindowController = SettingsWindowController()
    private lazy var onboardingWindowController = OnboardingWindowController(settings: settings)
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
        let model = OnboardingModel()
        model.refresh()
        if OnboardingDecision.shouldShowAtLaunch(status: model.status, dismissed: settings.onboardingDismissed) {
            showOnboarding()
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
            logger.info("Capture pipeline for \(purpose.rawValue, privacy: .public) is not implemented yet")
        case .stopRecording:
            logger.info("Stop recording requested (not implemented until 0011)")
        case .openMenu: statusItemController?.showMenu()
        case .openSettings: openSettings()
        }
    }

    private func beginSelection(for purpose: CapturePurpose) {
        if OnboardingDecision.shouldGateCapture(permissionGranted: ScreenRecordingPermission.isGranted) {
            send(.selectionCancelled)
            showOnboarding()
            return
        }
        Task { @MainActor in
            guard let result = await overlay.selectRegion() else {
                send(.selectionCancelled)
                return
            }
            send(.selectionFinished)
            switch result {
            case .rect(let display, let rect):
                selectionLogger.info("Selection for \(purpose.rawValue, privacy: .public): display \(display.displayID), rect \(String(describing: rect), privacy: .public)")
            }
            if purpose == .record {
                // TODO(0011): start RecordingSession here.
                send(.recordingEnded)
            }
        }
    }

    func showOnboarding() { onboardingWindowController.show() }

    func openSettings() { settingsWindowController.show() }
    @objc func openSettings(_ sender: Any?) { openSettings() }
    func quit() { NSApp.terminate(nil) }
}
