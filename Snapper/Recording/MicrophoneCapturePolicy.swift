/// Uses the same authorization model as the Settings toggle, without requesting access.
struct MicrophoneCaptureInputs: Sendable, Equatable {
    var recordMicrophoneSetting: Bool
    var authorization: MicrophoneAuthorization
    var hasAudioInputDevice: Bool
}

enum MicrophoneCapturePolicy {
    static func shouldCaptureMicrophone(_ inputs: MicrophoneCaptureInputs) -> Bool {
        inputs.recordMicrophoneSetting && inputs.authorization == .authorized && inputs.hasAudioInputDevice
    }
}
