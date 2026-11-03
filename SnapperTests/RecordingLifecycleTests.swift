import Foundation
import Testing
@testable import Snapper

struct RecordingLifecycleTests {
    private let url = URL(fileURLWithPath: "/recording.mp4")
    private let failure = RecordingFailure(message: "Writer failed", kind: .other)
    private let denied = RecordingFailure(message: "Permission denied", kind: .permissionDenied)

    private func started() -> RecordingLifecycle {
        var lifecycle = RecordingLifecycle()
        #expect(lifecycle.handle(.start(url: url)) == [.startCapture])
        #expect(lifecycle.phase == .starting)
        #expect(lifecycle.handle(.captureStarted) == [])
        #expect(lifecycle.phase == .recording)
        return lifecycle
    }

    @Test func happyPath() {
        var lifecycle = started()
        #expect(lifecycle.handle(.stopRequested) == [.stopCapture, .scheduleStopTimeout])
        #expect(lifecycle.phase == .stopping)
        #expect(lifecycle.handle(.outputFinished) == [.reveal(url), .ended])
        #expect(lifecycle.phase == .finished(url))
        #expect(RecordingLifecycle.stopTimeout == 5)
    }

    @Test func spontaneousWriterCompletionStopsTheStream() {
        var lifecycle = started()
        #expect(lifecycle.handle(.outputFinished) == [.stopCapture, .reveal(url), .ended])
        #expect(lifecycle.phase == .finished(url))
    }

    @Test func doubleStopAndCancelAfterStopAreIgnored() {
        var lifecycle = started()
        _ = lifecycle.handle(.stopRequested)
        #expect(lifecycle.handle(.stopRequested) == [])
        #expect(lifecycle.handle(.cancelRequested) == [])
        #expect(lifecycle.handle(.streamStopped(failure)) == [])
        #expect(lifecycle.phase == .stopping)
        #expect(lifecycle.handle(.outputFinished) == [.reveal(url), .ended])
    }

    @Test func stopWhileStarting() {
        var lifecycle = RecordingLifecycle()
        _ = lifecycle.handle(.start(url: url))
        #expect(lifecycle.handle(.stopRequested) == [])
        #expect(lifecycle.phase == .starting)
        #expect(lifecycle.handle(.captureStarted) == [.stopCapture, .scheduleStopTimeout])
        #expect(lifecycle.phase == .stopping)
    }

    @Test func cancellationRemovesFile() {
        var lifecycle = started()
        #expect(lifecycle.handle(.cancelRequested) == [.stopCapture, .scheduleStopTimeout])
        #expect(lifecycle.handle(.outputFinished) == [.deleteFile(url), .ended])
        #expect(lifecycle.phase == .cancelled)
    }

    @Test(arguments: [true, false]) func cancellationWinsPendingStop(cancelFirst: Bool) {
        var lifecycle = RecordingLifecycle()
        _ = lifecycle.handle(.start(url: url))
        let events: [RecordingEvent] = cancelFirst ? [.cancelRequested, .stopRequested] : [.stopRequested, .cancelRequested]
        for event in events { #expect(lifecycle.handle(event) == []) }
        #expect(lifecycle.handle(.captureStarted) == [.stopCapture, .scheduleStopTimeout])
        #expect(lifecycle.handle(.outputFinished) == [.deleteFile(url), .ended])
        #expect(lifecycle.phase == .cancelled)
    }

    @Test func failedStartRoutesPermissionAndOtherErrors() {
        for failure in [failure, denied] {
            var lifecycle = RecordingLifecycle()
            _ = lifecycle.handle(.start(url: url))
            #expect(lifecycle.handle(.captureFailedToStart(failure)) == [
                .deleteFile(url), failure.kind == .permissionDenied ? .showOnboarding : .showError(failure), .ended])
            #expect(lifecycle.phase == .failed(failure))
        }
    }

    @Test func failedStartWithPendingStopStillEnds() {
        var lifecycle = RecordingLifecycle()
        _ = lifecycle.handle(.start(url: url))
        _ = lifecycle.handle(.stopRequested)
        #expect(lifecycle.handle(.captureFailedToStart(failure)) == [.deleteFile(url), .showError(failure), .ended])
    }

    @Test func streamAlreadyStoppedWaitsForWriter() {
        var lifecycle = started()
        #expect(lifecycle.handle(.streamStopped(failure)) == [.scheduleStopTimeout])
        #expect(lifecycle.phase == .stopping)
        #expect(lifecycle.handle(.stopRequested) == [])
        #expect(lifecycle.handle(.streamStopped(failure)) == [])
        #expect(lifecycle.handle(.outputFinished) == [.reveal(url), .ended])
    }

    @Test(arguments: [true, false]) func timeoutPreservesOnlyContent(content: Bool) {
        var lifecycle = started()
        _ = lifecycle.handle(.stopRequested)
        #expect(lifecycle.handle(.stopTimedOut(fileHasContent: content)) == [
            content ? .reveal(url) : .deleteFile(url), .showError(.timeout), .ended])
        #expect(lifecycle.phase == (content ? .finished(url) : .failed(.timeout)))
    }

    @Test(arguments: [true, false]) func writerFailureDuringRecording(content: Bool) {
        var lifecycle = started()
        #expect(lifecycle.handle(.outputFailed(failure, fileHasContent: content)) == [
            .stopCapture, content ? .reveal(url) : .deleteFile(url), .showError(failure), .ended])
        #expect(lifecycle.phase == (content ? .finished(url) : .failed(failure)))
    }

    @Test(arguments: [true, false]) func writerFailureDuringStop(content: Bool) {
        var lifecycle = started()
        _ = lifecycle.handle(.stopRequested)
        #expect(lifecycle.handle(.outputFailed(failure, fileHasContent: content)) == [
            content ? .reveal(url) : .deleteFile(url), .showError(failure), .ended])
    }

    @Test(arguments: [true, false]) func cancellationDiscardsEvenOnFailureOrTimeout(content: Bool) {
        for event: RecordingEvent in [.outputFailed(failure, fileHasContent: content), .stopTimedOut(fileHasContent: content)] {
            var lifecycle = started()
            _ = lifecycle.handle(.cancelRequested)
            #expect(lifecycle.handle(event) == [.deleteFile(url), .ended])
            #expect(lifecycle.phase == .cancelled)
        }
    }

    @Test func terminalCallbacksAndStopsAreIdempotent() {
        for ending: RecordingEvent in [.outputFinished, .outputFailed(failure, fileHasContent: false), .cancelRequested] {
            var lifecycle = started()
            if ending == .cancelRequested {
                _ = lifecycle.handle(ending)
                _ = lifecycle.handle(.outputFinished)
            } else {
                _ = lifecycle.handle(.stopRequested)
                _ = lifecycle.handle(ending)
            }
            let terminalPhase = lifecycle.phase
            for event: RecordingEvent in [.start(url: url), .captureStarted, .captureFailedToStart(denied),
                .stopRequested, .cancelRequested, .outputFinished, .outputFailed(failure, fileHasContent: true),
                .streamStopped(failure), .stopTimedOut(fileHasContent: false)] {
                #expect(lifecycle.handle(event) == [])
                #expect(lifecycle.phase == terminalPhase)
            }
        }
    }

    @Test func idleIgnoresCallbacksAndStops() {
        var lifecycle = RecordingLifecycle()
        for event: RecordingEvent in [.stopRequested, .cancelRequested, .captureStarted, .outputFinished,
            .streamStopped(failure), .outputFailed(failure, fileHasContent: false), .stopTimedOut(fileHasContent: true)] {
            #expect(lifecycle.handle(event) == [])
            #expect(lifecycle.phase == .idle)
        }
    }

    @Test func displayDisconnection() {
        #expect(!RecordingLifecycle.isRecordedDisplayGone(recorded: 2, current: [1, 2]))
        #expect(RecordingLifecycle.isRecordedDisplayGone(recorded: 2, current: [1]))
        #expect(RecordingLifecycle.isRecordedDisplayGone(recorded: 2, current: []))
    }
}
