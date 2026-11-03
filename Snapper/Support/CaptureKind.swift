enum CaptureKind: Sendable, Equatable {
    case screenshot
    case recording(RecordingFormat)

    var namePrefix: String {
        switch self {
        case .screenshot: "Screenshot"
        case .recording: "Recording"
        }
    }

    var fileExtension: String {
        switch self {
        case .screenshot: "png"
        case .recording(let format): format.fileExtension
        }
    }
}
