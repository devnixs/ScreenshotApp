import Testing
@testable import Snapper

struct LaunchAtLoginStateTests {
    @Test func statusesAndErrors() {
        let cases: [(LaunchAtLoginStatus, LaunchAtLoginState)] = [
            (.enabled, .init(isOn: true, note: nil)),
            (.requiresApproval, .init(isOn: true, note: .requiresApproval)),
            (.notRegistered, .init(isOn: false, note: nil)),
            (.notFound, .init(isOn: false, note: .unavailable))
        ]
        for (status, expected) in cases {
            #expect(LaunchAtLoginState.state(for: status) == expected)
            #expect(LaunchAtLoginState.state(for: status, error: "x") == .init(isOn: expected.isOn, note: .failed("x")))
        }
    }
}
