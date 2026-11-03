import AppKit
import AVFoundation
import KeyboardShortcuts
import Observation

@MainActor @Observable
final class SettingsModel {
    private let settings: AppSettings
    private(set) var launchAtLogin = LaunchAtLoginState(isOn: false, note: nil)
    private(set) var conflicts: [ShortcutConflict] = []
    private(set) var showsMicrophoneDeniedNote = false
    @ObservationIgnored private var microphoneRequest = MicrophoneRequestState()
    @ObservationIgnored private var activationTask: Task<Void, Never>?
    // Weak so the controller remains the sole owner of the Settings window.
    @ObservationIgnored weak var window: NSWindow?

    init(settings: AppSettings) {
        self.settings = settings
        activationTask = Task { @MainActor [weak self] in
            for await _ in NotificationCenter.default.notifications(named: NSApplication.didBecomeActiveNotification).map({ _ in () }) {
                guard !Task.isCancelled else { return }
                self?.refreshOnAppear()
            }
        }
    }

    deinit { activationTask?.cancel() }

    var saveFolderURL: URL {
        get { settings.saveFolderURL }
        set { settings.saveFolderURL = newValue }
    }
    var screenshotDestination: ScreenshotDestination {
        get { settings.screenshotDestination }
        set { settings.screenshotDestination = newValue }
    }
    var playShutterSound: Bool {
        get { settings.playShutterSound }
        set { settings.playShutterSound = newValue }
    }
    var recordMicrophone: Bool { settings.recordMicrophone }

    func setLaunchAtLogin(_ value: Bool) {
        let error = LaunchAtLogin.setEnabled(value)
        refreshLaunchAtLogin(error: error)
    }
    private func refreshLaunchAtLogin(error: String? = nil) {
        launchAtLogin = .state(for: LaunchAtLogin.status, error: error)
        settings.launchAtLogin = launchAtLogin.isOn
    }
    func openLoginItems() { LaunchAtLogin.openLoginItemsSettings() }
    func openKeyboardShortcuts() { SystemSettingsLinks.open(SystemSettingsLinks.keyboardShortcuts) }
    func openMicrophoneSettings() { SystemSettingsLinks.open(SystemSettingsLinks.microphone) }

    func refreshConflicts() {
        conflicts = ShortcutConflictDetector.conflicts(
            bindings: SystemScreenshotShortcuts.currentSnapperBindings(),
            enabledSystemShortcuts: SystemScreenshotShortcuts.current()
        )
    }

    func duplicateOwner(of candidate: KeyboardShortcuts.Shortcut, for purpose: CapturePurpose) -> CapturePurpose? {
        ShortcutConflictDetector.duplicateOwner(of: candidate.keyChord, recordingFor: purpose,
                                               bindings: SystemScreenshotShortcuts.currentSnapperBindings())
    }

    func setRecordMicrophone(_ value: Bool) {
        microphoneRequest.userToggled(to: value)
        let outcome = MicrophoneToggleDecision.userToggled(to: value, status: microphoneAuthorization)
        apply(outcome)
        guard outcome.shouldRequestAccess, microphoneRequest.beginRequest() else { return }
        AVCaptureDevice.requestAccess(for: .audio) { [weak self] granted in
            Task { @MainActor [weak self] in
                guard let self, self.microphoneRequest.finishRequest() else { return }
                self.apply(MicrophoneToggleDecision.accessRequestFinished(granted: granted))
            }
        }
    }

    private var microphoneAuthorization: MicrophoneAuthorization {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .notDetermined: .notDetermined
        case .authorized: .authorized
        case .denied: .denied
        case .restricted: .restricted
        @unknown default: .restricted
        }
    }
    private func apply(_ outcome: MicrophoneToggleOutcome) {
        settings.recordMicrophone = outcome.storedValue
        showsMicrophoneDeniedNote = outcome.showsDeniedNote
    }

    func refreshOnAppear() {
        refreshLaunchAtLogin()
        apply(MicrophoneToggleDecision.reconcileOnAppear(stored: recordMicrophone, status: microphoneAuthorization))
        refreshConflicts()
    }

    func chooseSaveFolder() {
        guard let window else { return }
        ErrorAlerts.chooseFolder(current: saveFolderURL, on: window) { [weak self] in self?.saveFolderURL = $0 }
    }
    func showSaveFolder() {
        do { NSWorkspace.shared.open(try SaveFolder(url: saveFolderURL).prepareForWriting()) }
        catch { _ = ErrorAlerts.showSaveFolderFailure(folder: saveFolderURL, error: error, settings: settings) }
    }
}
