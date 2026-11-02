import CoreGraphics

@MainActor
enum ScreenRecordingPermission {
    static var isGranted: Bool { CGPreflightScreenCaptureAccess() }

    static func request() {
        _ = CGRequestScreenCaptureAccess()
    }
}
