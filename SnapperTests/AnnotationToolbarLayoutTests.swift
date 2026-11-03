import AppKit
import Testing
@testable import Snapper

@MainActor
struct AnnotationToolbarLayoutTests {
    @Test func copyAndCloseIsLastAfterSaveAndCopy() {
        #expect(AnnotationWindowController.toolbarItems == [
            AnnotationWindowController.toolsIdentifier,
            AnnotationWindowController.styleIdentifier,
            .flexibleSpace,
            AnnotationWindowController.saveIdentifier,
            AnnotationWindowController.copyIdentifier,
            AnnotationWindowController.copyAndCloseIdentifier
        ])
        #expect(AnnotationWindowController.toolbarItems.last == AnnotationWindowController.copyAndCloseIdentifier)
    }
}
