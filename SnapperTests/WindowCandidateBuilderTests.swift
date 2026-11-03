import CoreGraphics
import Testing
@testable import Snapper

struct WindowCandidateBuilderTests {
    private func window(_ id: CGWindowID) -> WindowCandidate {
        WindowCandidate(windowID: id, frame: CGRect(x: 1, y: 2, width: 100, height: 200), layer: 0,
                        ownerPID: 20, ownerBundleID: "test.app", isOnScreen: true, alpha: 0.5, title: "Test")
    }

    @Test func cgOrderWinsOverShareableContentOrder() {
        let built = WindowCandidateBuilder.build(windows: [window(1), window(2), window(3)],
                                                frontToBackIDs: [3, 1, 2], alphaByID: [:])
        #expect(built.map(\.windowID) == [3, 1, 2])
    }

    @Test func dropsWindowsMissingFromCGListAndIgnoresUnknownCGIDs() {
        let built = WindowCandidateBuilder.build(windows: [window(1), window(2)],
                                                frontToBackIDs: [99, 2], alphaByID: [:])
        #expect(built.map(\.windowID) == [2])
        #expect(WindowCandidateBuilder.build(windows: [], frontToBackIDs: [1], alphaByID: [:]).isEmpty)
        #expect(WindowCandidateBuilder.build(windows: [window(1)], frontToBackIDs: [], alphaByID: [:]).isEmpty)
    }

    @Test func alphaJoinedDefaultsToOneAndMetadataPreserved() {
        let built = WindowCandidateBuilder.build(windows: [window(1), window(2)],
                                                frontToBackIDs: [2, 1], alphaByID: [2: 0])
        var expected = window(2)
        expected.alpha = 0
        #expect(built[0] == expected)
        #expect(built[1].alpha == 1)
    }
}
