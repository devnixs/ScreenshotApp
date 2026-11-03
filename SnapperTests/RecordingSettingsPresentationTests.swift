import Foundation
import Testing
@testable import Snapper

struct RecordingSettingsPresentationTests {
    @Test func presentationForEachFormat() {
        #expect(RecordingSettingsPresentation.microphoneToggleEnabled(format: .mp4))
        #expect(!RecordingSettingsPresentation.microphoneToggleEnabled(format: .gif))
        #expect(RecordingSettingsPresentation.microphoneNote(format: .mp4) == nil)
        #expect(RecordingSettingsPresentation.microphoneNote(format: .gif) == "GIFs don't include sound.")
        #expect(RecordingSettingsPresentation.qualityCaption(format: .mp4) == "Medium and Low re-encode after you stop, for smaller files.")
        #expect(RecordingSettingsPresentation.qualityCaption(format: .gif) == "High: 15 fps, full size. Medium: 10 fps, 1×. Low: 8 fps, up to 640 px.")
        #expect(RecordingFailure.gifConversion("problem").title == "Couldn't create the GIF")
    }

    @MainActor @Test func perFormatQualityPersistsWithoutChangingMicrophone() throws {
        let suite = "SnapperRecordingPresentationTests.\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = AppSettings(defaults: defaults)
        settings.recordMicrophone = true
        let model = SettingsModel(settings: settings)
        #expect(model.recordingFormat == .mp4)
        #expect(model.recordingQuality == .high)
        model.recordingQuality = .low
        model.recordingFormat = .gif
        #expect(model.recordingQuality == .medium)
        #expect(model.recordMicrophone)
        model.recordingQuality = .high
        model.recordingFormat = .mp4
        #expect(model.recordingQuality == .low)
        #expect(model.recordMicrophone)
        let reloaded = AppSettings(defaults: defaults)
        #expect(reloaded.recordingFormat == .mp4)
        #expect(reloaded.mp4Quality == .low)
        #expect(reloaded.gifQuality == .high)
        #expect(reloaded.recordMicrophone)
    }
}
