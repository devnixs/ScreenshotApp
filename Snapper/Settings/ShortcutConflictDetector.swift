struct ShortcutConflict: Equatable, Sendable {
    let purpose: CapturePurpose
    let systemShortcutTitle: String
}

enum ShortcutConflictDetector {
    static func conflicts(bindings: [CapturePurpose: KeyChord], enabledSystemShortcuts: [SystemShortcut]) -> [ShortcutConflict] {
        ShortcutCollision.collisions(snapperBindings: bindings, systemShortcuts: enabledSystemShortcuts).compactMap {
            guard let id = SystemScreenshotHotKeyID(rawValue: $0.system.id) else { return nil }
            return ShortcutConflict(purpose: $0.purpose, systemShortcutTitle: id.actionName)
        }
    }

    static func warningText(for conflict: ShortcutConflict) -> String {
        "Conflicts with the macOS shortcut “\(conflict.systemShortcutTitle)”."
    }

    static func duplicateOwner(of candidate: KeyChord, recordingFor purpose: CapturePurpose,
                               bindings: [CapturePurpose: KeyChord]) -> CapturePurpose? {
        CapturePurpose.allCases.first { $0 != purpose && bindings[$0] == candidate }
    }

    static func duplicateReason(owner: CapturePurpose) -> String {
        "Already used by Snapper's “\(owner.rawValue.capitalized)” shortcut."
    }
}
