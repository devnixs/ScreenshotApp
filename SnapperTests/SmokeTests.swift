import Foundation
import Testing
@testable import Snapper

struct SmokeTests {
    @Test func hostEnvironmentSuppressesAppUI() {
        #expect(AppEnvironment.isRunningUnitTests(ProcessInfo.processInfo.environment))
    }

    @Test func testBundleIsHostedBySnapper() {
        #expect(Bundle.main.bundleIdentifier == "com.thera-engineering.snapper")
    }
}
