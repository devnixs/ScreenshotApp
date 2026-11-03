/// Deferred termination waits for the same terminal recording outcome as Stop.
struct RecordingTerminationDecision: Sendable {
    private var pending = false
    mutating func request(recordingActive: Bool) -> Bool {
        if recordingActive { pending = true }
        return pending
    }
    mutating func recordingEnded() -> Bool {
        let shouldReply = pending
        pending = false
        return shouldReply
    }
}
