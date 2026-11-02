import Testing
@testable import Snapper

struct StatusMenuLayoutTests {
    @Test func entriesFollowProductOrder() {
        #expect(StatusMenuLayout.entries == [.screenshot, .record, .annotate, .separator, .settings, .quit])
    }

    @Test func captureEntriesMapToTheirPurpose() {
        #expect(StatusMenuLayout.entries.map(\.capturePurpose) == [
            .screenshot, .record, .annotate, nil, nil, nil
        ])
    }

    @Test func titlesMatchProductMenu() {
        #expect(StatusMenuLayout.entries.map(\.title) == [
            "Screenshot", "Record", "Annotate", nil, "Settings…", "Quit Snapper"
        ])
    }
}
