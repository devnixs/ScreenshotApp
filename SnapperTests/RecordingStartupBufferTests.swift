import Foundation
import Testing
@testable import Snapper

struct RecordingStartupBufferTests {
    @Test func earlyFailureReplaysAfterStartedAndFinishesLifecycle() {
        let url = URL(fileURLWithPath: "/recording.mp4")
        let failure = RecordingFailure(message: "Writer stopped", kind: .other)
        var lifecycle = RecordingLifecycle()
        var buffer = RecordingStartupBuffer<RecordingEvent>()
        _ = lifecycle.handle(.start(url: url, converts: false))
        #expect(buffer.shouldDeliver(.streamStopped(failure)) == false)
        #expect(buffer.shouldDeliver(.outputFailed(failure, fileHasContent: true)) == false)
        #expect(lifecycle.phase == .starting)
        let replay = buffer.complete(succeeded: true)
        #expect(replay == [.streamStopped(failure), .outputFailed(failure, fileHasContent: true)])
        _ = lifecycle.handle(.captureStarted)
        #expect(lifecycle.handle(replay[0]) == [.scheduleStopTimeout])
        #expect(lifecycle.handle(replay[1]) == [.reveal(url), .showError(failure), .ended])
        #expect(lifecycle.phase == .finished(url))
        #expect(buffer.shouldDeliver(.outputFinished) == true)
        #expect(lifecycle.handle(.outputFinished) == [])
    }

    @Test func failedStartDiscardsEarlyCallbacks() {
        var buffer = RecordingStartupBuffer<Int>()
        #expect(buffer.shouldDeliver(1) == false)
        #expect(buffer.shouldDeliver(2) == false)
        #expect(buffer.complete(succeeded: false) == [])
        #expect(buffer.shouldDeliver(3) == true)
        #expect(buffer.complete(succeeded: true) == [])
    }

    @Test func callbacksAfterStartAreDeliveredImmediately() {
        var buffer = RecordingStartupBuffer<Int>()
        #expect(buffer.complete(succeeded: true) == [])
        #expect(buffer.shouldDeliver(1) == true)
        #expect(buffer.shouldDeliver(2) == true)
    }
}
