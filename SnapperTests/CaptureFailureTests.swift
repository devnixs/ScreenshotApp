import Foundation
import ScreenCaptureKit
import Testing
@testable import Snapper

struct CaptureFailureTests {
    @Test func unavailableWindowUsesGenericCaptureFailure() {
        let error = ScreenshotCaptureError.windowUnavailable
        #expect(error.localizedDescription == "The window is no longer available.")
        #expect(CaptureFailure(error) == .other)
    }

    @Test func permissionDomainAndCode() {
        #expect(CaptureFailure(NSError(domain: SCStreamErrorDomain, code: -3801)) == .permissionDenied)
        #expect(CaptureFailure(NSError(domain: SCStreamErrorDomain, code: -3802)) == .other)
        #expect(CaptureFailure(CocoaError(.fileReadUnknown)) == .other)
        #expect(CaptureFailure(NSError(domain: "unrelated", code: -3801)) == .other)
    }
}
