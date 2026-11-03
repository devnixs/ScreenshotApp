import AppKit
import KeyboardShortcuts
import os

@MainActor
final class AppCoordinator: NSObject {
    let settings = AppSettings()
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "AppMode")
    private let overlay = SelectionOverlayController()
    private let capturer = ScreenshotCapturer()
    private let shutterSound = ShutterSound()
    private let screenshotLogger = Logger(subsystem: "com.thera-engineering.snapper", category: "screenshot")
    private lazy var annotationWindowManager = AnnotationWindowManager(settings: settings)
    private lazy var settingsModel = SettingsModel(settings: settings)
    private lazy var settingsWindowController = SettingsWindowController(model: settingsModel)
    private lazy var onboardingWindowController = OnboardingWindowController(settings: settings)
    private var statusItemController: StatusItemController?
    private var modeMachine = AppModeMachine()
    private var recordingSession: RecordingSession?
    private var recordingOverlay: RecordingOverlayController?
    private var windowFrameTracker: WindowFrameTracker?
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
            stopRecording()
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
            guard let result = await overlay.selectRegion(for: purpose) else {
                send(.selectionCancelled)
                return
            }
            let startDate = Date()
            send(.selectionFinished)
            if purpose == .record {
                startRecording(result, startDate: startDate)
                return
            }
            await takeScreenshot(purpose: purpose, selection: result, captureDate: startDate)
        }
    }

    private func startRecording(_ selection: SelectionResult, startDate: Date) {
        let display: DisplayInfo
        let rect: CGRect
        let source: RecordingSource
        let followsWindow: Bool
        switch selection {
        case .rect(let selectedDisplay, let selectedRect):
            display = selectedDisplay
            rect = selectedRect
            followsWindow = false
            source = .rectangle(RecordingConfigurationBuilder.build(display: display, selection: rect))
        case .window(let id, let frame, let selectedDisplay):
            followsWindow = true
            source = .window(id)
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
            let exportSettings = RecordingExportSettings(format: settings.recordingFormat,
                mp4Quality: settings.mp4Quality, gifQuality: settings.gifQuality)
            let initialPlan = exportSettings.plan(pixelWidth: 2, pixelHeight: 2, pointPixelScale: 1)
            let url = try folder.uniqueFileURL(for: .recording(initialPlan.format), date: startDate)
            let session = RecordingSession(
                source: source, finalURL: url, startDate: startDate, exportSettings: exportSettings, settings: settings,
                showOnboarding: { [weak self] in self?.showOnboarding() },
                onStarted: { [weak self] in
                    guard let self else { return }
                    // Both the app-excluding display filter and the single-window filter exclude these controls.
                    let overlay = RecordingOverlayController(display: display, recordedRect: rect, followsWindow: followsWindow, startedAt: .now,
                        onStop: { [weak self] in self?.stopRecording() },
                        onCancel: { [weak self] in self?.cancelRecording() })
                    self.recordingOverlay = overlay
                    overlay.show()
                    if case .window(let id) = source {
                        let tracker = WindowFrameTracker(windowID: id, initialFrame: rect) { [weak self] event in
                            self?.handleWindowTracking(event)
                        }
                        self.windowFrameTracker = tracker
                        tracker.start()
                    }
                },
                onStopping: { [weak self] in self?.dismissRecordingOverlay() },
                onEnded: { [weak self] in self?.recordingEnded() })
            recordingSession = session
            statusItemController?.setRecording(true)
            session.start()
        } catch {
            send(.recordingEnded)
            _ = ErrorAlerts.showSaveFolderFailure(folder: settings.saveFolderURL, error: error, settings: settings)
        }
    }

    private func handleWindowTracking(_ event: WindowTrackingEvent) {
        switch event {
        case .moved(let rect), .shown(let rect):
            let displays = DisplayInfo.allCurrent()
            guard let display = DisplayGeometry.display(overlapping: rect, in: displays) else {
                recordingOverlay?.setBorderVisible(false)
                return
            }
            recordingOverlay?.update(recordedRect: rect, on: display)
            recordingOverlay?.setBorderVisible(true)
        case .hidden: recordingOverlay?.setBorderVisible(false)
        case .closed: stopRecording()
        case .unchanged: break
        }
    }

    private func dismissRecordingOverlay() {
        windowFrameTracker?.stop()
        windowFrameTracker = nil
        recordingOverlay?.dismiss()
        recordingOverlay = nil
    }

    private func stopRecording() {
        dismissRecordingOverlay()
        recordingSession?.stop()
    }

    private func cancelRecording() {
        dismissRecordingOverlay()
        recordingSession?.cancel()
    }

    private func recordingEnded() {
        dismissRecordingOverlay()
        recordingSession = nil
        send(.recordingEnded)
        statusItemController?.setRecording(false)
        if termination.recordingEnded() { NSApp.reply(toApplicationShouldTerminate: true) }
    }

    func applicationShouldTerminate() -> NSApplication.TerminateReply {
        annotationWindowManager.commitAllTextEdits()
        if case .confirm(let message) = UnsavedAnnotationsPolicy.quitDecision(dirtyWindowCount: annotationWindowManager.dirtyWindowCount) {
            NSApp.activate()
            let alert = NSAlert()
            alert.messageText = message
            alert.alertStyle = .warning
            alert.addButton(withTitle: UnsavedAnnotationsPolicy.quitButton).hasDestructiveAction = true
            alert.addButton(withTitle: UnsavedAnnotationsPolicy.cancelButton)
            guard alert.runModal() == .alertFirstButtonReturn else { return .terminateCancel }
        }
        if termination.request(recordingActive: recordingSession != nil) {
            stopRecording()
            return .terminateLater
        }
        return .terminateNow
    }

    private func capture(_ selection: SelectionResult) async throws -> CapturedImage {
        switch selection {
        case .rect(let display, let rect):
            let region = DisplayGeometry.captureRegion(for: rect, on: display)
            guard region.pixelSize.width > 0, region.pixelSize.height > 0 else { throw ScreenshotCaptureError.displayUnavailable }
            return try await capturer.capture(region, pixelsPerPoint: display.backingScale)
        case .window(let id, _, _):
            return try await capturer.captureWindowImage(windowID: id)
        }
    }

    private func takeScreenshot(purpose: CapturePurpose, selection: SelectionResult, captureDate: Date) async {
        let image: CapturedImage
        do {
            image = try await capture(selection)
        } catch {
            screenshotLogger.error("Capture failed: \(error.localizedDescription, privacy: .public)")
            if CaptureFailure(error) == .permissionDenied { showOnboarding() }
            else { ErrorAlerts.showCaptureFailure(error) }
            return
        }
        if settings.playShutterSound { shutterSound.play() }
        if purpose == .annotate {
            let displayID: CGDirectDisplayID
            switch selection {
            case .rect(let display, _), .window(_, _, let display): displayID = display.displayID
            }
            annotationWindowManager.open(image, displayID: displayID, captureDate: Date())
            return
        }
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
