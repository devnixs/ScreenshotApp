import AppKit
import KeyboardShortcuts
import os

@MainActor
final class AppCoordinator: NSObject {
    let settings = AppSettings()
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "AppMode")
    private let selectionLogger = Logger(subsystem: "com.thera-engineering.snapper", category: "selection")
    private let overlay = SelectionOverlayController()
    private let capturer = ScreenshotCapturer()
    private let shutterSound = ShutterSound()
    private let screenshotLogger = Logger(subsystem: "com.thera-engineering.snapper", category: "screenshot")
    private lazy var settingsModel = SettingsModel(settings: settings)
    private lazy var settingsWindowController = SettingsWindowController(model: settingsModel)
    private lazy var onboardingWindowController = OnboardingWindowController(settings: settings)
    private var statusItemController: StatusItemController?
    private var modeMachine = AppModeMachine()
    private var recordingSession: RecordingSession?
    private var termination = RecordingTerminationDecision()

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
        case .completeSelection: break // The overlay result carries the region to the pipeline.
        case .stopRecording:
            recordingSession?.stop()
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
            let startDate = Date()
            send(.selectionFinished)
            if purpose == .record {
                startRecording(result, startDate: startDate)
                return
            }
            switch result {
            case .rect(let display, let rect):
                selectionLogger.info("Selection for \(purpose.rawValue, privacy: .public): display \(display.displayID), rect \(String(describing: rect), privacy: .public)")
                if purpose == .screenshot {
                    let region = DisplayGeometry.captureRegion(for: rect, on: display)
                    guard region.pixelSize.width > 0, region.pixelSize.height > 0 else { return }
                    await takeScreenshot { try await self.capturer.capture(region, pixelsPerPoint: display.backingScale) }
                } else if purpose == .annotate {
                    selectionLogger.info("Annotate not implemented yet")
                }
            case .window(let windowID, _, _):
                selectionLogger.info("Window selection for \(purpose.rawValue, privacy: .public): \(windowID)")
                if purpose == .screenshot {
                    await takeScreenshot { try await self.capturer.captureWindowImage(windowID: windowID) }
                } else if purpose == .annotate {
                    selectionLogger.info("Annotate not implemented yet")
                }
            }
        }
    }

    private func startRecording(_ selection: SelectionResult, startDate: Date) {
        let display: DisplayInfo
        let rect: CGRect
        switch selection {
        case .rect(let selectedDisplay, let selectedRect):
            display = selectedDisplay
            rect = selectedRect
        case .window(_, let frame, let selectedDisplay):
            // 0013: replace this fixed display rectangle with a following window filter.
            display = selectedDisplay
            guard let height = DisplayGeometry.primaryHeight(in: DisplayInfo.allCurrent()) else {
                send(.recordingEnded)
                ErrorAlerts.showError(title: "Couldn't record the screen", error: ScreenshotCaptureError.displayUnavailable)
                return
            }
            rect = DisplayGeometry.appKitRect(fromCG: frame, primaryHeight: height)
        }
        do {
            let folder = SaveFolder(url: settings.saveFolderURL)
            _ = try folder.prepareForWriting()
            let url = try folder.uniqueFileURL(for: .recording, date: startDate)
            let session = RecordingSession(
                configuration: RecordingConfigurationBuilder.build(display: display, selection: rect), outputURL: url,
                showOnboarding: { [weak self] in self?.showOnboarding() },
                onEnded: { [weak self] in self?.recordingEnded() })
            recordingSession = session
            statusItemController?.setRecording(true)
            session.start()
        } catch {
            send(.recordingEnded)
            _ = ErrorAlerts.showSaveFolderFailure(folder: settings.saveFolderURL, error: error, settings: settings)
        }
    }

    private func recordingEnded() {
        recordingSession = nil
        send(.recordingEnded)
        statusItemController?.setRecording(false)
        if termination.recordingEnded() { NSApp.reply(toApplicationShouldTerminate: true) }
    }

    func applicationShouldTerminate() -> NSApplication.TerminateReply {
        if termination.request(recordingActive: recordingSession != nil) {
            recordingSession?.stop()
            return .terminateLater
        }
        return .terminateNow
    }

    private func takeScreenshot(capture: @MainActor () async throws -> CapturedImage) async {
        let captureDate = Date()
        let image: CapturedImage
        do {
            image = try await capture()
        } catch {
            screenshotLogger.error("Capture failed: \(error.localizedDescription, privacy: .public)")
            if CaptureFailure(error) == .permissionDenied { showOnboarding() }
            else { ErrorAlerts.showCaptureFailure(error) }
            return
        }
        if settings.playShutterSound { shutterSound.play() }
        let png: Data
        do {
            png = try await Task.detached(priority: .userInitiated) {
                try PNGEncoder.data(for: image.cgImage, pixelsPerPoint: image.pixelsPerPoint)
            }.value
        } catch {
            ErrorAlerts.showCaptureFailure(error)
            screenshotLogger.error("PNG encoding failed: \(error.localizedDescription, privacy: .public)")
            return
        }
        let plan = ScreenshotOutputPlan(destination: settings.screenshotDestination)
        if plan.copiesToClipboard {
            if Clipboard.copy(pngData: png, image: image.cgImage) {
                screenshotLogger.info("Screenshot copied to clipboard")
            } else {
                let error = NSError(domain: "com.thera-engineering.snapper.clipboard", code: 1,
                                    userInfo: [NSLocalizedDescriptionKey: "The clipboard couldn't accept the screenshot."])
                screenshotLogger.error("Clipboard write failed")
                ErrorAlerts.showError(title: "Couldn't copy to clipboard", error: error)
            }
        }
        guard plan.savesToFile else { return }
        var folder = settings.saveFolderURL
        // One retry for this pending image. A second folder selection updates future captures only.
        for attempt in 0...1 {
            do {
                let url = try ScreenshotFileWriter.save(png, to: folder, captureDate: captureDate)
                screenshotLogger.info("Screenshot saved: \(url.path, privacy: .public)")
                return
            } catch {
                screenshotLogger.error("Screenshot save failed: \(error.localizedDescription, privacy: .public)")
                switch ScreenshotOutputPlan.saveFailureAlert(for: error) {
                case .saveFolder:
                    guard let chosen = ErrorAlerts.showSaveFolderFailure(folder: folder, error: error, settings: settings),
                          attempt == 0 else { return }
                    folder = chosen
                case .generic:
                    ErrorAlerts.showError(title: "Couldn't save screenshot", error: error)
                    return
                }
            }
        }
    }

    func showOnboarding() { onboardingWindowController.show() }

    func openSettings() { settingsWindowController.show() }
    @objc func openSettings(_ sender: Any?) { openSettings() }
    func quit() { NSApp.terminate(nil) }
}
