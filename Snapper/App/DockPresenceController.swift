import AppKit
import os

/// Switches Snapper between a menu-bar-only (`.accessory`) and a regular app while onboarding,
/// Settings, or annotation windows are open, and handles Dock icon clicks. PRD §3.
@MainActor
final class DockPresenceController {
    private struct Tracked {
        weak var window: NSWindow?
        let kind: DockPresenceWindowKind
        let observer: NSObjectProtocol
    }

    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "DockPresence")
    private var presence = DockPresence()
    /// In tracking order, so reopen can prefer the most recently tracked window.
    private var tracked: [ObjectIdentifier: Tracked] = [:]
    private var order: [ObjectIdentifier] = []

    /// Call right before activating the app and showing `window`. Tracking an already-tracked
    /// window is a no-op.
    func track(_ window: NSWindow, kind: DockPresenceWindowKind) {
        let id = ObjectIdentifier(window)
        guard tracked[id] == nil else { return }
        let change = presence.windowOpened(id)
        // willClose is posted only for real closes; a vetoed "Discard changes?" close never posts it.
        let observer = NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification, object: window, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.untrack(id) }
        }
        tracked[id] = Tracked(window: window, kind: kind, observer: observer)
        order.append(id)
        logger.debug("Tracking \(kind.rawValue, privacy: .public) window")
        if let change { apply(change) }
    }

    /// Handles a Dock icon click. Returns `false` because Snapper handles the reopen itself.
    func reopen() -> Bool {
        let windows = order.reversed().compactMap { tracked[$0]?.window }
        let visible = windows.first { $0.isVisible && !$0.isMiniaturized }
        let minimized = windows.first { $0.isMiniaturized }
        switch DockReopenDecision.action(hasVisibleWindows: visible != nil,
                                         hasMinimizedTrackedWindow: minimized != nil) {
        case .bringToFront:
            NSApp.activate()
            visible?.makeKeyAndOrderFront(nil)
        case .deminiaturizeOne:
            NSApp.activate()
            minimized?.deminiaturize(nil)
        case .nothing:
            break
        }
        return false
    }

    private func untrack(_ id: ObjectIdentifier) {
        if let entry = tracked.removeValue(forKey: id) {
            NotificationCenter.default.removeObserver(entry.observer)
            logger.debug("Untracking \(entry.kind.rawValue, privacy: .public) window")
        }
        order.removeAll { $0 == id }
        if let change = presence.windowClosed(id) { apply(change) }
    }

    private func apply(_ change: DockPresenceChange) {
        switch change {
        case .becomeRegular: NSApp.setActivationPolicy(.regular)
        case .becomeAccessory: NSApp.setActivationPolicy(.accessory)
        }
    }
}
