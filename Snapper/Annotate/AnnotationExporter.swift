import AppKit

enum AnnotationExportError: LocalizedError, Equatable {
    case sizeMismatch, contextCreationFailed, imageCreationFailed

    var errorDescription: String? {
        switch self {
        case .sizeMismatch: "The annotation dimensions don't match the captured image."
        case .contextCreationFailed: "The annotated image couldn't be rendered."
        case .imageCreationFailed: "The annotated image couldn't be created."
        }
    }
}

@MainActor
enum AnnotationExporter {
    static func flatten(image: CGImage, document: AnnotationDocument) throws(AnnotationExportError) -> CGImage {
        try flatten(image: image, document: document, assertOnSizeMismatch: true)
    }

    /// Tests disable the programming-error assertion to verify the recoverable error path.
    static func flatten(image: CGImage, document: AnnotationDocument,
                        assertOnSizeMismatch: Bool) throws(AnnotationExportError) -> CGImage {
        guard document.imagePixelSize == CGSize(width: image.width, height: image.height) else {
            if assertOnSizeMismatch { assertionFailure("Annotation and capture dimensions must match") }
            throw .sizeMismatch
        }
        let colorSpace = image.colorSpace.flatMap { $0.model == .rgb ? $0 : nil }
            ?? CGColorSpace(name: CGColorSpace.sRGB)
        guard let colorSpace,
              let context = CGContext(data: nil, width: image.width, height: image.height,
                bitsPerComponent: 8, bytesPerRow: 0, space: colorSpace,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
            throw .contextCreationFailed
        }
        context.interpolationQuality = .none
        context.draw(image, in: CGRect(origin: .zero, size: document.imagePixelSize))
        context.translateBy(x: 0, y: CGFloat(image.height))
        context.scaleBy(x: 1, y: -1)
        NSGraphicsContext.saveGraphicsState()
        defer { NSGraphicsContext.restoreGraphicsState() }
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        ShapeRenderer.draw(document.shapes, pixelsPerPoint: document.pixelsPerPoint, in: context)
        guard let result = context.makeImage() else { throw .imageCreationFailed }
        return result
    }
}
