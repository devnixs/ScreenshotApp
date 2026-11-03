import Testing
import CoreGraphics
import CoreMedia
import CoreVideo
import ScreenCaptureKit
@testable import Snapper

struct RecordingConfigurationBuilderTests {
    private func build(_ selection: CGRect, frame: CGRect = CGRect(x: 0, y: 0, width: 1512, height: 982),
                       scale: CGFloat = 2) -> RecordingConfiguration {
        RecordingConfigurationBuilder.build(display: DisplayInfo(displayID: 42, frame: frame, backingScale: scale), selection: selection)
    }

    @Test func retinaAndConstants() {
        let result = build(CGRect(x: 100, y: 100, width: 400, height: 300))
        #expect(result.sourceRect == CGRect(x: 100, y: 582, width: 400, height: 300))
        #expect(result.pixelWidth == 800)
        #expect(result.pixelHeight == 600)
        #expect(result.displayID == 42)
        #expect(result.minimumFrameInterval == CMTime(value: 1, timescale: 30))
        #expect(result.pixelFormat == kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange)
        #expect(result.queueDepth == 5)
        #expect(result.showsCursor)
    }

    @Test func oddNonRetinaDimensionsRoundDown() {
        let result = build(CGRect(x: 10, y: 10, width: 101, height: 51), scale: 1)
        #expect(result.sourceRect == CGRect(x: 10, y: 921, width: 100, height: 50))
        #expect(result.pixelWidth == 100)
        #expect(result.pixelHeight == 50)
    }

    @Test func fractionalPointsSnapWithoutRescaling() {
        let result = build(CGRect(x: 10.25, y: 20.5, width: 100.5, height: 50.25))
        #expect(result.sourceRect == CGRect(x: 10.5, y: 911.5, width: 100, height: 50))
        #expect(result.pixelWidth == 200)
        #expect(result.pixelHeight == 100)
    }

    @Test func leftDisplay() {
        let result = build(CGRect(x: -1900, y: 100, width: 200, height: 100),
                           frame: CGRect(x: -1920, y: 0, width: 1920, height: 1080), scale: 1)
        #expect(result.sourceRect == CGRect(x: 20, y: 880, width: 200, height: 100))
    }

    @Test func displayBelowPrimary() {
        let frame = CGRect(x: 0, y: -1080, width: 1920, height: 1080)
        let result = build(frame, frame: frame, scale: 1)
        #expect(result.sourceRect == CGRect(x: 0, y: 0, width: 1920, height: 1080))
        #expect(result.pixelWidth == 1920)
        #expect(result.pixelHeight == 1080)
    }

    @Test func overhangingSelectionClamps() {
        let result = build(CGRect(x: 1400, y: -20, width: 200, height: 200))
        #expect(result.sourceRect == CGRect(x: 1400, y: 802, width: 112, height: 180))
        #expect(result.pixelWidth == 224)
        #expect(result.pixelHeight == 360)
    }

    @Test func fullRetinaDisplay() {
        let result = build(CGRect(x: 0, y: 0, width: 1512, height: 982))
        #expect(result.sourceRect == CGRect(x: 0, y: 0, width: 1512, height: 982))
        #expect(result.pixelWidth == 3024)
        #expect(result.pixelHeight == 1964)
    }

    @Test func fullOddDisplayLosesOnlyOnePixel() {
        let frame = CGRect(x: -100, y: 200, width: 101, height: 51)
        let result = build(frame, frame: frame, scale: 1)
        #expect(result.sourceRect == CGRect(x: 0, y: 0, width: 100, height: 50))
    }

    @Test func minimumSizeStaysInsideAtEdge() {
        let result = build(CGRect(x: 1511.9, y: 0, width: 0.1, height: 0.1), scale: 1)
        #expect(result.sourceRect == CGRect(x: 1510, y: 980, width: 2, height: 2))
        #expect(result.pixelWidth == 2)
        #expect(result.pixelHeight == 2)
    }

    @Test func configurationOverrides() {
        let result = RecordingConfigurationBuilder.build(
            display: DisplayInfo(displayID: 7, frame: CGRect(x: 0, y: 0, width: 100, height: 100), backingScale: 1),
            selection: CGRect(x: 0, y: 0, width: 10, height: 10), showsCursor: false, framesPerSecond: 60)
        #expect(!result.showsCursor)
        #expect(result.minimumFrameInterval == CMTime(value: 1, timescale: 60))
    }
    @Test func windowNativeSizeAndFixedFrameFlags() {
        let samples: [(CGSize, CGFloat, Int, Int)] = [
            (CGSize(width: 800, height: 600), 1, 800, 600),
            (CGSize(width: 800, height: 600), 2, 1600, 1200),
            (CGSize(width: 801, height: 601), 1, 800, 600),
            (CGSize(width: 400.5, height: 300.25), 2, 800, 600),
            (CGSize(width: 1, height: 1), 1, 2, 2),
            (CGSize(width: 5120, height: 2880), 2, 10240, 5760)
        ]
        for (size, scale, width, height) in samples {
            let result = RecordingConfigurationBuilder.buildWindow(contentSize: size, pointPixelScale: scale)
            #expect(result.pixelWidth == width)
            #expect(result.pixelHeight == height)
            #expect(result.scalesToFit)
            #expect(result.preservesAspectRatio)
            #expect(result.ignoresShadowsSingleWindow)
            #expect(result.ignoresGlobalClipSingleWindow)
            #expect(result.showsCursor)
            #expect(result.frameRate == 30)
        }
    }
}

extension RecordingConfigurationBuilderTests {
    private var microphoneInputs: [MicrophoneCaptureInputs] {
        [false, true].flatMap { setting in
            MicrophoneAuthorization.allCases.flatMap { authorization in
                [false, true].map { device in
                    MicrophoneCaptureInputs(recordMicrophoneSetting: setting, format: .mp4,
                        authorization: authorization, hasAudioInputDevice: device)
                }
            }
        }
    }

    private func streamConfigurations(_ microphone: MicrophoneCaptureInputs) -> [SCStreamConfiguration] {
        [RecordingConfigurationBuilder.buildStream(
            rectangle: build(CGRect(x: 10.25, y: 20.5, width: 400.5, height: 300.25)), microphone: microphone),
         RecordingConfigurationBuilder.buildStream(
            window: RecordingConfigurationBuilder.buildWindow(contentSize: CGSize(width: 800.5, height: 600.5),
                pointPixelScale: 2), microphone: microphone)]
    }

    @Test func testCapturesAudioAlwaysFalse() {
        for input in microphoneInputs {
            for config in streamConfigurations(input) { #expect(!config.capturesAudio) }
        }
    }

    @Test func testCaptureMicrophoneFollowsPolicy() {
        for input in microphoneInputs {
            for config in streamConfigurations(input) {
                #expect(config.captureMicrophone == MicrophoneCapturePolicy.shouldCaptureMicrophone(input))
            }
        }
    }

    @Test func testMicrophoneDeviceIDIsSystemDefault() {
        for input in microphoneInputs {
            for config in streamConfigurations(input) { #expect(config.microphoneCaptureDeviceID == nil) }
        }
    }

    @Test func testMicrophoneDoesNotAlterVideoSettings() {
        let on = streamConfigurations(.init(recordMicrophoneSetting: true, format: .mp4,
            authorization: .authorized, hasAudioInputDevice: true))
        let off = streamConfigurations(.init(recordMicrophoneSetting: false, format: .mp4,
            authorization: .authorized, hasAudioInputDevice: true))
        for (enabled, disabled) in zip(on, off) {
            #expect(enabled.width == disabled.width)
            #expect(enabled.height == disabled.height)
            #expect(enabled.sourceRect == disabled.sourceRect)
            #expect(enabled.minimumFrameInterval == disabled.minimumFrameInterval)
            #expect(enabled.showsCursor == disabled.showsCursor)
            #expect(enabled.pixelFormat == disabled.pixelFormat)
            #expect(enabled.queueDepth == disabled.queueDepth)
            #expect(enabled.scalesToFit == disabled.scalesToFit)
            #expect(enabled.preservesAspectRatio == disabled.preservesAspectRatio)
            #expect(enabled.ignoreShadowsSingleWindow == disabled.ignoreShadowsSingleWindow)
            #expect(enabled.ignoreGlobalClipSingleWindow == disabled.ignoreGlobalClipSingleWindow)
        }
    }
}
