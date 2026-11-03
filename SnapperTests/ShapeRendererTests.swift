import CoreGraphics
import Foundation
import Testing
@testable import Snapper

struct ShapeRendererTests {
    private func bitmap(_ size: Int = 100) throws -> CGContext {
        let space = try #require(CGColorSpace(name: CGColorSpace.sRGB))
        return try #require(CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: size * 4,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue))
    }
    private func yDown(_ context: CGContext) {
        context.translateBy(x: 0, y: CGFloat(context.height))
        context.scaleBy(x: 1, y: -1)
    }
    /// A CG bitmap buffer starts at the top raster row, independently of its drawing CTM.
    private func pixel(_ context: CGContext, _ x: Int, _ y: Int) throws -> [UInt8] {
        let data = try #require(context.data).assumingMemoryBound(to: UInt8.self)
        let index = y * context.bytesPerRow + x * 4
        return Array(UnsafeBufferPointer(start: data + index, count: 4))
    }
    @Test func imageKeepsAsymmetricTopAndBottomRows() throws {
        let source = try bitmap(4)
        let bytes = try #require(source.data).assumingMemoryBound(to: UInt8.self)
        for y in 0..<4 {
            for x in 0..<4 {
                let i = y * 16 + x * 4
                bytes[i] = y < 2 ? 255 : 0
                bytes[i + 1] = x == 0 ? 100 : 0
                bytes[i + 2] = y < 2 ? 0 : 255
                bytes[i + 3] = 255
            }
        }
        let destination = try bitmap(4)
        yDown(destination)
        ShapeRenderer.drawImage(try #require(source.makeImage()), in: destination)
        for y in 0..<4 { for x in 0..<4 { #expect(try pixel(destination, x, y) == pixel(source, x, y)) } }
    }
    @Test func rectangleIsOutlineNearImageTopAndScalesStroke() throws {
        let shape = AnnotationShape(kind: .rectangle(CGRect(x: 10, y: 10, width: 50, height: 30)), color: RGBAColor(red: 1, green: 0, blue: 0), strokeWidth: .s)
        let thin = try bitmap()
        yDown(thin)
        ShapeRenderer.draw([shape], pixelsPerPoint: 1, in: thin)
        #expect(try pixel(thin, 30, 10) == [255, 0, 0, 255])
        #expect(try pixel(thin, 35, 25)[3] == 0)
        #expect(try pixel(thin, 30, 89)[3] == 0)
        #expect(try pixel(thin, 30, 7)[3] == 0)
        let thick = try bitmap()
        yDown(thick)
        ShapeRenderer.draw(shape.withStrokeWidth(.m), pixelsPerPoint: 2, in: thick)
        #expect(try pixel(thick, 30, 7) == [255, 0, 0, 255])
    }
    @Test func arrowUsesFilledHeadButtStartAndNothingPastTip() throws {
        let context = try bitmap()
        yDown(context)
        let shape = AnnotationShape(kind: .arrow(start: CGPoint(x: 10, y: 50), end: CGPoint(x: 90, y: 50)),
                                    color: RGBAColor(red: 0, green: 0, blue: 1), strokeWidth: .m)
        ShapeRenderer.draw(shape, pixelsPerPoint: 1, in: context)
        #expect(try pixel(context, 30, 50) == [0, 0, 255, 255])
        #expect(try pixel(context, 80, 50) == [0, 0, 255, 255])
        #expect(try pixel(context, 95, 50)[3] == 0)
        #expect(try pixel(context, 8, 50)[3] == 0)
    }
    @Test func textIsSkippedAndGraphicsStateRestored() throws {
        let context = try bitmap()
        yDown(context)
        let before = context.ctm
        ShapeRenderer.draw(AnnotationShape.makeText(origin: .zero, color: .red, strokeWidth: .m), pixelsPerPoint: 2, in: context)
        #expect(context.ctm == before)
        #expect(try pixel(context, 0, 0)[3] == 0)
    }
}
