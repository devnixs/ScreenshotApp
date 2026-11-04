import AppKit
import SwiftUI

@MainActor
final class OnboardingWindowController: NSObject, NSWindowDelegate {
    private let settings: AppSettings
    private let dockPresence: DockPresenceController
    private let model = OnboardingModel()
    private var window: NSWindow?

    init(settings: AppSettings, dockPresence: DockPresenceController) {
        self.settings = settings
        self.dockPresence = dockPresence
        super.init()
    }

    func show() {
        model.refresh()
        if window == nil {
            let window = NSWindow(contentRect: .zero, styleMask: [.titled, .closable],
                                  backing: .buffered, defer: false)
            window.title = "Set up Snapper"
            window.isReleasedWhenClosed = false
            window.delegate = self
            window.collectionBehavior.insert(.moveToActiveSpace)
            let hostingController = NSHostingController(rootView: OnboardingView(
                model: model,
                dismissPermanently: { [weak self] in
                    self?.settings.onboardingDismissed = true
                    self?.window?.close()
                },
                close: { [weak self] in self?.window?.close() }
            ))
            window.contentViewController = hostingController
            window.setContentSize(hostingController.view.fittingSize)
            window.center()
            self.window = window
        }
        NotificationCenter.default.removeObserver(self, name: NSApplication.didBecomeActiveNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(refreshOnActivation),
                                               name: NSApplication.didBecomeActiveNotification, object: nil)
        if let window { dockPresence.track(window, kind: .onboarding) }
        NSApp.activate()
        window?.makeKeyAndOrderFront(nil)
    }

    @objc private func refreshOnActivation(_ notification: Notification) {
        model.refresh()
    }

    func windowWillClose(_ notification: Notification) {
        NotificationCenter.default.removeObserver(self, name: NSApplication.didBecomeActiveNotification, object: nil)
    }
}
