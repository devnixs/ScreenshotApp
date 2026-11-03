import Foundation
import CoreGraphics
import Testing
@testable import Snapper

struct AnnotationShapeTests {
    private func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x, y: y) }
    private func shape(_ kind: AnnotationShape.Kind) -> AnnotationShape {
        AnnotationShape(kind: kind, color: .red, strokeWidth: .m)
    }
    private func near(_ actual: CGFloat, _ expected: CGFloat) { #expect(abs(actual - expected) <= 1e-9) }
    private func near(_ actual: CGPoint, _ expected: CGPoint) {
        near(actual.x, expected.x)
        near(actual.y, expected.y)
    }
    private var rectangle: AnnotationShape { shape(.rectangle(CGRect(x: 100, y: 100, width: 200, height: 100))) }
    private var arrow: AnnotationShape { shape(.arrow(start: .zero, end: point(100, 0))) }
    private var text: AnnotationShape {
        shape(.text(TextBox(origin: point(10, 20), string: "Hello", measuredSize: CGSize(width: 50, height: 30))))
    }

    @Test func unitsAndRecommendedTolerances() {
        for (width, stroke, font) in [(StrokeWidth.s, CGFloat(2), CGFloat(16)), (.m, 4, 24), (.l, 8, 36)] {
            for scale: CGFloat in [1, 2] {
                let s = rectangle.withStrokeWidth(width)
                near(s.strokePixels(pixelsPerPoint: scale), stroke * scale)
                near(s.fontPixelSize(pixelsPerPoint: scale), font * scale)
            }
        }
        #expect(AnnotationShape.hitSlopViewPoints == 4)
        #expect(AnnotationShape.handleRadiusViewPoints == 6)
        #expect(AnnotationShape.minimumDragViewPoints == 3)
    }

    @Test func normalizationAndDirectKindMutation() {
        let expected = CGRect(x: 4, y: 5, width: 6, height: 15)
        #expect(ShapeGeometry.normalizedRect(from: point(10, 20), to: point(4, 5)) == expected)
        var s = shape(.rectangle(CGRect(x: 10, y: 20, width: -6, height: -15)))
        #expect(s.kind == .rectangle(expected))
        s.kind = .rectangle(CGRect(x: 10, y: 20, width: -6, height: -15))
        #expect(s.kind == .rectangle(expected))
    }

    @Test func squareConstraintsEveryQuadrantAndAxis() {
        let anchor = point(100, 100)
        let cases: [(CGPoint, CGRect)] = [
            (point(130, 110), CGRect(x: 100, y: 100, width: 30, height: 30)),
            (point(70, 110), CGRect(x: 70, y: 100, width: 30, height: 30)),
            (point(130, 90), CGRect(x: 100, y: 70, width: 30, height: 30)),
            (point(70, 90), CGRect(x: 70, y: 70, width: 30, height: 30)),
            (point(110, 140), CGRect(x: 100, y: 100, width: 40, height: 40)),
            (point(100, 130), CGRect(x: 100, y: 100, width: 30, height: 30)),
            (point(130, 100), CGRect(x: 100, y: 100, width: 30, height: 30))]
        for (p, expected) in cases {
            #expect(AnnotationShape.rectangleFromDrag(start: anchor, current: p, constrained: true) == expected)
            let resized = shape(.rectangle(CGRect(x: 100, y: 100, width: 100, height: 100)))
                .resized(handle: .bottomRight, to: p, constrained: true)
            #expect(resized.shape.kind == .rectangle(expected))
        }
    }

    @Test func angleSnappingPreservesLength() {
        let cases: [(CGFloat, CGFloat)] = [(0, 0), (22, 0), (23, 45), (44, 45), (46, 45), (90, 90),
                                             (180, 180), (-135, -135), (-22, 0), (-23, -45)]
        for fixed in [CGPoint.zero, point(50, 50)] {
            for (angle, snapped) in cases + [(40, 45)] {
                let radians = angle * .pi / 180, expected = snapped * .pi / 180
                let moving = point(fixed.x + 100 * cos(radians), fixed.y + 100 * sin(radians))
                let actual = ShapeGeometry.snappedEndpoint(fixed: fixed, moving: moving)
                near(actual, point(fixed.x + 100 * cos(expected), fixed.y + 100 * sin(expected)))
                near(hypot(actual.x - fixed.x, actual.y - fixed.y), 100)
            }
            #expect(ShapeGeometry.snappedEndpoint(fixed: fixed, moving: fixed) == fixed)
        }
    }

    @Test func segmentAndOutlineDistances() {
        for (p, distance): (CGPoint, CGFloat) in [(point(50, 6), 6), (point(-6, 0), 6), (point(-3, 4), 5), (point(103, 4), 5)] {
            near(ShapeGeometry.distance(from: p, toSegment: .zero, point(100, 0)), distance)
        }
        near(ShapeGeometry.distance(from: point(8, 9), toSegment: point(5, 5), point(5, 5)), 5)
        let rect = CGRect(x: 10, y: 20, width: 20, height: 30)
        near(ShapeGeometry.distance(from: point(20, 30), toOutlineOf: rect), 10)
        near(ShapeGeometry.distance(from: point(7, 16), toOutlineOf: rect), 5)
        near(ShapeGeometry.distance(from: point(10, 40), toOutlineOf: rect), 0)
    }

    @Test func triangleEdgesWindingAndDegeneracy() {
        for p in [point(0, 0), point(5, 0), point(2, 2), point(5, 5)] {
            #expect(ShapeGeometry.triangleContains(p, .zero, point(10, 0), point(0, 10)))
            #expect(ShapeGeometry.triangleContains(p, .zero, point(0, 10), point(10, 0)))
        }
        #expect(!ShapeGeometry.triangleContains(point(8, 8), .zero, point(10, 0), point(0, 10)))
        #expect(!ShapeGeometry.triangleContains(point(50, 50), .zero, .zero, .zero))
        #expect(ShapeGeometry.triangleContains(.zero, .zero, .zero, .zero))
        #expect(!ShapeGeometry.triangleContains(point(20, 0), .zero, point(5, 0), point(10, 0)))
    }

    @Test func arrowheadSizesScalesAndDirections() {
        for (width, length): (StrokeWidth, CGFloat) in [(.s, 14), (.m, 20), (.l, 32)] {
            for scale: CGFloat in [1, 2] {
                let g = arrow.withStrokeWidth(width).arrowGeometry(pixelsPerPoint: scale)!
                #expect(g.tip == point(100, 0))
                #expect(g.shaftStart == .zero)
                near(g.baseCenter, point(100 - length * scale, 0))
                near(g.baseLeft, point(100 - length * scale, 0.4 * length * scale))
                near(g.baseRight, point(100 - length * scale, -0.4 * length * scale))
                near(g.strokePixels, width.points * scale)
            }
        }
        let short = shape(.arrow(start: .zero, end: point(10, 0))).arrowGeometry(pixelsPerPoint: 1)!
        #expect(short.baseCenter == .zero)
        #expect(short.baseLeft == point(0, 4))
        #expect(short.baseRight == point(0, -4))
        let diagonal = shape(.arrow(start: .zero, end: point(30, 40))).arrowGeometry(pixelsPerPoint: 1)!
        near(diagonal.baseCenter, point(18, 24))
        let reverse = shape(.arrow(start: point(100, 0), end: .zero)).arrowGeometry(pixelsPerPoint: 1)!
        near(reverse.baseCenter, point(20, 0))
        #expect(rectangle.arrowGeometry(pixelsPerPoint: 1) == nil)
        #expect(text.arrowGeometry(pixelsPerPoint: 1) == nil)
    }

    @Test func paintedBoundsIncludeStrokeAndHead() {
        #expect(rectangle.bounds(pixelsPerPoint: 1) == CGRect(x: 98, y: 98, width: 204, height: 104))
        #expect(rectangle.bounds(pixelsPerPoint: 2) == CGRect(x: 96, y: 96, width: 208, height: 108))
        #expect(arrow.bounds(pixelsPerPoint: 1) == CGRect(x: 0, y: -8, width: 100, height: 16))
        #expect(arrow.bounds(pixelsPerPoint: 2) == CGRect(x: 0, y: -16, width: 100, height: 32))
        #expect(text.bounds(pixelsPerPoint: 1) == CGRect(x: 10, y: 20, width: 50, height: 30))
        #expect(text.bounds(pixelsPerPoint: 2) == text.bounds(pixelsPerPoint: 1))
        let diagonal = shape(.arrow(start: .zero, end: point(30, 40))).bounds(pixelsPerPoint: 1)
        near(diagonal.minX, -1.6)
        near(diagonal.minY, -1.2)
        near(diagonal.maxX, 30)
        near(diagonal.maxY, 40)
        let short = shape(.arrow(start: .zero, end: point(1, 0))).bounds(pixelsPerPoint: 1)
        near(short.minY, -0.4) // No painted shaft for an arrow composed entirely of its head.
    }

    @Test func rectangleOutlineHitsAtBothScales() {
        for (p, hit) in [(point(150, 106), true), (point(150, 107), false), (point(94, 150), true),
                         (point(93, 150), false), (point(150, 94), true), (point(200, 150), false)] {
            #expect(rectangle.hitTest(p, pixelsPerPoint: 1, slop: 4) == hit)
        }
        #expect(rectangle.hitTest(point(150, 112), pixelsPerPoint: 2, slop: 8))
        #expect(!rectangle.hitTest(point(150, 113), pixelsPerPoint: 2, slop: 8))
    }

    @Test func arrowStrokeAndFilledHeadHits() {
        for (p, hit) in [(point(50, 6), true), (point(50, 7), false), (point(-6, 0), true),
                         (point(-7, 0), false), (point(-5, -4), false), (point(106, 0), true),
                         (point(82, 7), true), (point(85, 7), false)] {
            #expect(arrow.hitTest(p, pixelsPerPoint: 1, slop: 4) == hit)
        }
        #expect(arrow.hitTest(point(50, 12), pixelsPerPoint: 2, slop: 8))
        #expect(!arrow.hitTest(point(50, 13), pixelsPerPoint: 2, slop: 8))
    }

    @Test func authoritativeTextBoundsAndInclusiveHits() {
        for (p, hit) in [(point(10, 20), true), (point(60, 50), true), (point(35, 35), true),
                         (point(9, 35), false), (point(61, 35), false), (point(35, 51), false)] {
            #expect(text.hitTest(p, pixelsPerPoint: 2, slop: 100) == hit)
        }
        let empty = text.withMeasuredSize(.zero)
        #expect(empty.hitTest(point(10, 20), pixelsPerPoint: 1, slop: 100))
        #expect(!empty.hitTest(point(35, 35), pixelsPerPoint: 1, slop: 100))
        #expect(empty.bounds(pixelsPerPoint: 1) == CGRect(x: 10, y: 20, width: 0, height: 0))
    }

    @Test func handleOrderOppositesAndNearestHit() {
        #expect(rectangle.handles.map { $0.0 } == [.topLeft, .topRight, .bottomLeft, .bottomRight])
        #expect(rectangle.handles.map { $0.1 } == [point(100, 100), point(300, 100), point(100, 200), point(300, 200)])
        #expect(arrow.handles.map { $0.0 } == [.start, .end])
        #expect(arrow.handles.map { $0.1 } == [.zero, point(100, 0)])
        #expect(text.handles.isEmpty)
        for handle in ShapeHandle.allCases { #expect(handle.opposite.opposite == handle) }
        #expect(ShapeHandle.topLeft.opposite == .bottomRight)
        #expect(ShapeHandle.topRight.opposite == .bottomLeft)
        #expect(ShapeHandle.start.opposite == .end)
        #expect(rectangle.handleHitTest(point(304, 203), radius: 6) == .bottomRight)
        #expect(rectangle.handleHitTest(point(306, 200), radius: 6) == .bottomRight)
        #expect(rectangle.handleHitTest(point(310, 200), radius: 6) == nil)
        let tiny = shape(.rectangle(CGRect(x: 0, y: 0, width: 4, height: 4)))
        #expect(tiny.handleHitTest(point(3, 3), radius: 6) == .bottomRight)
        #expect(tiny.handleHitTest(point(2, 2), radius: 6) == .topLeft)
        #expect(text.handleHitTest(point(10, 20), radius: 100) == nil)
    }

    @Test func movementPreservesMetadataAndDoesNotClamp() {
        for original in [rectangle, arrow, text] {
            let moved = original.moved(by: CGVector(dx: 10, dy: -5))
            #expect(moved.id == original.id)
            #expect(moved.color == original.color)
            #expect(moved.strokeWidth == original.strokeWidth)
            #expect(moved.bounds(pixelsPerPoint: 1) == original.bounds(pixelsPerPoint: 1).offsetBy(dx: 10, dy: -5))
            #expect(moved.moved(by: CGVector(dx: -10, dy: 5)) == original)
        }
        #expect(arrow.moved(by: CGVector(dx: -100, dy: -100)).kind == .arrow(start: point(-100, -100), end: point(0, -100)))
    }

    @Test func rectangleResizeFlipAndContinuedAnchor() {
        let original = shape(.rectangle(CGRect(x: 100, y: 100, width: 100, height: 100)))
        let cases: [(ShapeHandle, CGPoint, CGRect, ShapeHandle)] = [
            (.topRight, point(250, 50), CGRect(x: 100, y: 50, width: 150, height: 150), .topRight),
            (.bottomRight, point(50, 60), CGRect(x: 50, y: 60, width: 50, height: 40), .topLeft),
            (.bottomRight, point(50, 300), CGRect(x: 50, y: 100, width: 50, height: 200), .bottomLeft),
            (.bottomRight, point(100, 150), CGRect(x: 100, y: 100, width: 0, height: 50), .bottomRight),
            (.bottomRight, point(150, 50), CGRect(x: 100, y: 50, width: 50, height: 50), .topRight)]
        for (handle, p, expected, next) in cases {
            let result = original.resized(handle: handle, to: p, constrained: false)
            #expect(result.shape.kind == .rectangle(expected))
            #expect(result.handle == next)
            #expect(result.shape.id == original.id)
            #expect(result.shape.color == original.color)
            #expect(result.shape.strokeWidth == original.strokeWidth)
        }
        var result = original.resized(handle: .bottomRight, to: point(50, 60), constrained: false)
        result = result.shape.resized(handle: result.handle, to: point(150, 250), constrained: false)
        #expect(result.shape.kind == .rectangle(CGRect(x: 100, y: 100, width: 50, height: 150)))
        #expect(result.handle == .bottomRight)
        result = result.shape.resized(handle: result.handle, to: point(100, 100), constrained: false)
        result = result.shape.resized(handle: result.handle, to: point(40, 80), constrained: true)
        #expect(result.shape.kind == .rectangle(CGRect(x: 40, y: 40, width: 60, height: 60)))
        #expect(result.handle == .topLeft)
        let square = original.resized(handle: .bottomRight, to: point(260, 180), constrained: true)
        #expect(square.shape.kind == .rectangle(CGRect(x: 100, y: 100, width: 160, height: 160)))
    }

    @Test func arrowEndpointResizeAndInvalidHandles() {
        let arrow = self.arrow
        let text = self.text
        let end = arrow.resized(handle: .end, to: point(50, 60), constrained: false)
        #expect(end.shape.kind == .arrow(start: .zero, end: point(50, 60)))
        #expect(end.handle == .end)
        let start = arrow.resized(handle: .start, to: point(30, 65), constrained: true)
        guard case .arrow(let p, let fixed) = start.shape.kind else { Issue.record("Expected arrow"); return }
        let component = hypot(CGFloat(70), CGFloat(65)) / sqrt(2)
        near(p, point(100 - component, component))
        #expect(fixed == point(100, 0))
        #expect(start.handle == .start)
        #expect(start.shape.id == arrow.id)
        #expect(start.shape.color == arrow.color)
        #expect(start.shape.strokeWidth == arrow.strokeWidth)
        for (s, handle) in [(arrow, ShapeHandle.topLeft), (rectangle, .start)] {
            let result = s.resized(handle: handle, to: .zero, constrained: true)
            #expect(result.shape == s)
            #expect(result.handle == handle)
        }
        for handle in ShapeHandle.allCases { #expect(text.resized(handle: handle, to: .zero, constrained: false).shape == text) }
    }

    @Test func creationRejectsSmallDragsAfterConstraints() {
        func rect(_ p: CGPoint, constrained: Bool = false, minimum: CGFloat = 3) -> AnnotationShape? {
            .makeRectangle(dragFrom: point(10, 10), to: p, constrained: constrained,
                           color: .red, strokeWidth: .m, minimumDrag: minimum)
        }
        #expect(rect(point(10, 10)) == nil)
        #expect(rect(point(12, 40)) == nil)
        #expect(rect(point(40, 12)) == nil)
        #expect(rect(point(40, 40))?.kind == .rectangle(CGRect(x: 10, y: 10, width: 30, height: 30)))
        #expect(rect(point(40, 12), constrained: true)?.kind == .rectangle(CGRect(x: 10, y: 10, width: 30, height: 30)))
        #expect(rect(point(13, 13)) != nil)
        #expect(rect(point(15, 15), minimum: 6) == nil) // Caller converts 3 view points to pixels.
        func makeArrow(_ p: CGPoint, constrained: Bool = false) -> AnnotationShape? {
            .makeArrow(dragFrom: .zero, to: p, constrained: constrained, color: .red, strokeWidth: .m, minimumDrag: 3)
        }
        #expect(makeArrow(.zero) == nil)
        #expect(makeArrow(point(2, 0)) == nil)
        #expect(makeArrow(point(3, 0)) != nil)
        #expect(makeArrow(point(30, 40))?.kind == .arrow(start: .zero, end: point(30, 40)))
        let constrained = makeArrow(point(100, 10), constrained: true)!
        guard case .arrow(let start, let end) = constrained.kind else { Issue.record("Expected arrow"); return }
        #expect(start == .zero)
        near(end, point(hypot(CGFloat(100), CGFloat(10)), 0))
        let blank = AnnotationShape.makeText(origin: point(10, 20), color: .blue, strokeWidth: .l)
        #expect(blank.kind == .text(TextBox(origin: point(10, 20), string: "", measuredSize: .zero)))
        #expect(blank.color == .blue)
        #expect(blank.strokeWidth == .l)
    }

    @Test func copyMutatorsAndStableIdentity() {
        let text = self.text
        for original in [rectangle, arrow, text] {
            let colored = original.withColor(.blue)
            #expect(colored.id == original.id)
            #expect(colored.kind == original.kind)
            #expect(colored.color == .blue)
            #expect(colored.strokeWidth == original.strokeWidth)
            let widened = original.withStrokeWidth(.l)
            #expect(widened.id == original.id)
            #expect(widened.kind == original.kind)
            #expect(widened.color == original.color)
            #expect(widened.strokeWidth == .l)
        }
        for original in [rectangle, arrow] {
            #expect(original.withText("ignored") == original)
            #expect(original.withMeasuredSize(.zero) == original)
        }
        let edited = text.withText("Updated\ntext").withMeasuredSize(CGSize(width: 90, height: 80))
        #expect(edited.id == text.id)
        #expect(edited.color == text.color)
        #expect(edited.strokeWidth == text.strokeWidth)
        #expect(edited.kind == .text(TextBox(origin: point(10, 20), string: "Updated\ntext", measuredSize: CGSize(width: 90, height: 80))))
        let first = rectangle, second = rectangle
        #expect(first.id != second.id)
    }

    @Test func zeroSizeGeometryRemainsFiniteAndLocal() {
        for scale: CGFloat in [1, 2] {
            let zero = shape(.arrow(start: point(-5, 7), end: point(-5, 7)))
            let g = zero.arrowGeometry(pixelsPerPoint: scale)!
            for p in [g.tip, g.baseCenter, g.baseLeft, g.baseRight, g.shaftStart] {
                #expect(p == point(-5, 7))
                #expect(p.x.isFinite && p.y.isFinite)
            }
            #expect(zero.bounds(pixelsPerPoint: scale) == CGRect(x: -5, y: 7, width: 0, height: 0))
            #expect(zero.hitTest(point(-5, 7), pixelsPerPoint: scale, slop: 0))
            #expect(!zero.hitTest(point(500, 500), pixelsPerPoint: scale, slop: 4))
            #expect(zero.handles.count == 2)
            let rect = shape(.rectangle(CGRect(x: -5, y: 7, width: 0, height: 0)))
            #expect(rect.bounds(pixelsPerPoint: scale).width == 4 * scale)
            #expect(rect.hitTest(point(-5, 7), pixelsPerPoint: scale, slop: 0))
            #expect(rect.handles.count == 4)
        }
    }
}
