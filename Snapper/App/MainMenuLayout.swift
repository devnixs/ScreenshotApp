import Foundation

struct MenuModifiers: OptionSet, Sendable {
    let rawValue: Int

    static let command = Self(rawValue: 1 << 0)
    static let shift = Self(rawValue: 1 << 1)
    static let option = Self(rawValue: 1 << 2)
    static let control = Self(rawValue: 1 << 3)
}

struct MenuCommandSpec: Equatable, Sendable {
    let title: String
    let keyEquivalent: String
    let modifiers: MenuModifiers
    let action: String
}

enum MainMenuLayout {
    static let app: [MenuCommandSpec?] = [
        .init(title: "About Snapper", keyEquivalent: "", modifiers: [], action: "orderFrontStandardAboutPanel:"),
        nil,
        .init(title: "Settings…", keyEquivalent: ",", modifiers: .command, action: "openSettings:"),
        nil,
        .init(title: "Hide Snapper", keyEquivalent: "h", modifiers: .command, action: "hide:"),
        .init(title: "Quit Snapper", keyEquivalent: "q", modifiers: .command, action: "terminate:")
    ]
    static let file: [MenuCommandSpec?] = [
        .init(title: "Close", keyEquivalent: "w", modifiers: .command, action: "performClose:"),
        .init(title: "Save…", keyEquivalent: "s", modifiers: .command, action: "saveDocument:")
    ]
    static let edit: [MenuCommandSpec?] = [
        .init(title: "Undo", keyEquivalent: "z", modifiers: .command, action: "undo:"),
        .init(title: "Redo", keyEquivalent: "z", modifiers: [.command, .shift], action: "redo:"),
        nil,
        .init(title: "Cut", keyEquivalent: "x", modifiers: .command, action: "cut:"),
        .init(title: "Copy", keyEquivalent: "c", modifiers: .command, action: "copy:"),
        .init(title: "Paste", keyEquivalent: "v", modifiers: .command, action: "paste:"),
        .init(title: "Delete", keyEquivalent: "", modifiers: [], action: "delete:"),
        .init(title: "Select All", keyEquivalent: "a", modifiers: .command, action: "selectAll:")
    ]
    static let window: [MenuCommandSpec?] = [
        .init(title: "Minimize", keyEquivalent: "m", modifiers: .command, action: "performMiniaturize:"),
        .init(title: "Zoom", keyEquivalent: "", modifiers: [], action: "performZoom:")
    ]
}
