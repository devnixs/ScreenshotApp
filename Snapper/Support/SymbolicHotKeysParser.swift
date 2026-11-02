import Foundation
import CoreFoundation

struct SystemShortcut: Equatable, Sendable {
    let id: Int
    let chord: KeyChord
    let isEnabled: Bool
}

enum SystemScreenshotHotKeyID: Int, CaseIterable, Sendable {
    case screenFile = 28, screenClipboard = 29, areaFile = 30, areaClipboard = 31
    case touchBarFile = 181, touchBarClipboard = 182, options = 184

    var assumedPresentWhenMissing: Bool {
        self != .touchBarFile && self != .touchBarClipboard
    }

    var defaultChord: KeyChord {
        let keyCode: Int
        switch self {
        case .screenFile, .screenClipboard: keyCode = 20
        case .areaFile, .areaClipboard: keyCode = 21
        case .touchBarFile, .touchBarClipboard: keyCode = 22
        case .options: keyCode = 23
        }
        let clipboard = self == .screenClipboard || self == .areaClipboard || self == .touchBarClipboard
        return KeyChord(keyCode: keyCode, rawModifiers: clipboard ? 0x160000 : 0x120000)
    }

    var actionName: String {
        switch self {
        case .screenFile: "Save picture of screen as a file"
        case .screenClipboard: "Copy picture of screen to the clipboard"
        case .areaFile: "Save picture of selected area as a file"
        case .areaClipboard: "Copy picture of selected area to the clipboard"
        case .touchBarFile: "Save picture of the Touch Bar as a file"
        case .touchBarClipboard: "Copy picture of the Touch Bar to the clipboard"
        case .options: "Screenshot and recording options"
        }
    }
}

enum SymbolicHotKeysParser {
    static func parse(_ root: [String: Any]?) -> [SystemShortcut] {
        SystemScreenshotHotKeyID.allCases.sorted { $0.rawValue < $1.rawValue }.compactMap { id in
            let raw = root?[String(id.rawValue)]
            guard raw != nil || id.assumedPresentWhenMissing else { return nil }
            let entry = raw as? [String: Any]
            let enabled = integer(entry?["enabled"], allowBoolean: true).flatMap {
                $0 == 0 ? false : ($0 == 1 ? true : nil)
            } ?? true
            var chord = id.defaultChord
            if let value = entry?["value"] as? [String: Any],
               let parameters = value["parameters"] as? [Any], parameters.count == 3,
               integer(parameters[0]) != nil,
               let keyCode = integer(parameters[1]), keyCode >= 0,
               let modifiers = integer(parameters[2]), modifiers >= 0 {
                guard keyCode != 65535 else { return nil }
                chord = KeyChord(keyCode: keyCode, rawModifiers: UInt(modifiers))
            }
            return SystemShortcut(id: id.rawValue, chord: chord, isEnabled: enabled)
        }
    }

    /// Plist integers must not silently accept strings, fractions, booleans, or overflow.
    private static func integer(_ value: Any?, allowBoolean: Bool = false) -> Int? {
        guard let number = value as? NSNumber,
              allowBoolean || CFGetTypeID(number) != CFBooleanGetTypeID(),
              let integer = Int(number.stringValue) else { return nil }
        return integer
    }
}
