import Testing
@testable import Snapper

struct ToolbarStyleStateTests {
    let initial = ShapeStyle(color: .red, strokeWidth: .m)
    @Test func selectionOnlyChangesDisplayedStyle() {
        var state = ToolbarStyleState(currentStyle: initial)
        #expect(state.displayedStyle == initial && state.swatchSelection == .preset(0))
        #expect(state.reduce(.selectedShapeStyleChanged(ShapeStyle(color: .blue, strokeWidth: .l))) == ToolbarStyleEffects())
        #expect(state.displayedStyle == ShapeStyle(color: .blue, strokeWidth: .l))
        #expect(state.currentStyle == initial)
        _ = state.reduce(.selectedShapeStyleChanged(nil))
        #expect(state.displayedStyle == initial)
    }
    @Test func explicitPicksPersistAndApplyOnlyChanges() {
        for selected in [false, true] {
            var state = ToolbarStyleState(currentStyle: initial)
            if selected { _ = state.reduce(.selectedShapeStyleChanged(ShapeStyle(color: .blue, strokeWidth: .l))) }
            let color = state.reduce(.colorPicked(.green))
            #expect(color.persistColor == .green && color.persistStrokeWidth == nil)
            #expect(color.applyColorToSelection == (selected ? .green : nil))
            #expect(state.currentStyle == ShapeStyle(color: .green, strokeWidth: .m))
            #expect(state.reduce(.colorPicked(.green)).applyColorToSelection == nil)
            let width = state.reduce(.strokeWidthPicked(.s))
            #expect(width.persistStrokeWidth == .s && width.persistColor == nil)
            #expect(width.applyStrokeWidthToSelection == (selected ? .s : nil))
            #expect(state.reduce(.strokeWidthPicked(.s)).applyStrokeWidthToSelection == nil)
            #expect(state.currentStyle == ShapeStyle(color: .green, strokeWidth: .s))
        }
    }
    @Test func swatchesAndIndependentWindows() {
        var state = ToolbarStyleState(currentStyle: initial)
        let other = state
        for (index, color) in RGBAColor.presets.enumerated() {
            _ = state.reduce(.colorPicked(color))
            #expect(state.swatchSelection == .preset(index))
        }
        _ = state.reduce(.colorPicked(RGBAColor(red: 0.5, green: 0.5, blue: 0.5)))
        #expect(state.swatchSelection == .custom)
        #expect(other.currentStyle == initial)
    }
    @Test func undoRestylesSelectionWithoutRevertingPicks() {
        var state = ToolbarStyleState(currentStyle: initial)
        _ = state.reduce(.selectedShapeStyleChanged(initial))
        _ = state.reduce(.colorPicked(.blue))
        _ = state.reduce(.selectedShapeStyleChanged(initial))
        #expect(state.displayedStyle == initial)
        #expect(state.currentStyle.color == .blue)
    }
}
