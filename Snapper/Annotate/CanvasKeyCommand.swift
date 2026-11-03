import AppKit

enum CanvasKeyCommand: Equatable {
    case deleteSelection, deselect
    init?(keyCode: UInt16, modifiers: NSEvent.ModifierFlags) {
        guard modifiers.intersection([.command, .option, .control]).isEmpty else { return nil }
        switch keyCode {
        case 51, 117: self = .deleteSelection
        case 53: self = .deselect
        default: return nil
        }
    }
}
