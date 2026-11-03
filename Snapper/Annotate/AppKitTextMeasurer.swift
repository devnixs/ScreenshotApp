import AppKit

struct AppKitTextMeasurer: TextMeasuring {
    func lineHeight(fontPixelSize: CGFloat) -> CGFloat {
        ceil(NSLayoutManager().defaultLineHeight(for: TextStyle.font(size: fontPixelSize)))
    }
    func layoutSize(of text: String, fontPixelSize: CGFloat) -> CGSize {
        // Include a final empty line fragment, which boundingRect otherwise omits.
        let measuredText = text.isEmpty || text.hasSuffix("\n") ? text + " " : text
        let rect = NSAttributedString(string: measuredText, attributes: TextStyle.attributes(size: fontPixelSize, color: .black))
            .boundingRect(with: CGSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude),
                          options: [.usesLineFragmentOrigin, .usesFontLeading])
        return CGSize(width: text.isEmpty ? 0 : ceil(rect.width), height: max(lineHeight(fontPixelSize: fontPixelSize), ceil(rect.height)))
    }
}
