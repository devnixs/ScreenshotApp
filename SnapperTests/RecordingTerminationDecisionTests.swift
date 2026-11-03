import Testing
@testable import Snapper

struct RecordingTerminationDecisionTests {
    @Test func idleQuitsImmediately() {
        var decision = RecordingTerminationDecision()
        #expect(decision.request(recordingActive: false) == false)
        #expect(decision.recordingEnded() == false)
    }
    @Test func activeRecordingDefersUntilExactlyOneReply() {
        var decision = RecordingTerminationDecision()
        #expect(decision.request(recordingActive: true) == true)
        #expect(decision.request(recordingActive: true) == true)
        #expect(decision.recordingEnded() == true)
        #expect(decision.recordingEnded() == false)
        #expect(decision.request(recordingActive: false) == false)
    }
    @Test func ordinaryStopNeverRepliesToTermination() {
        var decision = RecordingTerminationDecision()
        #expect(decision.recordingEnded() == false)
    }
}
