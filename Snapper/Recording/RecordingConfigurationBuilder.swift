import CoreGraphics
import CoreMedia
import CoreVideo
import ScreenCaptureKit

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

// Deterministic framework configuration: no permission requests or device discovery here.
extension RecordingConfigurationBuilder {
    static func buildStream(rectangle: RecordingConfiguration,
                            microphone: MicrophoneCaptureInputs) -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        config.sourceRect = rectangle.sourceRect
        config.width = rectangle.pixelWidth
        config.height = rectangle.pixelHeight
        config.minimumFrameInterval = rectangle.minimumFrameInterval
        config.pixelFormat = rectangle.pixelFormat
        config.queueDepth = rectangle.queueDepth
        config.showsCursor = rectangle.showsCursor
        config.scalesToFit = false
        configureAudio(config, microphone: microphone)
        return config
    }

    static func buildStream(window: WindowRecordingConfiguration,
                            microphone: MicrophoneCaptureInputs) -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        config.width = window.pixelWidth
        config.height = window.pixelHeight
        config.scalesToFit = window.scalesToFit
        config.preservesAspectRatio = window.preservesAspectRatio
        config.ignoreShadowsSingleWindow = window.ignoresShadowsSingleWindow
        config.ignoreGlobalClipSingleWindow = window.ignoresGlobalClipSingleWindow
        config.showsCursor = window.showsCursor
        config.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(window.frameRate))
        config.pixelFormat = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
        config.queueDepth = 5
        configureAudio(config, microphone: microphone)
        return config
    }

    private static func configureAudio(_ config: SCStreamConfiguration, microphone: MicrophoneCaptureInputs) {
        config.capturesAudio = false // System/app audio is never recorded.
        config.captureMicrophone = MicrophoneCapturePolicy.shouldCaptureMicrophone(microphone)
        config.microphoneCaptureDeviceID = nil // Resolve the system default when capture starts.
    }
}
