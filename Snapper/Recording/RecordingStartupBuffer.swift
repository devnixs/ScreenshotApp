/// Preserves delegate ordering when framework callbacks race the async startup return.
/// The caller feeds captureStarted before replaying a successful startup's callbacks.
struct RecordingStartupBuffer<Callback: Sendable>: Sendable {
    private var ready = false
    private var pending: [Callback] = []

    /// Returns true when the caller should deliver this callback immediately.
    mutating func shouldDeliver(_ callback: Callback) -> Bool {
        guard ready else { pending.append(callback); return false }
        return true
    }

    mutating func complete(succeeded: Bool) -> [Callback] {
        ready = true
        let callbacks = succeeded ? pending : []
        pending.removeAll()
        return callbacks
    }
}
