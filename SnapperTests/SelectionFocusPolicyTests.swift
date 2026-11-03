import Testing
@testable import Snapper

struct SelectionFocusPolicyTests {
    @Test func cancelledAlwaysRestores() {
        for purpose in CapturePurpose.allCases {
            #expect(SelectionFocusPolicy.restoresPreviousApplication(purpose: purpose, outcome: .cancelled))
        }
    }

    @Test func screenshotSelectionRestores() {
        #expect(SelectionFocusPolicy.restoresPreviousApplication(purpose: .screenshot, outcome: .selected))
    }

    @Test func recordSelectionRestores() {
        #expect(SelectionFocusPolicy.restoresPreviousApplication(purpose: .record, outcome: .selected))
    }

    @Test func annotateSelectionKeepsFocus() {
        #expect(!SelectionFocusPolicy.restoresPreviousApplication(purpose: .annotate, outcome: .selected))
    }
}
