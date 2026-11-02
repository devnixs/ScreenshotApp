import KeyboardShortcuts

extension KeyboardShortcuts.Name {
    static let screenshot = Self(CapturePurpose.screenshot.rawValue, initial: .init(.four, modifiers: [.command, .shift]))
    static let record = Self(CapturePurpose.record.rawValue, initial: .init(.five, modifiers: [.command, .shift]))
    static let annotate = Self(CapturePurpose.annotate.rawValue, initial: .init(.six, modifiers: [.command, .shift]))
}

extension CapturePurpose {
    var shortcutName: KeyboardShortcuts.Name {
        switch self {
        case .screenshot: .screenshot
        case .record: .record
        case .annotate: .annotate
        }
    }
}
