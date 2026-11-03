import Testing
@testable import Snapper

struct ShortcutConflictDetectorTests {
    private let defaults: [CapturePurpose: KeyChord] = [
        .screenshot: KeyChord(keyCode: 21, rawModifiers: 0x120000),
        .record: KeyChord(keyCode: 23, rawModifiers: 0x120000),
        .annotate: KeyChord(keyCode: 22, rawModifiers: 0x120000)
    ]
    private let system = SymbolicHotKeysParser.parse(nil)

    @Test func defaultsAndWarnings() {
        let result = ShortcutConflictDetector.conflicts(bindings: defaults, enabledSystemShortcuts: system)
        #expect(result == [
            .init(purpose: .screenshot, systemShortcutTitle: "Save picture of selected area as a file"),
            .init(purpose: .record, systemShortcutTitle: "Screenshot and recording options")
        ])
        #expect(ShortcutConflictDetector.warningText(for: result[0])
                == "Conflicts with the macOS shortcut “Save picture of selected area as a file”.")
    }

    @Test func disabledAndAbsentSystemShortcuts() {
        #expect(ShortcutConflictDetector.conflicts(bindings: defaults, enabledSystemShortcuts: system.filter { $0.id < 30 }).isEmpty)
        #expect(ShortcutConflictDetector.conflicts(bindings: defaults, enabledSystemShortcuts: []).isEmpty)
        let disabled = system.map { SystemShortcut(id: $0.id, chord: $0.chord, isEnabled: false) }
        #expect(ShortcutConflictDetector.conflicts(bindings: defaults, enabledSystemShortcuts: disabled).isEmpty)
    }

    @Test func rebindingsClearedBindingsAndExactModifiers() {
        var bindings = defaults
        bindings[.screenshot] = KeyChord(keyCode: 21, rawModifiers: 0x180000)
        bindings[.annotate] = nil
        #expect(ShortcutConflictDetector.conflicts(bindings: bindings, enabledSystemShortcuts: system).map(\.purpose) == [.record])
        bindings[.screenshot] = KeyChord(keyCode: 21, rawModifiers: 0x160000)
        #expect(ShortcutConflictDetector.conflicts(bindings: bindings, enabledSystemShortcuts: system.filter { $0.id == 30 }).isEmpty)
        #expect(ShortcutConflictDetector.conflicts(bindings: bindings, enabledSystemShortcuts: system.filter { $0.id == 31 })
                == [.init(purpose: .screenshot, systemShortcutTitle: "Copy picture of selected area to the clipboard")])
    }

    @Test func duplicatesIgnoreSelfAndClearedBindings() {
        #expect(ShortcutConflictDetector.duplicateOwner(of: defaults[.record]!, recordingFor: .screenshot, bindings: defaults) == .record)
        #expect(ShortcutConflictDetector.duplicateOwner(of: defaults[.screenshot]!, recordingFor: .screenshot, bindings: defaults) == nil)
        #expect(ShortcutConflictDetector.duplicateOwner(of: KeyChord(keyCode: 25, rawModifiers: 0x180000), recordingFor: .annotate, bindings: defaults) == nil)
        var bindings = defaults
        bindings[.record] = nil
        #expect(ShortcutConflictDetector.duplicateOwner(of: defaults[.record]!, recordingFor: .screenshot, bindings: bindings) == nil)
        #expect(ShortcutConflictDetector.duplicateReason(owner: .record) == "Already used by Snapper's “Record” shortcut.")
    }

    @Test func settingsAndOnboardingAlwaysAgree() {
        let bindingSets = [defaults, [:], [.screenshot: KeyChord(keyCode: 21, rawModifiers: 0x180000)]]
        for bindings in bindingSets {
            for shortcuts in [system, [], system.filter { $0.id == 30 }] {
                let conflicts = ShortcutConflictDetector.conflicts(bindings: bindings, enabledSystemShortcuts: shortcuts)
                let status = OnboardingStatus(permissionGranted: true, collisions: ShortcutCollision.collisions(
                    snapperBindings: bindings, systemShortcuts: shortcuts))
                #expect(conflicts.isEmpty == status.isShortcutStepDone)
            }
        }
    }

    @Test func deterministicOrderingIncludesAllPairsAndTouchBarTitles() {
        let chord = KeyChord(keyCode: 21, rawModifiers: 0x120000)
        let shortcuts = [184, 181, 30].map { SystemShortcut(id: $0, chord: chord, isEnabled: true) }
        let result = ShortcutConflictDetector.conflicts(bindings: [.screenshot: chord, .record: chord], enabledSystemShortcuts: shortcuts)
        #expect(result.map(\.purpose) == [.screenshot, .screenshot, .screenshot, .record, .record, .record])
        #expect(result.prefix(3).map(\.systemShortcutTitle) == ["Save picture of selected area as a file", "Save picture of the Touch Bar as a file", "Screenshot and recording options"])
    }
}
