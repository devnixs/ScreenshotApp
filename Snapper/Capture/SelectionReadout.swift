import CoreGraphics

enum SelectionReadout {
    static let offset: CGFloat = 12
    static let margin: CGFloat = 4

    static func text(for size: CGSize) -> String {
        "\(Int(size.width.rounded(.toNearestOrAwayFromZero))) × \(Int(size.height.rounded(.toNearestOrAwayFromZero)))"
    }

    static func origin(labelSize: CGSize, cursor: CGPoint, display: CGRect) -> CGPoint {
        let inset = display.insetBy(dx: margin, dy: margin)
        var x = cursor.x + offset
        var y = cursor.y - offset - labelSize.height
        if x + labelSize.width > inset.maxX { x = cursor.x - offset - labelSize.width }
        if y < inset.minY { y = cursor.y + offset }
        return CGPoint(x: max(inset.minX, min(x, inset.maxX - labelSize.width)),
                       y: max(inset.minY, min(y, inset.maxY - labelSize.height)))
    }
}
