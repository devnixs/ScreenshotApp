import AppKit
import AVFoundation
import ScreenCaptureKit
import os

/// The delegate has immutable state; framework callbacks never touch UI directly.
private final class RecordingDelegateProxy: NSObject, SCStreamDelegate, SCRecordingOutputDelegate, Sendable {
    enum Callback: Sendable { case finished, failed(RecordingFailure), stopped(RecordingFailure) }
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "recording")
    let receive: @Sendable (Callback) -> Void
    init(receive: @escaping @Sendable (Callback) -> Void) { self.receive = receive }
    func recordingOutputDidStartRecording(_ recordingOutput: SCRecordingOutput) {
        logger.info("Recording output started")
    }
    func recordingOutputDidFinishRecording(_ recordingOutput: SCRecordingOutput) { receive(.finished) }
    func recordingOutput(_ recordingOutput: SCRecordingOutput, didFailWithError error: Error) {
        receive(.failed(RecordingFailure(message: error.localizedDescription, kind: .other)))
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) {
        receive(.stopped(RecordingFailure(message: error.localizedDescription, kind: .other)))
    }
}

enum RecordingSource {
    case rectangle(RecordingConfiguration)
    case window(CGWindowID)
}

@MainActor
final class RecordingSession {
    let source: RecordingSource
    let outputURL: URL
    private let onEnded: @MainActor () -> Void
    private let onStarted: @MainActor () -> Void
    private let onStopping: @MainActor () -> Void
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "recording")
    private let showOnboarding: @MainActor () -> Void
    private var lifecycle = RecordingLifecycle()
    private var stream: SCStream?
    private var recordingOutput: SCRecordingOutput?
    private var stopTimeoutTask: Task<Void, Never>?
    private var screenObserver: NSObjectProtocol?
    private var startup = RecordingStartupBuffer<RecordingDelegateProxy.Callback>()
    private lazy var proxy = RecordingDelegateProxy { [weak self] callback in
        Task { @MainActor in self?.receive(callback) }
    }

    init(source: RecordingSource, outputURL: URL,
         showOnboarding: @escaping @MainActor () -> Void,
         onStarted: @escaping @MainActor () -> Void, onStopping: @escaping @MainActor () -> Void,
         onEnded: @escaping @MainActor () -> Void) {
        self.source = source
        self.outputURL = outputURL
        self.showOnboarding = showOnboarding
        self.onEnded = onEnded
        self.onStarted = onStarted
        self.onStopping = onStopping
    }

    func start() { send(.start(url: outputURL)) }
    func stop() { send(.stopRequested) }
    func cancel() { send(.cancelRequested) }

    private func receive(_ callback: RecordingDelegateProxy.Callback) {
        // SCK can call the writer delegate before startCapture's async return resumes.
        guard startup.shouldDeliver(callback) else { return }
        switch callback {
        case .finished: send(.outputFinished)
        case .failed(let failure): send(.outputFailed(failure, fileHasContent: fileHasContent))
        case .stopped(let failure):
            if case .window(let id) = source {
                if WindowFrameTracker.poll(windowID: id) == .gone { stop() }
                else { send(.windowStreamFailed(failure)) }
            } else { send(.streamStopped(failure)) }
        }
    }

    private var fileHasContent: Bool {
        ((try? outputURL.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) > 0
    }

    private func send(_ event: RecordingEvent) {
        let effects = lifecycle.handle(event)
        // Dismiss before alerts/reveal, including spontaneous failures and display disconnect.
        if lifecycle.phase != .recording && lifecycle.phase != .starting { onStopping() }
        for effect in effects {
            switch effect {
            case .startCapture:
                observeDisplays()
                Task { await startCapture() }
            case .stopCapture:
                let stream = stream
                Task {
                    do { try await stream?.stopCapture() }
                    catch { logger.error("Stopping recording: \(error.localizedDescription, privacy: .public)") }
                }
            case .scheduleStopTimeout:
                stopTimeoutTask = Task { [weak self] in
                    do { try await Task.sleep(for: .seconds(RecordingLifecycle.stopTimeout)) }
                    catch { return }
                    guard let self else { return }
                    send(.stopTimedOut(fileHasContent: fileHasContent))
                }
            case .reveal(let url): NSWorkspace.shared.activateFileViewerSelecting([url])
            case .deleteFile(let url):
                removeRecordingOutput()
                do { try FileManager.default.removeItem(at: url) }
                catch let error as NSError where error.domain == NSCocoaErrorDomain && error.code == NSFileNoSuchFileError {}
                catch { logger.error("Deleting recording: \(error.localizedDescription, privacy: .public)") }
            case .showError(let failure): ErrorAlerts.showError(title: "Couldn't record the screen", error: failure)
            case .showOnboarding: showOnboarding()
            case .ended:
                stopTimeoutTask?.cancel()
                stopTimeoutTask = nil
                if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
                screenObserver = nil
                // Detach the writer after finalization and before releasing SCK resources.
                removeRecordingOutput()
                // Keep SCK objects until finalization; release them only after a terminal event.
                stream = nil
                recordingOutput = nil
                onEnded()
            }
        }
    }

    private func removeRecordingOutput() {
        guard let recordingOutput else { return }
        do { try stream?.removeRecordingOutput(recordingOutput) }
        catch { logger.error("Removing recording output: \(error.localizedDescription, privacy: .public)") }
        self.recordingOutput = nil
    }

    private func startCapture() async {
        do {
            // An offscreen ordered window makes this menu-bar process discoverable even when
            // selection windows are closed, so rectangle filters can exclude the whole app.
            let anchor = NSPanel(contentRect: CGRect(x: -100_000, y: -100_000, width: 1, height: 1),
                                 styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            anchor.isReleasedWhenClosed = false
            anchor.ignoresMouseEvents = true
            anchor.orderFrontRegardless()
            defer { anchor.close() }
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            let filter: SCContentFilter
            let config = SCStreamConfiguration()
            switch source {
            case .rectangle(let configuration):
                guard let display = content.displays.first(where: { $0.displayID == configuration.displayID }) else {
                    throw ScreenshotCaptureError.displayUnavailable
                }
                guard let ownApp = content.applications.first(where: { $0.processID == getpid() }) else {
                    throw RecordingFailure(message: "Couldn't exclude Snapper's recording controls from capture.", kind: .other)
                }
                filter = SCContentFilter(display: display, excludingApplications: [ownApp], exceptingWindows: [])
                config.sourceRect = configuration.sourceRect
                config.width = configuration.pixelWidth
                config.height = configuration.pixelHeight
                config.minimumFrameInterval = configuration.minimumFrameInterval
                config.pixelFormat = configuration.pixelFormat
                config.queueDepth = configuration.queueDepth
                config.showsCursor = configuration.showsCursor
                config.scalesToFit = false
            case .window(let windowID):
                guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
                    throw ScreenshotCaptureError.windowUnavailable
                }
                // Captures only this window: Snapper's pill and border are excluded by construction.
                filter = SCContentFilter(desktopIndependentWindow: window)
                let configuration = RecordingConfigurationBuilder.buildWindow(
                    contentSize: filter.contentRect.size, pointPixelScale: CGFloat(filter.pointPixelScale))
                config.width = configuration.pixelWidth
                config.height = configuration.pixelHeight
                config.scalesToFit = configuration.scalesToFit
                config.preservesAspectRatio = configuration.preservesAspectRatio
                config.ignoreShadowsSingleWindow = configuration.ignoresShadowsSingleWindow
                config.ignoreGlobalClipSingleWindow = configuration.ignoresGlobalClipSingleWindow
                config.showsCursor = configuration.showsCursor
                config.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(configuration.frameRate))
                config.pixelFormat = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
                config.queueDepth = 5
            }
            config.capturesAudio = false
            config.captureMicrophone = false
            let outputConfig = SCRecordingOutputConfiguration()
            outputConfig.outputURL = outputURL
            outputConfig.videoCodecType = .h264
            outputConfig.outputFileType = .mp4
            let output = SCRecordingOutput(configuration: outputConfig, delegate: proxy)
            let stream = SCStream(filter: filter, configuration: config, delegate: proxy)
            self.stream = stream
            recordingOutput = output
            try stream.addRecordingOutput(output)
            try await stream.startCapture()
            let callbacks = startup.complete(succeeded: true)
            send(.captureStarted)
            for callback in callbacks { receive(callback) }
            if lifecycle.phase == .recording { onStarted() }
        } catch {
            _ = startup.complete(succeeded: false)
            let denied = CaptureFailure(error) == .permissionDenied || !ScreenRecordingPermission.isGranted
            send(.captureFailedToStart(RecordingFailure(message: error.localizedDescription,
                                                       kind: denied ? .permissionDenied : .other)))
        }
    }

    private func observeDisplays() {
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                guard let self, case .rectangle(let configuration) = self.source else { return }
                if RecordingLifecycle.isRecordedDisplayGone(recorded: configuration.displayID,
                    current: DisplayInfo.allCurrent().map(\.displayID)) { self.stop() }
            }
        }
    }
}
