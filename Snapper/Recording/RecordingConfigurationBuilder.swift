import CoreGraphics
import CoreMedia
import CoreVideo

struct RecordingConfiguration: Sendable, Equatable {
    let displayID: CGDirectDisplayID
    let sourceRect: CGRect
    let pixelWidth: Int
    let pixelHeight: Int
    let minimumFrameInterval: CMTime
    let pixelFormat: OSType
    let queueDepth: Int
    let showsCursor: Bool
}

enum RecordingConfigurationBuilder {
    static func build(display: DisplayInfo, selection: CGRect,
                      showsCursor: Bool = true, framesPerSecond: Int = 30) -> RecordingConfiguration {
        let local = DisplayGeometry.displayLocalTopLeftRect(
            DisplayGeometry.clamp(selection, to: display.frame), on: display)
        let scale = display.backingScale
        let width = max(2, Int(floor(local.width * scale)) / 2 * 2)
        let height = max(2, Int(floor(local.height * scale)) / 2 * 2)
        let x = max(0, min((local.minX * scale).rounded(), floor(display.frame.width * scale) - CGFloat(width)))
        let y = max(0, min((local.minY * scale).rounded(), floor(display.frame.height * scale) - CGFloat(height)))
        return RecordingConfiguration(displayID: display.displayID,
            sourceRect: CGRect(x: x / scale, y: y / scale, width: CGFloat(width) / scale, height: CGFloat(height) / scale),
            pixelWidth: width, pixelHeight: height,
            minimumFrameInterval: CMTime(value: 1, timescale: CMTimeScale(max(1, framesPerSecond))),
            pixelFormat: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange, queueDepth: 5, showsCursor: showsCursor)
    }
}
