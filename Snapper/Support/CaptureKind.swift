enum CaptureKind: Sendable, Equatable, CaseIterable {
    case screenshot
    case recording

    var namePrefix: String {
        switch self {
        case .screenshot: "Screenshot"
        case .recording: "Recording"
        }
    }

    var fileExtension: String {
        switch self {
        case .screenshot: "png"
        case .recording: "mp4"
        }
    }
}
