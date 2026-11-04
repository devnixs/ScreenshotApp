/// The kinds of Snapper windows that make it a regular app (Dock icon, ⌘Tab entry). PRD §3.
/// All kinds count equally; the kind is only used for logging.
enum DockPresenceWindowKind: String, Hashable, Sendable {
    case onboarding, settings, annotation
}

enum DockPresenceChange: Equatable, Sendable {
    case becomeRegular, becomeAccessory
}

/// Tracks which onboarding, Settings, and annotation windows are open (minimized ones included)
/// and reports when Snapper should switch between `.accessory` and `.regular`.
struct DockPresence: Equatable, Sendable {
    private(set) var openWindows: Set<ObjectIdentifier> = []

    var wantsRegularApp: Bool { !openWindows.isEmpty }

    mutating func windowOpened(_ id: ObjectIdentifier) -> DockPresenceChange? {
        let wasEmpty = openWindows.isEmpty
        guard openWindows.insert(id).inserted else { return nil }
        return wasEmpty ? .becomeRegular : nil
    }

    mutating func windowClosed(_ id: ObjectIdentifier) -> DockPresenceChange? {
        guard openWindows.remove(id) != nil else { return nil }
        return openWindows.isEmpty ? .becomeAccessory : nil
    }
}

enum DockReopenAction: Equatable, Sendable {
    case bringToFront, deminiaturizeOne, nothing
}

/// What a click on the Dock icon does (PRD §3).
enum DockReopenDecision {
    static func action(hasVisibleWindows: Bool, hasMinimizedTrackedWindow: Bool) -> DockReopenAction {
        if hasVisibleWindows { return .bringToFront }
        if hasMinimizedTrackedWindow { return .deminiaturizeOne }
        return .nothing
    }
}
