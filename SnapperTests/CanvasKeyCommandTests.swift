import AppKit
import Testing
@testable import Snapper

struct CanvasKeyCommandTests {
    @Test func keyMappingAndModifierPolicy() {
        #expect(CanvasKeyCommand(keyCode: 51, modifiers: []) == .deleteSelection)
        #expect(CanvasKeyCommand(keyCode: 117, modifiers: [.function]) == .deleteSelection)
        #expect(CanvasKeyCommand(keyCode: 53, modifiers: []) == .deselect)
        #expect(CanvasKeyCommand(keyCode: 51, modifiers: [.shift, .numericPad]) == .deleteSelection)
        for modifier: NSEvent.ModifierFlags in [.command, .option, .control] {
            #expect(CanvasKeyCommand(keyCode: 51, modifiers: modifier) == nil)
            #expect(CanvasKeyCommand(keyCode: 53, modifiers: modifier) == nil)
        }
        #expect(CanvasKeyCommand(keyCode: 9, modifiers: []) == nil)
    }
}
