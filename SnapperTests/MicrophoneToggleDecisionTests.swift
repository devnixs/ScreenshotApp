import Testing
@testable import Snapper

struct MicrophoneToggleDecisionTests {
    @Test(arguments: MicrophoneAuthorization.allCases) func toggleDecisionTable(status: MicrophoneAuthorization) {
        #expect(MicrophoneToggleDecision.userToggled(to: false, status: status)
                == .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: false))
        let expected: MicrophoneToggleOutcome
        switch status {
        case .authorized: expected = .init(storedValue: true, showsDeniedNote: false, shouldRequestAccess: false)
        case .notDetermined: expected = .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: true)
        case .denied, .restricted: expected = .init(storedValue: false, showsDeniedNote: true, shouldRequestAccess: false)
        }
        #expect(MicrophoneToggleDecision.userToggled(to: true, status: status) == expected)
    }

    @Test func requestCompletion() {
        #expect(MicrophoneToggleDecision.accessRequestFinished(granted: true)
                == .init(storedValue: true, showsDeniedNote: false, shouldRequestAccess: false))
        #expect(MicrophoneToggleDecision.accessRequestFinished(granted: false)
                == .init(storedValue: false, showsDeniedNote: true, shouldRequestAccess: false))
    }

    @Test(arguments: MicrophoneAuthorization.allCases) func reconcileNeverPrompts(status: MicrophoneAuthorization) {
        #expect(MicrophoneToggleDecision.reconcileOnAppear(stored: false, status: status)
                == .init(storedValue: false, showsDeniedNote: false, shouldRequestAccess: false))
        let expected = MicrophoneToggleOutcome(storedValue: status == .authorized,
                                              showsDeniedNote: status == .denied || status == .restricted,
                                              shouldRequestAccess: false)
        #expect(MicrophoneToggleDecision.reconcileOnAppear(stored: true, status: status) == expected)
    }

    @Test func pendingPromptHonorsLatestIntentAndDoesNotDuplicateRequests() {
        var request = MicrophoneRequestState()
        request.userToggled(to: true)
        let result1 = request.beginRequest()
        #expect(result1)
        let result2 = !request.beginRequest()
        #expect(result2)
        request.userToggled(to: false)
        let result3 = !request.finishRequest()
        #expect(result3)
        #expect(!request.isPending)
        request.userToggled(to: true)
        let result4 = request.beginRequest()
        #expect(result4)
        let result5 = request.finishRequest()
        #expect(result5)
        #expect(!request.isPending)
    }

    @Test func onOffOnWhilePendingAppliesToLatestOn() {
        var request = MicrophoneRequestState()
        request.userToggled(to: true)
        let result6 = request.beginRequest()
        #expect(result6)
        request.userToggled(to: false)
        request.userToggled(to: true)
        let result7 = !request.beginRequest()
        #expect(result7)
        let result8 = request.finishRequest()
        #expect(result8)
    }
}
