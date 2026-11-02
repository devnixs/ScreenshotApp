import Testing
@testable import Snapper

struct CapturePurposeTests {
    @Test func purposesHaveStableOrder() {
        #expect(CapturePurpose.allCases == [.screenshot, .record, .annotate])
    }

    @Test func storageIdentifiersNeverChange() {
        #expect(CapturePurpose.screenshot.rawValue == "screenshot")
        #expect(CapturePurpose.record.rawValue == "record")
        #expect(CapturePurpose.annotate.rawValue == "annotate")
    }
}
