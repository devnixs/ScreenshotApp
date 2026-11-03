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

@MainActor
final class RecordingSession {
    let configuration: RecordingConfiguration
    let outputURL: URL
    private let onEnded: @MainActor () -> Void
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

    init(configuration: RecordingConfiguration, outputURL: URL,
         showOnboarding: @escaping @MainActor () -> Void, onEnded: @escaping @MainActor () -> Void) {
        self.configuration = configuration
        self.outputURL = outputURL
        self.showOnboarding = showOnboarding
        self.onEnded = onEnded
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
        case .stopped(let failure): send(.streamStopped(failure))
        }
    }

    private var fileHasContent: Bool {
        ((try? outputURL.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) > 0
    }

    private func send(_ event: RecordingEvent) {
        for effect in lifecycle.handle(event) {
            switch effect {
            case .startCapture:
                observeDisplays()
                Task { await startCapture() }
            case .stopCapture:
                let stream = stream
                Task { try? await stream?.stopCapture() }
            case .scheduleStopTimeout:
                stopTimeoutTask = Task { [weak self] in
                    do { try await Task.sleep(for: .seconds(RecordingLifecycle.stopTimeout)) }
                    catch { return }
                    guard let self else { return }
                    send(.stopTimedOut(fileHasContent: fileHasContent))
                }
            case .reveal(let url): NSWorkspace.shared.activateFileViewerSelecting([url])
            case .deleteFile(let url): try? FileManager.default.removeItem(at: url)
            case .showError(let failure): ErrorAlerts.showError(title: "Couldn't record the screen", error: failure)
            case .showOnboarding: showOnboarding()
            case .ended:
                stopTimeoutTask?.cancel()
                stopTimeoutTask = nil
                if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
                screenObserver = nil
                // Keep SCK objects until finalization; release them only after a terminal event.
                stream = nil
                recordingOutput = nil
                onEnded()
            }
        }
    }

    private func startCapture() async {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            guard let display = content.displays.first(where: { $0.displayID == configuration.displayID }) else {
                throw ScreenshotCaptureError.displayUnavailable
            }
            let ownApp = content.applications.first { $0.processID == getpid() }
                ?? content.applications.first { $0.bundleIdentifier == Bundle.main.bundleIdentifier }
            // If absent, no Snapper windows are on screen. 0012 must keep a window present
            // before startup or updateContentFilter before showing the pill and border.
            let filter = SCContentFilter(display: display, excludingApplications: ownApp.map { [$0] } ?? [], exceptingWindows: [])
            let config = SCStreamConfiguration()
            config.sourceRect = configuration.sourceRect
            config.width = configuration.pixelWidth
            config.height = configuration.pixelHeight
            config.minimumFrameInterval = configuration.minimumFrameInterval
            config.pixelFormat = configuration.pixelFormat
            config.queueDepth = configuration.queueDepth
            config.showsCursor = configuration.showsCursor
            config.scalesToFit = false
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
                guard let self else { return }
                if RecordingLifecycle.isRecordedDisplayGone(recorded: self.configuration.displayID,
                    current: DisplayInfo.allCurrent().map(\.displayID)) { self.stop() }
            }
        }
    }
}
