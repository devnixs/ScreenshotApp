import AVFoundation

/// Read-only checks. Only the Settings toggle may request microphone access.
@MainActor
enum MicrophonePermission {
    static var authorization: MicrophoneAuthorization {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .notDetermined: .notDetermined
        case .authorized: .authorized
        case .denied: .denied
        case .restricted: .restricted
        @unknown default: .denied
        }
    }

    static var hasAudioInputDevice: Bool { AVCaptureDevice.default(for: .audio) != nil }
}
