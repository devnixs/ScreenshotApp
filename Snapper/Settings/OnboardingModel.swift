import Observation

@MainActor @Observable
final class OnboardingModel {
    private(set) var status = OnboardingStatus(permissionGranted: false, collisions: [])

    func refresh() {
        status = OnboardingStatus(
            permissionGranted: ScreenRecordingPermission.isGranted,
            collisions: ShortcutCollision.collisions(
                snapperBindings: SystemScreenshotShortcuts.currentSnapperBindings(),
                systemShortcuts: SystemScreenshotShortcuts.current()
            )
        )
    }
}
