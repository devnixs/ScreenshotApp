enum ScreenshotDestination: String, CaseIterable, Sendable {
    case file, clipboard, both

    var savesToFile: Bool { self != .clipboard }
    var copiesToClipboard: Bool { self != .file }

    var title: String {
        switch self {
        case .file: "Save to file"
        case .clipboard: "Copy to clipboard"
        case .both: "Both"
        }
    }
}
