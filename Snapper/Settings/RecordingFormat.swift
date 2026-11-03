enum RecordingFormat: String, CaseIterable, Sendable {
    case mp4, gif
    var title: String { rawValue.uppercased() }
    var fileExtension: String { rawValue }
}
