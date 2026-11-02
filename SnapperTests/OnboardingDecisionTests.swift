import Testing
@testable import Snapper

struct OnboardingDecisionTests {
    @Test func launchTruthTableAndStepState() {
        let collision = ShortcutCollision(purpose: .screenshot, system: SystemShortcut(
            id: 30, chord: KeyChord(keyCode: 21, rawModifiers: 0x120000), isEnabled: true
        ))
        for granted in [false, true] {
            for hasCollisions in [false, true] {
                let status = OnboardingStatus(permissionGranted: granted, collisions: hasCollisions ? [collision] : [])
                #expect(status.isPermissionStepDone == granted)
                #expect(status.isShortcutStepDone == !hasCollisions)
                #expect(status.allDone == (granted && !hasCollisions))
                for dismissed in [false, true] {
                    #expect(OnboardingDecision.shouldShowAtLaunch(status: status, dismissed: dismissed)
                            == (!dismissed && (!granted || hasCollisions)))
                }
            }
        }
    }

    @Test func captureAlwaysRequiresPermission() {
        #expect(!OnboardingDecision.shouldGateCapture(permissionGranted: true))
        #expect(OnboardingDecision.shouldGateCapture(permissionGranted: false))
    }
}
