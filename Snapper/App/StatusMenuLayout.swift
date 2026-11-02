enum StatusMenuEntry: Equatable {
    case screenshot, record, annotate, separator, settings, quit

    var capturePurpose: CapturePurpose? {
        switch self {
        case .screenshot: .screenshot
        case .record: .record
        case .annotate: .annotate
        case .separator, .settings, .quit: nil
        }
    }

    var title: String? {
        switch self {
        case .screenshot: "Screenshot"
        case .record: "Record"
        case .annotate: "Annotate"
        case .separator: nil
        case .settings: "Settings…"
        case .quit: "Quit Snapper"
        }
    }
}

enum StatusMenuLayout {
    static let entries: [StatusMenuEntry] = [
        .screenshot, .record, .annotate, .separator, .settings, .quit
    ]
}
