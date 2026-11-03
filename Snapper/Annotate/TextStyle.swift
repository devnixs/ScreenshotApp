import AppKit

enum TextStyle {
    static func font(size: CGFloat) -> NSFont { .systemFont(ofSize: size, weight: .bold) }
    static func attributes(size: CGFloat, color: RGBAColor) -> [NSAttributedString.Key: Any] {
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineBreakMode = .byClipping
        paragraph.paragraphSpacing = 0
        paragraph.lineSpacing = 0
        return [.font: font(size: size), .foregroundColor: color.nsColor, .paragraphStyle: paragraph]
    }
    static func strokeAttributes(size: CGFloat, color: RGBAColor, editing: Bool = false) -> [NSAttributedString.Key: Any] {
        var result = attributes(size: size, color: color)
        result[.strokeColor] = ContrastingOutline.color(for: color).nsColor
        result[.strokeWidth] = editing ? -ContrastingOutline.strokeWidthPercent : ContrastingOutline.strokeWidthPercent
        return result
    }
}
