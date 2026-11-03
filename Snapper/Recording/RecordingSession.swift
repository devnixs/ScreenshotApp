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
    private let finalURL: URL
    private let startDate: Date
    private let exportSettings: RecordingExportSettings
    private var outputPlan: RecordingOutputPlan
    private let settings: AppSettings
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

    init(source: RecordingSource, finalURL: URL, startDate: Date,
         exportSettings: RecordingExportSettings, settings: AppSettings,
         showOnboarding: @escaping @MainActor () -> Void,
         onStarted: @escaping @MainActor () -> Void, onStopping: @escaping @MainActor () -> Void,
         onEnded: @escaping @MainActor () -> Void) {
        self.settings = settings
        self.source = source
        self.finalURL = finalURL
        self.startDate = startDate
        self.exportSettings = exportSettings
        let initialPlan = exportSettings.plan(pixelWidth: 2, pixelHeight: 2, pointPixelScale: 1)
        self.outputPlan = initialPlan
        self.outputURL = initialPlan.transcode == nil ? finalURL
            : FileManager.default.temporaryDirectory.appendingPathComponent("Snapper-\(UUID().uuidString).mp4")
        self.showOnboarding = showOnboarding
        self.onEnded = onEnded
        self.onStarted = onStarted
        self.onStopping = onStopping
    }

    func start() { send(.start(url: outputURL, converts: outputPlan.transcode != nil)) }
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
            case .showError(let failure): ErrorAlerts.showError(title: failure.title, error: failure)
            case .showOnboarding: showOnboarding()
            case .releaseCapture: releaseCapture()
            case .convert(let url): convert(url)
            case .saveOriginal(let url):
                releaseCapture()
                if let saved = saveOriginal(url) { NSWorkspace.shared.activateFileViewerSelecting([saved]) }
            case .ended:
                releaseCapture()
                onEnded()
            }
        }
    }

    private func releaseCapture() {
        stopTimeoutTask?.cancel()
        stopTimeoutTask = nil
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
        screenObserver = nil
        removeRecordingOutput()
        stream = nil
        recordingOutput = nil
    }

    private func saveOriginal(_ capture: URL) -> URL? {
        do {
            let destination = try SaveFolder(url: finalURL.deletingLastPathComponent())
                .uniqueFileURL(for: .recording(.mp4), date: startDate)
            try FileManager.default.moveItem(at: capture, to: destination)
            return destination
        } catch {
            logger.error("Saving original recording: \(error.localizedDescription, privacy: .public)")
            // The folder may have vanished; don't leave a temporary capture behind.
            try? FileManager.default.removeItem(at: capture)
            return nil
        }
    }

    private func convert(_ capture: URL) {
        guard let transcode = outputPlan.transcode else { return }
        let converted = capture.deletingPathExtension().appendingPathExtension("out.\(outputPlan.finalExtension)")
        Task {
            do {
                try await Task.detached(priority: .userInitiated) {
                    switch transcode {
                    case .mp4(let bitrate):
                        try await MP4Transcoder.transcode(input: capture, output: converted, averageBitRate: bitrate)
                    case .gif(let settings, let width, let height):
                        try await GIFEncoder.encode(input: capture, output: converted, settings: settings,
                                                    width: width, height: height)
                    }
                }.value
                let folder = SaveFolder(url: finalURL.deletingLastPathComponent())
                // Resolve again immediately before moving; moveItem never overwrites a collision.
                var destination = finalURL
                while true {
                    do {
                        try FileManager.default.moveItem(at: converted, to: destination)
                        break
                    } catch let error as NSError where error.domain == NSCocoaErrorDomain && error.code == NSFileWriteFileExistsError {
                        destination = try folder.uniqueFileURL(for: .recording(outputPlan.format), date: startDate)
                    }
                }
                send(.conversionFinished(destination))
            } catch {
                try? FileManager.default.removeItem(at: converted)
                let saved = saveOriginal(capture)
                let failure: RecordingFailure = outputPlan.format == .gif
                    ? .gifConversion(error.localizedDescription) : .compression(error.localizedDescription)
                send(.conversionFailed(failure, savedOriginal: saved))
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
            // Snapshot fresh preferences and availability once per recording, after content lookup.
            let microphone = MicrophoneCaptureInputs(
                recordMicrophoneSetting: settings.recordMicrophone, format: exportSettings.format,
                authorization: MicrophonePermission.authorization,
                hasAudioInputDevice: MicrophonePermission.hasAudioInputDevice)
            let config: SCStreamConfiguration
            let pointPixelScale: CGFloat
            switch source {
            case .rectangle(let configuration):
                guard let display = content.displays.first(where: { $0.displayID == configuration.displayID }) else {
                    throw ScreenshotCaptureError.displayUnavailable
                }
                guard let ownApp = content.applications.first(where: { $0.processID == getpid() }) else {
                    throw RecordingFailure(message: "Couldn't exclude Snapper's recording controls from capture.", kind: .other)
                }
                filter = SCContentFilter(display: display, excludingApplications: [ownApp], exceptingWindows: [])
                config = RecordingConfigurationBuilder.buildStream(rectangle: configuration, microphone: microphone)
                pointPixelScale = CGFloat(configuration.pixelWidth) / configuration.sourceRect.width
            case .window(let windowID):
                guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
                    throw ScreenshotCaptureError.windowUnavailable
                }
                // Captures only this window: Snapper's pill and border are excluded by construction.
                filter = SCContentFilter(desktopIndependentWindow: window)
                let configuration = RecordingConfigurationBuilder.buildWindow(
                    contentSize: filter.contentRect.size, pointPixelScale: CGFloat(filter.pointPixelScale))
                config = RecordingConfigurationBuilder.buildStream(window: configuration, microphone: microphone)
                pointPixelScale = CGFloat(filter.pointPixelScale)
            }
            outputPlan = exportSettings.plan(pixelWidth: config.width, pixelHeight: config.height,
                                             pointPixelScale: pointPixelScale)
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
