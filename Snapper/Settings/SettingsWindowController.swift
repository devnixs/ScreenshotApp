import AppKit
import SwiftUI

@MainActor
final class SettingsWindowController {
    private let model: SettingsModel
    private lazy var window: NSWindow = {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 360),
                              styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Snapper Settings"
        let host = NSHostingController(rootView: SettingsView(model: model))
        host.sizingOptions = [.preferredContentSize]
        window.contentViewController = host
        window.isReleasedWhenClosed = false
        window.collectionBehavior.insert(.moveToActiveSpace)
        model.window = window
        window.center()
        return window
    }()

    init(model: SettingsModel) { self.model = model }

    func show() {
        model.refreshOnAppear()
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }
}
