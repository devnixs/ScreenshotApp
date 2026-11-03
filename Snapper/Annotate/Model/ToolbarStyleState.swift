struct ShapeStyle: Equatable, Sendable {
    var color: RGBAColor
    var strokeWidth: StrokeWidth
}

extension AnnotationShape {
    var style: ShapeStyle { ShapeStyle(color: color, strokeWidth: strokeWidth) }
}

enum SwatchSelection: Equatable, Sendable { case preset(Int), custom }
enum ToolbarStyleEvent: Equatable, Sendable {
    case selectedShapeStyleChanged(ShapeStyle?)
    case colorPicked(RGBAColor)
    case strokeWidthPicked(StrokeWidth)
}
struct ToolbarStyleEffects: Equatable, Sendable {
    var applyColorToSelection: RGBAColor?
    var applyStrokeWidthToSelection: StrokeWidth?
    var persistColor: RGBAColor?
    var persistStrokeWidth: StrokeWidth?
}
struct ToolbarStyleState: Equatable, Sendable {
    private(set) var currentStyle: ShapeStyle
    private(set) var selectedShapeStyle: ShapeStyle?
    init(currentStyle: ShapeStyle) { self.currentStyle = currentStyle }
    var displayedStyle: ShapeStyle { selectedShapeStyle ?? currentStyle }
    var swatchSelection: SwatchSelection {
        RGBAColor.presets.firstIndex(of: displayedStyle.color).map(SwatchSelection.preset) ?? .custom
    }
    mutating func reduce(_ event: ToolbarStyleEvent) -> ToolbarStyleEffects {
        var effects = ToolbarStyleEffects()
        switch event {
        case .selectedShapeStyleChanged(let style): selectedShapeStyle = style
        case .colorPicked(let color):
            currentStyle.color = color
            effects.persistColor = color
            if let selected = selectedShapeStyle, selected.color != color {
                selectedShapeStyle?.color = color
                effects.applyColorToSelection = color
            }
        case .strokeWidthPicked(let width):
            currentStyle.strokeWidth = width
            effects.persistStrokeWidth = width
            if let selected = selectedShapeStyle, selected.strokeWidth != width {
                selectedShapeStyle?.strokeWidth = width
                effects.applyStrokeWidthToSelection = width
            }
        }
        return effects
    }
}
