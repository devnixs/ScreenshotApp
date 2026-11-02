import Testing
@testable import Snapper

struct AppEnvironmentTests {
    @Test func emptyEnvironmentIsNotTesting() {
        #expect(!AppEnvironment.isRunningUnitTests([:]))
    }

    @Test func configurationPathDetectsTests() {
        #expect(AppEnvironment.isRunningUnitTests(["XCTestConfigurationFilePath": "/x"]))
    }

    @Test func bundlePathDetectsTests() {
        #expect(AppEnvironment.isRunningUnitTests(["XCTestBundlePath": "/x"]))
    }

    @Test func unrelatedEnvironmentIsNotTesting() {
        #expect(!AppEnvironment.isRunningUnitTests(["PATH": "/usr/bin", "SNAPPER_TEST": "1"]))
    }
}
