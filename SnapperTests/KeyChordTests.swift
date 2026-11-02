import Testing
@testable import Snapper

struct KeyChordTests {
    @Test func masksModifierNoise() {
        #expect(KeyChord(keyCode: 21, rawModifiers: 0x930000) == KeyChord(keyCode: 21, rawModifiers: 0x120000))
        #expect(KeyChord(keyCode: 21, rawModifiers: UInt.max).modifiers == 0x1E0000)
    }

    @Test func displaysDigitsAndModifierOrder() {
        #expect(KeyChord(keyCode: 21, rawModifiers: 0x120000).displayString == "⇧⌘4")
        #expect(KeyChord(keyCode: 20, rawModifiers: 0x160000).displayString == "⌃⇧⌘3")
        #expect(KeyChord(keyCode: 99, rawModifiers: 0x1E0000).displayString == "⌃⌥⇧⌘Key 99")
        let digitCodes = [29, 18, 19, 20, 21, 23, 22, 26, 28, 25]
        for (digit, code) in digitCodes.enumerated() {
            #expect(KeyChord(keyCode: code, rawModifiers: 0).displayString == String(digit))
        }
    }
}
