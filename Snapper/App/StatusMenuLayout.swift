enum StatusMenuEntry: Equatable {
    case screenshot, record, annotate, separator, settings, quit

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
