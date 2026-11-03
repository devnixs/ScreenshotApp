import Foundation

enum CaptureFailure: Equatable {
    case permissionDenied, other

    init(_ error: Error) {
        let error = error as NSError
        self = error.domain == "com.apple.ScreenCaptureKit.SCStreamErrorDomain" && error.code == -3801
            ? .permissionDenied : .other
    }
}
