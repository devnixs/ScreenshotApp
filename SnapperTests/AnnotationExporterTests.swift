import AppKit
import ImageIO
import Testing
@testable import Snapper

@MainActor
struct AnnotationExporterTests {
    let red = RGBAColor(red: 1, green: 0, blue: 0)
    func source(width: Int = 200, height: Int = 100, space: CGColorSpace = CGColorSpace(name: CGColorSpace.sRGB)!) -> CGImage {
        let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
            bytesPerRow: 0, space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(CGColor(srgbRed: 0, green: 0, blue: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        return context.makeImage()!
    }
    func document(width: Int = 200, height: Int = 100, scale: CGFloat = 1) -> AnnotationDocument {
        AnnotationDocument(imagePixelSize: CGSize(width: width, height: height), pixelsPerPoint: scale)
    }
    func rectangle(_ rect: CGRect = CGRect(x: 20, y: 10, width: 100, height: 50)) -> AnnotationShape {
        AnnotationShape(kind: .rectangle(rect), color: red, strokeWidth: .m)
    }
    /// CGImage data rows are top-down; normalizing by drawing into a standard RGBA8 bitmap
    /// preserves that row convention without depending on the source's byte layout.
    func bytes(_ image: CGImage) -> [UInt8] {
        var buffer = [UInt8](repeating: 0, count: image.width * image.height * 4)
        buffer.withUnsafeMutableBytes { storage in
            let context = CGContext(data: storage.baseAddress!, width: image.width, height: image.height,
                bitsPerComponent: 8, bytesPerRow: image.width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        }
        return buffer
    }
    func pixel(_ image: CGImage, _ x: Int, _ y: Int) -> [UInt8] {
        let offset = (y * image.width + x) * 4
        return Array(bytes(image)[offset..<offset + 4])
    }
    @Test(arguments: [1, 2]) func fullPixelSizeAndDPI(scale: Int) throws {
        let image = source(width: 300 * scale, height: 200 * scale)
        var doc = document(width: image.width, height: image.height, scale: CGFloat(scale))
        doc.add(rectangle())
        let result = try AnnotationExporter.flatten(image: image, document: doc)
        #expect(result.width == image.width && result.height == image.height)
        let png = try PNGEncoder.data(for: result, pixelsPerPoint: doc.pixelsPerPoint)
        let decoded = CGImageSourceCreateWithData(png as CFData, nil)!
        let properties = CGImageSourceCopyPropertiesAtIndex(decoded, 0, nil)! as NSDictionary
        #expect(abs((properties[kCGImagePropertyDPIWidth] as! Double) - Double(72 * scale)) < 0.1)
    }
    @Test func rectangleOrientationInteriorAndStrokeScale() throws {
        let image = source()
        var doc = document()
        doc.add(rectangle())
        let result = try AnnotationExporter.flatten(image: image, document: doc)
        #expect(pixel(result, 70, 10) == [255, 0, 0, 255])
        #expect(pixel(result, 70, 35) == [0, 0, 255, 255])
        #expect(pixel(result, 180, 90) == [0, 0, 255, 255])
        #expect(pixel(result, 70, 90) == [0, 0, 255, 255])
        #expect(pixel(result, 70, 13) == [0, 0, 255, 255])
        var retina = document(scale: 2)
        retina.add(rectangle())
        #expect(pixel(try AnnotationExporter.flatten(image: image, document: retina), 70, 13) == [255, 0, 0, 255])
    }
    @Test func baseImageOrientationAndNoShapes() throws {
        let image = source()
        let context = CGContext(data: nil, width: 200, height: 100, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.draw(image, in: CGRect(x: 0, y: 0, width: 200, height: 100))
        context.setFillColor(CGColor(srgbRed: 0, green: 1, blue: 0, alpha: 1))
        context.fill(CGRect(x: 0, y: 80, width: 200, height: 20))
        let asymmetric = context.makeImage()!
        let output = try AnnotationExporter.flatten(image: asymmetric, document: document())
        #expect(bytes(output) == bytes(asymmetric))
        #expect(pixel(output, 50, 5) == [0, 255, 0, 255])
    }
    @Test func arrowAndClipping() throws {
        var doc = document()
        doc.add(AnnotationShape(kind: .arrow(start: CGPoint(x: 20, y: 80), end: CGPoint(x: 180, y: 80)), color: red, strokeWidth: .m))
        doc.add(rectangle(CGRect(x: -20, y: -20, width: 30, height: 30)))
        let result = try AnnotationExporter.flatten(image: source(), document: doc)
        #expect(pixel(result, 100, 80) == [255, 0, 0, 255])
        #expect(pixel(result, 100, 20) == [0, 0, 255, 255])
        #expect(pixel(result, 5, 10) == [255, 0, 0, 255])
    }
    @Test func textDrawsInsideTopLeftBounds() throws {
        var doc = document()
        doc.add(AnnotationShape(kind: .text(TextBox(origin: CGPoint(x: 10, y: 5), string: "Hi",
            measuredSize: CGSize(width: 60, height: 32))), color: red, strokeWidth: .m))
        let result = try AnnotationExporter.flatten(image: source(), document: doc)
        let data = bytes(result)
        let hasInk = (5..<37).contains { y in (10..<70).contains { x in
            Array(data[((y * 200 + x) * 4)..<((y * 200 + x) * 4 + 4)]) != [0, 0, 255, 255]
        } }
        #expect(hasInk)
        #expect(pixel(result, 40, 85) == [0, 0, 255, 255])
    }
    @Test func zoomAndSelectionDoNotAffectExport() throws {
        var outputs: [[UInt8]] = []
        for scale in [0.25, 1.0] {
            var doc = document()
            let transform = CanvasTransform(imagePixelSize: doc.imagePixelSize, pixelsPerPoint: 1,
                viewSize: CGSize(width: 200 * scale, height: 100 * scale))
            let rect = CGRect(x: 20, y: 10, width: 100, height: 50)
            let shape = rectangle(transform.imageRect(fromView: transform.viewRect(fromImage: rect))!)
            doc.add(shape)
            outputs.append(bytes(try AnnotationExporter.flatten(image: source(), document: doc)))
            doc.select(id: shape.id)
            #expect(bytes(try AnnotationExporter.flatten(image: source(), document: doc)) == outputs.last!)
        }
        #expect(outputs[0] == outputs[1])
    }
    @Test func shapesRenderBackToFront() throws {
        var doc = document()
        doc.add(rectangle())
        doc.add(rectangle().withColor(.green))
        var onlyFront = document()
        onlyFront.add(rectangle().withColor(.green))
        #expect(bytes(try AnnotationExporter.flatten(image: source(), document: doc)) ==
            bytes(try AnnotationExporter.flatten(image: source(), document: onlyFront)))
    }
    @Test func profilesAndMismatch() throws {
        let p3 = source(space: CGColorSpace(name: CGColorSpace.displayP3)!)
        let result = try AnnotationExporter.flatten(image: p3, document: document())
        #expect(result.colorSpace?.name == CGColorSpace.displayP3)
        let png = try PNGEncoder.data(for: result, pixelsPerPoint: 1)
        let decoded = CGImageSourceCreateWithData(png as CFData, nil)!
        #expect(CGImageSourceCreateImageAtIndex(decoded, 0, nil)?.colorSpace?.name == CGColorSpace.displayP3)
        let gray = CGContext(data: nil, width: 200, height: 100, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue)!.makeImage()!
        #expect(try AnnotationExporter.flatten(image: gray, document: document()).colorSpace?.name == CGColorSpace.sRGB)
        #expect(throws: AnnotationExportError.sizeMismatch) {
            try AnnotationExporter.flatten(image: source(), document: document(width: 20), assertOnSizeMismatch: false)
        }
    }
}
