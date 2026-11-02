import Foundation
import KeyboardShortcuts

enum SystemScreenshotShortcuts {
    static func current() -> [SystemShortcut] {
        let domain = "com.apple.symbolichotkeys" as CFString
        CFPreferencesAppSynchronize(domain)
        let root = CFPreferencesCopyAppValue("AppleSymbolicHotKeys" as CFString, domain)
        return SymbolicHotKeysParser.parse(root as? [String: Any])
    }

    @MainActor
    static func currentSnapperBindings() -> [CapturePurpose: KeyChord] {
        var bindings: [CapturePurpose: KeyChord] = [:]
        for purpose in CapturePurpose.allCases {
            if let shortcut = KeyboardShortcuts.getShortcut(for: purpose.shortcutName) {
                bindings[purpose] = KeyChord(
                    keyCode: shortcut.carbonKeyCode, rawModifiers: UInt(shortcut.modifiers.rawValue)
                )
            }
        }
        return bindings
    }
}
