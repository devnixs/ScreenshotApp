struct OnboardingStatus: Equatable, Sendable {
    let permissionGranted: Bool
    let collisions: [ShortcutCollision]

    var isPermissionStepDone: Bool { permissionGranted }
    var isShortcutStepDone: Bool { collisions.isEmpty }
    var allDone: Bool { isPermissionStepDone && isShortcutStepDone }
}

enum OnboardingDecision {
    static func shouldShowAtLaunch(status: OnboardingStatus, dismissed: Bool) -> Bool {
        !dismissed && !status.allDone
    }

    static func shouldGateCapture(permissionGranted: Bool) -> Bool { !permissionGranted }
}
