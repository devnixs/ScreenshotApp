import Testing
@testable import Snapper

struct RecordingOutputPlanTests {
    @Test func bitrateLevels() {
        #expect(MP4Bitrate.averageBitRate(pixelWidth: 2880, pixelHeight: 1800, quality: .high) == nil)
        #expect(MP4Bitrate.averageBitRate(pixelWidth: 2880, pixelHeight: 1800, quality: .medium) == 15_552_000)
        #expect(MP4Bitrate.averageBitRate(pixelWidth: 2880, pixelHeight: 1800, quality: .low) == 7_776_000)
        #expect(MP4Bitrate.averageBitRate(pixelWidth: 1920, pixelHeight: 1080, quality: .medium) == 6_220_800)
    }
    @Test func mp4PlansIgnoreGifQualityAndScale() {
        for gifQuality in RecordingQuality.allCases {
            for scale in [1.0, 2.0] {
                let high = RecordingOutputPlan.make(format: .mp4, mp4Quality: .high, gifQuality: gifQuality,
                    pixelWidth: 1000, pixelHeight: 500, pointPixelScale: scale)
                #expect(high.transcode == nil)
                #expect(high.finalExtension == "mp4")
                let medium = RecordingOutputPlan.make(format: .mp4, mp4Quality: .medium, gifQuality: gifQuality,
                    pixelWidth: 1000, pixelHeight: 500, pointPixelScale: scale)
                #expect(medium.transcode == .mp4(averageBitRate: 1_500_000))
                let low = RecordingOutputPlan.make(format: .mp4, mp4Quality: .low, gifQuality: gifQuality,
                    pixelWidth: 1000, pixelHeight: 500, pointPixelScale: scale)
                #expect(low.transcode == .mp4(averageBitRate: 750_000))
            }
        }
    }
    @Test func formatAndQualityTitles() {
        #expect(RecordingFormat.allCases.map(\.title) == ["MP4", "GIF"])
        #expect(RecordingFormat.allCases.map(\.fileExtension) == ["mp4", "gif"])
        #expect(RecordingQuality.allCases.map(\.title) == ["High", "Medium", "Low"])
    }
    @Test func gifIsDeferredTo0024() {
        #expect(RecordingOutputPlan.make(format: .gif, mp4Quality: .low, gifQuality: .low,
            pixelWidth: 100, pixelHeight: 100, pointPixelScale: 1) == RecordingOutputPlan(format: .mp4, transcode: nil))
    }
}
