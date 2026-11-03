import CoreGraphics

/// Immutable preferences captured before any asynchronous capture setup.
struct RecordingExportSettings: Equatable, Sendable {
    let format: RecordingFormat
    let mp4Quality: RecordingQuality
    let gifQuality: RecordingQuality

    func plan(pixelWidth: Int, pixelHeight: Int, pointPixelScale: CGFloat) -> RecordingOutputPlan {
        RecordingOutputPlan.make(format: format, mp4Quality: mp4Quality, gifQuality: gifQuality,
                                 pixelWidth: pixelWidth, pixelHeight: pixelHeight, pointPixelScale: pointPixelScale)
    }
}

enum RecordingTranscode: Equatable, Sendable {
    case mp4(averageBitRate: Int)
    case gif(GIFEncodingSettings, width: Int, height: Int)
}

struct RecordingOutputPlan: Equatable, Sendable {
    let format: RecordingFormat
    let transcode: RecordingTranscode?
    var finalExtension: String { format.fileExtension }

    static func make(format: RecordingFormat, mp4Quality: RecordingQuality, gifQuality: RecordingQuality,
                     pixelWidth: Int, pixelHeight: Int, pointPixelScale: CGFloat) -> RecordingOutputPlan {
        if format == .gif {
            let settings = GIFEncodingSettings.forQuality(gifQuality)
            let size = settings.outputPixelSize(sourceWidth: pixelWidth, sourceHeight: pixelHeight,
                                               pointPixelScale: pointPixelScale)
            return RecordingOutputPlan(format: .gif, transcode: .gif(settings, width: size.width, height: size.height))
        }
        return RecordingOutputPlan(format: .mp4,
            transcode: MP4Bitrate.averageBitRate(pixelWidth: pixelWidth, pixelHeight: pixelHeight, quality: mp4Quality)
                .map { .mp4(averageBitRate: $0) })
    }
}

enum MP4Bitrate {
    static let framesPerSecond = 30
    static func bitsPerPixelPerFrame(_ quality: RecordingQuality) -> Double? {
        switch quality {
        case .high: nil
        case .medium: 0.10
        case .low: 0.05
        }
    }
    static func averageBitRate(pixelWidth: Int, pixelHeight: Int, quality: RecordingQuality) -> Int? {
        guard let factor = bitsPerPixelPerFrame(quality) else { return nil }
        return Int((Double(pixelWidth) * Double(pixelHeight) * Double(framesPerSecond) * factor).rounded())
    }
}
