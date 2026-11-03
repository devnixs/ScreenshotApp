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

struct WindowRecordingConfiguration: Sendable, Equatable {
    let pixelWidth: Int
    let pixelHeight: Int
    let scalesToFit = true
    let preservesAspectRatio = true
    let ignoresShadowsSingleWindow = true
    let ignoresGlobalClipSingleWindow = true
    let showsCursor = true
    let frameRate = 30
}

enum RecordingConfigurationBuilder {
    private static func evenFloor(_ pixels: CGFloat) -> Int {
        max(2, Int(floor(pixels)) / 2 * 2)
    }

    static func buildWindow(contentSize: CGSize, pointPixelScale: CGFloat) -> WindowRecordingConfiguration {
        WindowRecordingConfiguration(pixelWidth: evenFloor((contentSize.width * pointPixelScale).rounded()),
                                     pixelHeight: evenFloor((contentSize.height * pointPixelScale).rounded()))
    }

    static func build(display: DisplayInfo, selection: CGRect,
                      showsCursor: Bool = true, framesPerSecond: Int = 30) -> RecordingConfiguration {
        let local = DisplayGeometry.displayLocalTopLeftRect(
            DisplayGeometry.clamp(selection, to: display.frame), on: display)
        let scale = display.backingScale
        let width = evenFloor(local.width * scale)
        let height = evenFloor(local.height * scale)
        let x = max(0, min((local.minX * scale).rounded(), floor(display.frame.width * scale) - CGFloat(width)))
        let y = max(0, min((local.minY * scale).rounded(), floor(display.frame.height * scale) - CGFloat(height)))
        return RecordingConfiguration(displayID: display.displayID,
            sourceRect: CGRect(x: x / scale, y: y / scale, width: CGFloat(width) / scale, height: CGFloat(height) / scale),
            pixelWidth: width, pixelHeight: height,
            minimumFrameInterval: CMTime(value: 1, timescale: CMTimeScale(max(1, framesPerSecond))),
            pixelFormat: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange, queueDepth: 5, showsCursor: showsCursor)
    }
}
