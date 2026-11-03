import Testing
@testable import Snapper

struct CopyAndCloseDecisionTests {
    @Test func idleWindowCanCopy() {
        #expect(CopyAndCloseDecision.start(isDragging: false, hasAttachedSheet: false) == .copy)
    }

    @Test func draggingRefusesCopy() {
        #expect(CopyAndCloseDecision.start(isDragging: true, hasAttachedSheet: false) == .refuse)
    }

    @Test func attachedSheetRefusesCopy() {
        #expect(CopyAndCloseDecision.start(isDragging: false, hasAttachedSheet: true) == .refuse)
    }

    @Test func draggingWithSheetRefusesCopy() {
        #expect(CopyAndCloseDecision.start(isDragging: true, hasAttachedSheet: true) == .refuse)
    }

    @Test func successfulCopyCloses() {
        #expect(CopyAndCloseDecision.finish(copySucceeded: true) == .close)
    }

    @Test func failedCopyStaysOpen() {
        #expect(CopyAndCloseDecision.finish(copySucceeded: false) == .stayOpen)
    }
}
