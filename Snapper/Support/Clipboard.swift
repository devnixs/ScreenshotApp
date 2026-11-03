import AppKit

@MainActor
enum Clipboard {
    @discardableResult
    static func copy(pngData: Data, image: CGImage) -> Bool {
        let item = NSPasteboardItem()
        item.setData(pngData, forType: .png)
        if let tiff = NSBitmapImageRep(cgImage: image).tiffRepresentation {
            item.setData(tiff, forType: .tiff)
        }
        NSPasteboard.general.clearContents()
        return NSPasteboard.general.writeObjects([item])
    }
}
