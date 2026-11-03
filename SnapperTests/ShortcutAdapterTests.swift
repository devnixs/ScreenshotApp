import KeyboardShortcuts
import Testing
@testable import Snapper

struct ShortcutAdapterTests {
    @MainActor @Test func carbonCodesUseCocoaModifierBitsForSymbolicHotkeys() {
        let shortcut = KeyboardShortcuts.Shortcut(.four, modifiers: [.command, .shift])
        #expect(shortcut.keyChord == KeyChord(keyCode: 21, rawModifiers: 0x120000))
        let control = KeyboardShortcuts.Shortcut(.four, modifiers: [.command, .shift, .control])
        #expect(control.keyChord == KeyChord(keyCode: 21, rawModifiers: 0x160000))
        #expect(KeyboardShortcuts.Shortcut(.nine, modifiers: [.option, .command]).keyChord
                == KeyChord(keyCode: 25, rawModifiers: 0x180000))
    }
}
