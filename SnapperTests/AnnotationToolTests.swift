import Testing
@testable import Snapper

struct AnnotationToolTests {
    @Test func routingAndSegments() {
        for (key, tool) in [("v", AnnotationTool.select), ("r", .rectangle), ("a", .arrow), ("t", .text)] {
            for key in [key, key.uppercased()] {
                #expect(AnnotationTool.forShortcut(characters: key, hasCommandOptionOrControl: false, textToolEnabled: true) == tool)
                #expect(AnnotationTool.forShortcut(characters: key, hasCommandOptionOrControl: true, textToolEnabled: true) == nil)
            }
        }
        for key in ["t", "x", "", "rr"] {
            #expect(AnnotationTool.forShortcut(characters: key, hasCommandOptionOrControl: false, textToolEnabled: false) == nil)
        }
        for tool in AnnotationTool.allCases { #expect(AnnotationTool(segmentIndex: tool.segmentIndex) == tool) }
        #expect(AnnotationTool(segmentIndex: -1) == nil)
        #expect(AnnotationTool(segmentIndex: 4) == nil)
        #expect(AnnotationTool.rectangle.createsShapesByDragging && AnnotationTool.arrow.createsShapesByDragging)
        #expect(!AnnotationTool.select.createsShapesByDragging && !AnnotationTool.text.createsShapesByDragging)
    }
}
