import Foundation
import ScreenCaptureKit
import Testing
@testable import Snapper

struct CaptureFailureTests {
    @Test func permissionDomainAndCode() {
        #expect(CaptureFailure(NSError(domain: SCStreamErrorDomain, code: -3801)) == .permissionDenied)
        #expect(CaptureFailure(NSError(domain: SCStreamErrorDomain, code: -3802)) == .other)
        #expect(CaptureFailure(CocoaError(.fileReadUnknown)) == .other)
        #expect(CaptureFailure(NSError(domain: "unrelated", code: -3801)) == .other)
    }
}
