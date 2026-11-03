enum RecordingSettingsPresentation {
    static func microphoneToggleEnabled(format: RecordingFormat) -> Bool { format == .mp4 }
    static func microphoneNote(format: RecordingFormat) -> String? {
        format == .gif ? "GIFs don't include sound." : nil
    }
    static func qualityCaption(format: RecordingFormat) -> String {
        switch format {
        case .mp4: "Medium and Low re-encode after you stop, for smaller files."
        case .gif: "High: 15 fps, full size. Medium: 10 fps, 1×. Low: 8 fps, up to 640 px."
        }
    }
}
