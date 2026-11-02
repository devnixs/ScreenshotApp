import Testing
@testable import Snapper

struct ShortcutCollisionTests {
    private let defaults: [CapturePurpose: KeyChord] = [
        .screenshot: KeyChord(keyCode: 21, rawModifiers: 0x120000),
        .record: KeyChord(keyCode: 23, rawModifiers: 0x120000),
        .annotate: KeyChord(keyCode: 22, rawModifiers: 0x120000)
    ]

    @Test func defaultsAndDisabledShortcuts() {
        let system = SymbolicHotKeysParser.parse(nil)
        let collisions = ShortcutCollision.collisions(snapperBindings: defaults, systemShortcuts: system.reversed())
        #expect(collisions.map(\.purpose) == [.screenshot, .record])
        #expect(collisions.map(\.system.id) == [30, 184])
        let disabled = system.map { SystemShortcut(id: $0.id, chord: $0.chord, isEnabled: $0.id != 30 && $0.id != 184) }
        #expect(ShortcutCollision.collisions(snapperBindings: defaults, systemShortcuts: disabled).isEmpty)
    }

    @Test func rebindingsRequireExactModifiersAndEnabledSystem() {
        let system = SymbolicHotKeysParser.parse(nil)
        let control = [CapturePurpose.screenshot: KeyChord(keyCode: 21, rawModifiers: 0x160000)]
        #expect(ShortcutCollision.collisions(snapperBindings: control, systemShortcuts: system).map(\.system.id) == [31])
        let disabled = system.map { SystemShortcut(id: $0.id, chord: $0.chord, isEnabled: false) }
        #expect(ShortcutCollision.collisions(snapperBindings: control, systemShortcuts: disabled).isEmpty)
        let option = [CapturePurpose.screenshot: KeyChord(keyCode: 21, rawModifiers: 0x1A0000)]
        #expect(ShortcutCollision.collisions(snapperBindings: option, systemShortcuts: system).isEmpty)
        let noise = [CapturePurpose.screenshot: KeyChord(keyCode: 21, rawModifiers: 0x130000)]
        #expect(ShortcutCollision.collisions(snapperBindings: noise, systemShortcuts: system).map(\.system.id) == [30])
        #expect(ShortcutCollision.collisions(snapperBindings: [:], systemShortcuts: system).isEmpty)
    }

    @Test func touchBarOnlyCollidesWhenPresentAndEnabled() {
        for enabled in [false, true] {
            let system = SystemShortcut(id: 181, chord: KeyChord(keyCode: 22, rawModifiers: 0x120000), isEnabled: enabled)
            #expect(ShortcutCollision.collisions(snapperBindings: defaults, systemShortcuts: [system]).count == (enabled ? 1 : 0))
        }
        #expect(ShortcutCollision.collisions(snapperBindings: defaults, systemShortcuts: []).isEmpty)
    }

    @Test func ordersPurposesThenSystemIDs() {
        let chord = KeyChord(keyCode: 22, rawModifiers: 0x120000)
        let system = [184, 28].map { SystemShortcut(id: $0, chord: chord, isEnabled: true) }
        let collisions = ShortcutCollision.collisions(
            snapperBindings: [.screenshot: chord, .record: chord, .annotate: chord],
            systemShortcuts: system
        )
        #expect(collisions.map(\.purpose) == [.screenshot, .screenshot, .record, .record, .annotate, .annotate])
        #expect(collisions.map(\.system.id) == [28, 184, 28, 184, 28, 184])
    }
}
