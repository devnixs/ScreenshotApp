import CoreGraphics
import Foundation
import ImageIO
import Testing
import UniformTypeIdentifiers
@testable import Snapper

struct PNGEncoderTests {
    private func image() throws -> CGImage {
        let space = try #require(CGColorSpace(name: CGColorSpace.sRGB))
        let context = try #require(CGContext(data: nil, width: 200, height: 100, bitsPerComponent: 8,
                                             bytesPerRow: 800, space: space,
                                             bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0, green: 0, blue: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 200, height: 100))
        context.setFillColor(CGColor(red: 1, green: 0, blue: 0, alpha: 1))
        context.fill(CGRect(x: 0, y: 75, width: 50, height: 25))
        return try #require(context.makeImage())
    }

    @Test(arguments: [CGFloat(1), CGFloat(2)])
    func nativePixelsAndDPI(scale: CGFloat) throws {
        let original = try image()
        let data = try PNGEncoder.data(for: original, pixelsPerPoint: scale)
        let source = try #require(CGImageSourceCreateWithData(data as CFData, nil))
        #expect(CGImageSourceGetType(source) == UTType.png.identifier as CFString)
        let decoded = try #require(CGImageSourceCreateImageAtIndex(source, 0, nil))
        #expect(decoded.width == 200)
        #expect(decoded.height == 100)
        let properties = try #require(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
        for key in [kCGImagePropertyDPIWidth, kCGImagePropertyDPIHeight] {
            let dpi = try #require(properties[key] as? Double)
            #expect(abs(dpi - 72 * Double(scale)) < 0.1)
        }
        // Normalize both images to the same RGBA layout to check orientation and pixel colors.
        func pixels(_ image: CGImage) throws -> Data {
            let space = try #require(CGColorSpace(name: CGColorSpace.sRGB))
            let context = try #require(CGContext(data: nil, width: 200, height: 100, bitsPerComponent: 8,
                                                 bytesPerRow: 800, space: space,
                                                 bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: 200, height: 100))
            return Data(bytes: try #require(context.data), count: 800 * 100)
        }
        let originalBytes = try pixels(original)
        let decodedBytes = try pixels(decoded)
        #expect(originalBytes == decodedBytes)
        #expect(Set(decodedBytes).count > 1)
        #expect(CapturedImage(cgImage: decoded, pixelsPerPoint: scale).pointSize
                == CGSize(width: 200 / scale, height: 100 / scale))
    }
}
