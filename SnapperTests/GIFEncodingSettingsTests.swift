import Testing
@testable import Snapper

struct GIFEncodingSettingsTests {
    @Test func qualityPresets() {
        #expect(GIFEncodingSettings.forQuality(.high) == .init(framesPerSecond: 15, usesPointSize: false, maxLongestSide: nil))
        #expect(GIFEncodingSettings.forQuality(.medium) == .init(framesPerSecond: 10, usesPointSize: true, maxLongestSide: nil))
        #expect(GIFEncodingSettings.forQuality(.low) == .init(framesPerSecond: 8, usesPointSize: true, maxLongestSide: 640))
    }

    @Test func sizesRespectScaleRoundingCapAndNeverUpscale() {
        let cases: [(RecordingQuality, Int, Int, Double, Int, Int)] = [
            (.high, 2880, 1800, 2, 2880, 1800),
            (.medium, 2880, 1800, 2, 1440, 900), (.medium, 1000, 500, 1, 1000, 500),
            (.medium, 1001, 501, 2, 501, 251),
            (.low, 2880, 1800, 2, 640, 400), (.low, 1000, 500, 1, 640, 320),
            (.low, 800, 1600, 1, 320, 640), (.low, 300, 200, 2, 150, 100),
            (.low, 8, 8, 2, 4, 4), (.medium, 2, 2, 2, 1, 1),
            (.medium, 1, 1, 2, 1, 1), (.medium, 100, 50, 0.5, 100, 50),
            (.low, 100, 50, 1, 100, 50), (.medium, 900, 600, 1.5, 600, 400)
        ]
        for (quality, width, height, scale, expectedWidth, expectedHeight) in cases {
            let size = GIFEncodingSettings.forQuality(quality).outputPixelSize(
                sourceWidth: width, sourceHeight: height, pointPixelScale: scale)
            #expect(size.width == expectedWidth)
            #expect(size.height == expectedHeight)
        }
    }
}
