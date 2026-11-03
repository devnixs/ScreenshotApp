import Foundation
import Testing
@testable import Snapper

struct RecordingLifecycleTests {
    private let url = URL(fileURLWithPath: "/recording.mp4")
    private let failure = RecordingFailure(message: "Writer failed", kind: .other)
    private let denied = RecordingFailure(message: "Permission denied", kind: .permissionDenied)

    private func started() -> RecordingLifecycle {
        var lifecycle = RecordingLifecycle()
        #expect(lifecycle.handle(.start(url: url, converts: false)) == [.startCapture])
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
        _ = lifecycle.handle(.start(url: url, converts: false))
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
        _ = lifecycle.handle(.start(url: url, converts: false))
        let events: [RecordingEvent] = cancelFirst ? [.cancelRequested, .stopRequested] : [.stopRequested, .cancelRequested]
        for event in events { #expect(lifecycle.handle(event) == []) }
        #expect(lifecycle.handle(.captureStarted) == [.stopCapture, .scheduleStopTimeout])
        #expect(lifecycle.handle(.outputFinished) == [.deleteFile(url), .ended])
        #expect(lifecycle.phase == .cancelled)
    }

    @Test func failedStartRoutesPermissionAndOtherErrors() {
        for failure in [failure, denied] {
            var lifecycle = RecordingLifecycle()
            _ = lifecycle.handle(.start(url: url, converts: false))
            #expect(lifecycle.handle(.captureFailedToStart(failure)) == [
                .deleteFile(url), failure.kind == .permissionDenied ? .showOnboarding : .showError(failure), .ended])
            #expect(lifecycle.phase == .failed(failure))
        }
    }

    @Test func failedStartWithPendingStopStillEnds() {
        var lifecycle = RecordingLifecycle()
        _ = lifecycle.handle(.start(url: url, converts: false))
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

    @Test func windowStreamErrorReportsAfterFinalizing() {
        var lifecycle = started()
        #expect(lifecycle.handle(.windowStreamFailed(failure)) == [.scheduleStopTimeout])
        #expect(lifecycle.handle(.stopRequested) == [])
        #expect(lifecycle.handle(.windowStreamFailed(failure)) == [])
        #expect(lifecycle.handle(.outputFinished) == [.reveal(url), .showError(failure), .ended])
        #expect(lifecycle.handle(.outputFinished) == [])
    }

    @Test func duplicateWindowClosureSavesAndRevealsOnce() {
        var lifecycle = started()
        #expect(lifecycle.handle(.stopRequested) == [.stopCapture, .scheduleStopTimeout])
        #expect(lifecycle.handle(.stopRequested) == [])
        #expect(lifecycle.handle(.windowStreamFailed(failure)) == [])
        #expect(lifecycle.handle(.outputFinished) == [.reveal(url), .ended])
        #expect(lifecycle.handle(.stopRequested) == [])
        #expect(lifecycle.handle(.outputFinished) == [])
    }

    @Test(arguments: [true, false]) func windowStreamTimeoutRetainsOriginalFailure(content: Bool) {
        var lifecycle = started()
        _ = lifecycle.handle(.windowStreamFailed(failure))
        #expect(lifecycle.handle(.stopTimedOut(fileHasContent: content)) == [
            content ? .reveal(url) : .deleteFile(url), .showError(failure), .ended])
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
            for event: RecordingEvent in [.start(url: url, converts: false), .captureStarted, .captureFailedToStart(denied),
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
    private func convertingCapture() -> RecordingLifecycle {
        var lifecycle = RecordingLifecycle()
        #expect(lifecycle.handle(.start(url: url, converts: true)) == [.startCapture])
        _ = lifecycle.handle(.captureStarted)
        return lifecycle
    }

    @Test func stopBeginsConversionWithoutEnding() {
        var lifecycle = convertingCapture()
        _ = lifecycle.handle(.stopRequested)
        #expect(lifecycle.handle(.outputFinished) == [.releaseCapture, .convert(url)])
        #expect(lifecycle.phase == .converting)
    }
    @Test func spontaneousFinishBeginsConversionAndStopsStream() {
        var lifecycle = convertingCapture()
        #expect(lifecycle.handle(.outputFinished) == [.stopCapture, .releaseCapture, .convert(url)])
    }
    @Test func conversionSuccessDeletesCaptureAndEnds() {
        var lifecycle = convertingCapture()
        _ = lifecycle.handle(.outputFinished)
        let final = URL(fileURLWithPath: "/final.mp4")
        #expect(lifecycle.handle(.conversionFinished(final)) == [.deleteFile(url), .reveal(final), .ended])
        #expect(lifecycle.phase == .finished(final))
        #expect(lifecycle.handle(.conversionFinished(final)) == [])
    }
    @Test(arguments: [true, false]) func conversionFailureReportsAndEnds(originalSaved: Bool) {
        var lifecycle = convertingCapture()
        _ = lifecycle.handle(.outputFinished)
        let saved = originalSaved ? URL(fileURLWithPath: "/original.mp4") : nil
        let failure = RecordingFailure.compression("Compression failed")
        #expect(lifecycle.handle(.conversionFailed(failure, savedOriginal: saved)) == [
            saved.map { .reveal($0) } ?? .deleteFile(url), .showError(failure), .ended])
        #expect(lifecycle.phase == (saved.map { .finished($0) } ?? .failed(failure)))
    }
    @Test func conversionIgnoresCaptureEventsAndUserRequests() {
        var lifecycle = convertingCapture()
        _ = lifecycle.handle(.outputFinished)
        for event: RecordingEvent in [.stopRequested, .cancelRequested, .stopTimedOut(fileHasContent: true),
            .captureStarted, .outputFinished, .outputFailed(failure, fileHasContent: true),
            .streamStopped(failure), .windowStreamFailed(failure)] {
            #expect(lifecycle.handle(event) == [])
            #expect(lifecycle.phase == .converting)
        }
    }
    @Test func convertingCaptureCancellationNeverConverts() {
        var lifecycle = convertingCapture()
        _ = lifecycle.handle(.cancelRequested)
        #expect(lifecycle.handle(.outputFinished) == [.deleteFile(url), .ended])
        #expect(lifecycle.phase == .cancelled)
    }
    @Test(arguments: [true, false]) func convertingCaptureFailureSavesOnlyNonemptyOriginal(content: Bool) {
        var lifecycle = convertingCapture()
        #expect(lifecycle.handle(.outputFailed(failure, fileHasContent: content)) == [
            .stopCapture, content ? .saveOriginal(url) : .deleteFile(url), .showError(failure), .ended])
        var stopping = convertingCapture()
        _ = stopping.handle(.stopRequested)
        #expect(stopping.handle(.outputFailed(failure, fileHasContent: content)) == [
            content ? .saveOriginal(url) : .deleteFile(url), .showError(failure), .ended])
    }
    @Test func convertingCaptureWindowFailureSavesOriginalWithoutConverting() {
        var lifecycle = convertingCapture()
        _ = lifecycle.handle(.windowStreamFailed(failure))
        #expect(lifecycle.handle(.outputFinished) == [.saveOriginal(url), .showError(failure), .ended])
    }
    @Test func failureTitles() {
        #expect(failure.title == "Couldn't record the screen")
        #expect(RecordingFailure.compression("x").title == "Couldn't compress the recording")
    }

}
