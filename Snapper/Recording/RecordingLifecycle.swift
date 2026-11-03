import Foundation
import CoreGraphics

struct RecordingFailure: Error, LocalizedError, Equatable, Sendable {
    enum Kind: Equatable, Sendable { case permissionDenied, other }
    let message: String
    let kind: Kind
    var title: String = "Couldn't record the screen"
    static func compression(_ message: String) -> RecordingFailure {
        RecordingFailure(message: message, kind: .other, title: "Couldn't compress the recording")
    }
    static func gifConversion(_ message: String) -> RecordingFailure {
        RecordingFailure(message: message, kind: .other, title: "Couldn't create the GIF")
    }
    var errorDescription: String? { message }
    static let timeout = RecordingFailure(message: "The recording didn't finish saving in time.", kind: .other)
}

enum RecordingPhase: Equatable, Sendable {
    case idle, starting, recording, stopping, converting, finished(URL), failed(RecordingFailure), cancelled
}
enum RecordingEvent: Equatable, Sendable {
    case start(url: URL, converts: Bool), captureStarted, captureFailedToStart(RecordingFailure)
    case stopRequested, cancelRequested, outputFinished
    case outputFailed(RecordingFailure, fileHasContent: Bool), streamStopped(RecordingFailure)
    case conversionFinished(URL), conversionFailed(RecordingFailure, savedOriginal: URL?)
    case windowStreamFailed(RecordingFailure)
    case stopTimedOut(fileHasContent: Bool)
}
enum RecordingEffect: Equatable, Sendable {
    case startCapture, stopCapture, scheduleStopTimeout, reveal(URL), deleteFile(URL)
    case releaseCapture, convert(URL), saveOriginal(URL)
    case showError(RecordingFailure), showOnboarding, ended
}

/// All callback ordering and terminal decisions live here, independently of SCK and UI.
struct RecordingLifecycle: Sendable {
    static let stopTimeout: Double = 5
    private(set) var phase: RecordingPhase = .idle
    private var url: URL?
    private var converts = false
    private var pendingStop = false
    private var pendingCancel = false
    private var streamFailure: RecordingFailure?

    static func isRecordedDisplayGone(recorded: CGDirectDisplayID, current: [CGDirectDisplayID]) -> Bool {
        !current.contains(recorded)
    }

    mutating func handle(_ event: RecordingEvent) -> [RecordingEffect] {
        switch phase {
        case .finished, .failed, .cancelled: return []
        default: break
        }
        if case .start(let url, let converts) = event, phase == .idle {
            self.url = url
            self.converts = converts
            phase = .starting
            return [.startCapture]
        }
        guard let url else { return [] }
        switch (phase, event) {
        case (.starting, .stopRequested): pendingStop = true; return []
        case (.starting, .cancelRequested): pendingCancel = true; return []
        case (.starting, .captureStarted):
            phase = pendingStop || pendingCancel ? .stopping : .recording
            return phase == .stopping ? [.stopCapture, .scheduleStopTimeout] : []
        case (.starting, .captureFailedToStart(let failure)):
            phase = .failed(failure)
            return [.deleteFile(url), failure.kind == .permissionDenied ? .showOnboarding : .showError(failure), .ended]
        case (.recording, .stopRequested), (.recording, .cancelRequested):
            pendingCancel = event == .cancelRequested
            phase = .stopping
            return [.stopCapture, .scheduleStopTimeout]
        case (.recording, .windowStreamFailed(let failure)):
            streamFailure = failure
            phase = .stopping
            return [.scheduleStopTimeout]
        case (.recording, .streamStopped):
            phase = .stopping
            return [.scheduleStopTimeout]
        case (.recording, .outputFinished):
            if converts {
                phase = .converting
                return [.stopCapture, .releaseCapture, .convert(url)]
            }
            phase = .finished(url)
            return [.stopCapture, .reveal(url), .ended]
        case (.stopping, .outputFinished):
            if let streamFailure { return resolveFailure(streamFailure, fileHasContent: true, url: url) }
            if converts && !pendingCancel {
                phase = .converting
                return [.releaseCapture, .convert(url)]
            }
            phase = pendingCancel ? .cancelled : .finished(url)
            return [pendingCancel ? .deleteFile(url) : .reveal(url), .ended]
        case (.recording, .outputFailed(let failure, let content)):
            return [.stopCapture] + resolveFailure(failure, fileHasContent: content, url: url)
        case (.stopping, .outputFailed(let failure, let content)):
            return resolveFailure(failure, fileHasContent: content, url: url)
        case (.stopping, .stopTimedOut(let content)):
            return resolveFailure(streamFailure ?? .timeout, fileHasContent: content, url: url)
        case (.converting, .conversionFinished(let final)):
            phase = .finished(final)
            return [.deleteFile(url), .reveal(final), .ended]
        case (.converting, .conversionFailed(let failure, let saved)):
            phase = saved.map { .finished($0) } ?? .failed(failure)
            return [saved.map { .reveal($0) } ?? .deleteFile(url), .showError(failure), .ended]
        default: return []
        }
    }

    private mutating func resolveFailure(_ failure: RecordingFailure, fileHasContent: Bool, url: URL) -> [RecordingEffect] {
        if pendingCancel {
            phase = .cancelled
            return [.deleteFile(url), .ended]
        }
        phase = fileHasContent ? .finished(url) : .failed(failure)
        return [fileHasContent ? (converts ? .saveOriginal(url) : .reveal(url)) : .deleteFile(url), .showError(failure), .ended]
    }
}
