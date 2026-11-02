struct ShortcutCollision: Equatable, Sendable {
    let purpose: CapturePurpose
    let system: SystemShortcut

    static func collisions(
        snapperBindings: [CapturePurpose: KeyChord], systemShortcuts: [SystemShortcut]
    ) -> [ShortcutCollision] {
        CapturePurpose.allCases.flatMap { purpose in
            systemShortcuts.filter {
                $0.isEnabled && $0.chord == snapperBindings[purpose]
            }.sorted { $0.id < $1.id }.map { ShortcutCollision(purpose: purpose, system: $0) }
        }
    }
}
