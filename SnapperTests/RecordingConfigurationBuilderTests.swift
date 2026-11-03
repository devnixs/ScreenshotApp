import Testing
import CoreGraphics
import CoreMedia
import CoreVideo
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
}
