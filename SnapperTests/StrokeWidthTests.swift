import Testing
@testable import Snapper

struct StrokeWidthTests {
    @Test func casesOrder() { #expect(StrokeWidth.allCases == [.s, .m, .l]) }
    @Test func points() { #expect(StrokeWidth.allCases.map(\.points) == [2, 4, 8]) }
    @Test func textPointSize() { #expect(StrokeWidth.allCases.map(\.textPointSize) == [16, 24, 36]) }
    @Test func rawValuesAndLabels() {
        #expect(StrokeWidth.allCases.map(\.rawValue) == ["s", "m", "l"])
        #expect(StrokeWidth.allCases.map(\.label) == ["S", "M", "L"])
    }
}
