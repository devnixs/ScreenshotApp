import CoreGraphics
import Testing
@testable import Snapper

struct DisplayGeometryTests {
    private let single1x = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1920, height: 1080), backingScale: 1)
    private let single2x = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), backingScale: 2)
    private let primary = DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1440, height: 900), backingScale: 2)
    private let right = DisplayInfo(displayID: 2, frame: CGRect(x: 1440, y: 0, width: 1920, height: 1080), backingScale: 1)
    private let left = DisplayInfo(displayID: 3, frame: CGRect(x: -1920, y: 0, width: 1920, height: 1080), backingScale: 1)
    private let above = DisplayInfo(displayID: 4, frame: CGRect(x: -240, y: 900, width: 1920, height: 1080), backingScale: 1)
    private let below = DisplayInfo(displayID: 5, frame: CGRect(x: 0, y: -1080, width: 1920, height: 1080), backingScale: 1)
    private let external = DisplayInfo(displayID: 6, frame: CGRect(x: 1512, y: 0, width: 2560, height: 1440), backingScale: 1)

    private var layouts: [[DisplayInfo]] {
        [[single1x], [single2x], [primary, right], [primary, left],
         [primary, above], [primary, below], [single2x, external]]
    }

    @Test func primaryDetection() {
        #expect(DisplayGeometry.primaryDisplay(in: [right, primary]) == primary)
        #expect(DisplayGeometry.primaryHeight(in: [primary, right]) == 900)
        #expect(DisplayGeometry.primaryDisplay(in: []) == nil)
        #expect(DisplayGeometry.primaryHeight(in: []) == nil)
        #expect(DisplayGeometry.primaryDisplay(in: [above, right]) == above)
        #expect(DisplayGeometry.primaryHeight(in: [above, right]) == 1080)
    }

    @Test func pointConversions() {
        for (appKit, cg) in [(CGPoint(x: 100, y: 200), CGPoint(x: 100, y: 700)),
                             (CGPoint(x: -500, y: -300), CGPoint(x: -500, y: 1200))] {
            #expect(DisplayGeometry.cgPoint(fromAppKit: appKit, primaryHeight: 900) == cg)
            #expect(DisplayGeometry.appKitPoint(fromCG: cg, primaryHeight: 900) == appKit)
        }
    }

    @Test func singleDisplayRectConversion() {
        #expect(DisplayGeometry.cgRect(fromAppKit: CGRect(x: 100, y: 200, width: 300, height: 400), primaryHeight: 1080)
                == CGRect(x: 100, y: 480, width: 300, height: 400))
    }

    @Test func multiDisplayRectConversions() {
        for (display, expected) in [
            (below, CGRect(x: 0, y: 900, width: 1920, height: 1080)),
            (above, CGRect(x: -240, y: -1080, width: 1920, height: 1080)),
            (left, CGRect(x: -1920, y: -180, width: 1920, height: 1080)),
            (right, CGRect(x: 1440, y: -180, width: 1920, height: 1080))
        ] {
            #expect(DisplayGeometry.cgRect(fromAppKit: display.frame, primaryHeight: 900) == expected)
            #expect(DisplayGeometry.appKitRect(fromCG: expected, primaryHeight: 900) == display.frame)
        }
    }

    @Test func roundTripAcrossEveryLayout() throws {
        for layout in layouts {
            let height = try #require(DisplayGeometry.primaryHeight(in: layout))
            let rects = layout.map(\.frame) + [CGRect(x: 5, y: 5, width: 0, height: 0),
                                               CGRect(x: -500, y: -300, width: 100, height: 50)]
            for rect in rects {
                let cg = DisplayGeometry.cgRect(fromAppKit: rect, primaryHeight: height)
                #expect(DisplayGeometry.appKitRect(fromCG: cg, primaryHeight: height) == rect)
                for point in [rect.origin, CGPoint(x: rect.maxX, y: rect.maxY)] {
                    let cg = DisplayGeometry.cgPoint(fromAppKit: point, primaryHeight: height)
                    #expect(DisplayGeometry.appKitPoint(fromCG: cg, primaryHeight: height) == point)
                }
            }
        }
    }

    @Test func negativeSizeConversion() {
        let negative = CGRect(x: 400, y: 600, width: -300, height: -400)
        let standard = CGRect(x: 100, y: 200, width: 300, height: 400)
        #expect(DisplayGeometry.cgRect(fromAppKit: negative, primaryHeight: 900)
                == DisplayGeometry.cgRect(fromAppKit: standard, primaryHeight: 900))
        #expect(DisplayGeometry.appKitRect(fromCG: negative, primaryHeight: 900)
                == DisplayGeometry.appKitRect(fromCG: standard, primaryHeight: 900))
    }

    @Test func singleDisplayLookup() {
        for point in [CGPoint(x: 10, y: 10), CGPoint(x: 1920, y: 500), CGPoint(x: 100, y: 1080)] {
            #expect(DisplayGeometry.display(containing: point, in: [single1x]) == single1x)
        }
        #expect(DisplayGeometry.display(containing: CGPoint(x: -1, y: 0), in: [single1x]) == nil)
        #expect(DisplayGeometry.display(containing: .zero, in: []) == nil)
    }

    @Test func rightDisplayLookup() {
        for point in [CGPoint(x: 1440, y: 500), CGPoint(x: 1440, y: 0),
                      CGPoint(x: 1500, y: 950), CGPoint(x: 3360, y: 500)] {
            #expect(DisplayGeometry.display(containing: point, in: [primary, right]) == right)
        }
        #expect(DisplayGeometry.display(containing: CGPoint(x: 1439.5, y: 500), in: [primary, right]) == primary)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 1000, y: 950), in: [primary, right]) == nil)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 5000, y: 5000), in: [primary, right]) == nil)
    }

    @Test func leftDisplayLookup() {
        #expect(DisplayGeometry.display(containing: CGPoint(x: -1, y: 10), in: [primary, left]) == left)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 0, y: 10), in: [left, primary]) == primary)
        #expect(DisplayGeometry.display(containing: CGPoint(x: -1920, y: 0), in: [primary, left]) == left)
    }

    @Test func aboveDisplayLookup() {
        #expect(DisplayGeometry.display(containing: CGPoint(x: 100, y: 900), in: [primary, above]) == above)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 100, y: 899.5), in: [primary, above]) == primary)
        #expect(DisplayGeometry.display(containing: CGPoint(x: -240, y: 1500), in: [primary, above]) == above)
    }

    @Test func belowDisplayLookup() {
        #expect(DisplayGeometry.display(containing: CGPoint(x: 100, y: 0), in: [below, primary]) == primary)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 100, y: -0.5), in: [primary, below]) == below)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 100, y: -1080), in: [primary, below]) == below)
    }

    @Test func sharedCornerAndClosedFallbackOrder() {
        let corner = DisplayInfo(displayID: 7, frame: CGRect(x: 1440, y: 900, width: 100, height: 100), backingScale: 1)
        let shortRight = DisplayInfo(displayID: 8, frame: CGRect(x: 1440, y: 0, width: 1920, height: 900), backingScale: 1)
        #expect(DisplayGeometry.display(containing: CGPoint(x: 1440, y: 900), in: [primary, shortRight, corner]) == corner)
        // At y=1080 neither side owns this point half-open, so input order breaks the tie.
        let tallPrimary = DisplayInfo(displayID: 8, frame: CGRect(x: 0, y: 0, width: 1440, height: 1080), backingScale: 2)
        let edge = CGPoint(x: 1440, y: 1080)
        #expect(DisplayGeometry.display(containing: edge, in: [tallPrimary, right]) == tallPrimary)
        #expect(DisplayGeometry.display(containing: edge, in: [right, tallPrimary]) == right)
    }

    @Test func normalizedRect() {
        for (start, end) in [(CGPoint(x: 10, y: 20), CGPoint(x: 110, y: 70)),
                             (CGPoint(x: 110, y: 70), CGPoint(x: 10, y: 20)),
                             (CGPoint(x: 110, y: 20), CGPoint(x: 10, y: 70))] {
            #expect(DisplayGeometry.normalizedRect(from: start, to: end) == CGRect(x: 10, y: 20, width: 100, height: 50))
        }
        #expect(DisplayGeometry.normalizedRect(from: CGPoint(x: 5, y: 5), to: CGPoint(x: 5, y: 5))
                == CGRect(x: 5, y: 5, width: 0, height: 0))
    }

    @Test func clampPoint() {
        #expect(DisplayGeometry.clamp(CGPoint(x: 2000, y: 950), to: primary.frame) == CGPoint(x: 1440, y: 900))
        #expect(DisplayGeometry.clamp(CGPoint(x: -5, y: -5), to: primary.frame) == .zero)
        #expect(DisplayGeometry.clamp(CGPoint(x: 100, y: 100), to: primary.frame) == CGPoint(x: 100, y: 100))
    }

    @Test func clampRect() {
        for (input, expected) in [
            (CGRect(x: 1400, y: 850, width: 200, height: 200), CGRect(x: 1400, y: 850, width: 40, height: 50)),
            (CGRect(x: 2000, y: 2000, width: 10, height: 10), CGRect(x: 1440, y: 900, width: 0, height: 0)),
            (CGRect(x: 1440, y: 0, width: 10, height: 10), CGRect(x: 1440, y: 0, width: 0, height: 10)),
            (CGRect(x: 100, y: 100, width: 0, height: 0), CGRect(x: 100, y: 100, width: 0, height: 0)),
            (CGRect(x: 1600, y: 1050, width: -200, height: -200), CGRect(x: 1400, y: 850, width: 40, height: 50)),
            (CGRect(x: -20, y: -20, width: 10, height: 10), CGRect.zero)
        ] {
            let result = DisplayGeometry.clamp(input, to: primary.frame)
            #expect(result == expected)
            #expect(!result.isNull && !result.isInfinite)
        }
    }

    @Test func dragToRightDisplay() {
        #expect(DisplayGeometry.dragRect(from: CGPoint(x: 1000, y: 500), to: CGPoint(x: 2000, y: 950), on: primary)
                == CGRect(x: 1000, y: 500, width: 440, height: 400))
    }

    @Test func dragFromLeftDisplay() {
        #expect(DisplayGeometry.dragRect(from: CGPoint(x: -100, y: 500), to: CGPoint(x: 300, y: -50), on: left)
                == CGRect(x: -100, y: 0, width: 100, height: 500))
    }

    @Test func dragFromBelowDisplay() {
        #expect(DisplayGeometry.dragRect(from: CGPoint(x: 100, y: -100), to: CGPoint(x: 200, y: 300), on: below)
                == CGRect(x: 100, y: -100, width: 100, height: 100))
    }

    @Test func zeroSizeDragAndClampedStart() {
        let point = CGPoint(x: 100, y: 100)
        #expect(DisplayGeometry.dragRect(from: point, to: point, on: primary) == CGRect(origin: point, size: .zero))
        #expect(DisplayGeometry.dragRect(from: CGPoint(x: -5, y: -5), to: point, on: primary)
                == CGRect(x: 0, y: 0, width: 100, height: 100))
    }

    @Test func displayLocalTopLeftCoordinates() {
        for (display, input, expected) in [
            (single2x, CGRect(x: 100, y: 100, width: 200, height: 50), CGRect(x: 100, y: 832, width: 200, height: 50)),
            (right, CGRect(x: 1500, y: 1000, width: 100, height: 50), CGRect(x: 60, y: 30, width: 100, height: 50)),
            (left, CGRect(x: -1900, y: 0, width: 100, height: 100), CGRect(x: 20, y: 980, width: 100, height: 100)),
            (below, CGRect(x: 10, y: -1070, width: 100, height: 100), CGRect(x: 10, y: 970, width: 100, height: 100)),
            (above, CGRect(x: -240, y: 1880, width: 100, height: 100), CGRect(x: 0, y: 0, width: 100, height: 100))
        ] {
            #expect(DisplayGeometry.displayLocalTopLeftRect(input, on: display) == expected)
        }
        for display in layouts.flatMap({ $0 }) {
            #expect(DisplayGeometry.displayLocalTopLeftRect(display.frame, on: display)
                    == CGRect(origin: .zero, size: display.frame.size))
        }
        // Conversion alone must not clamp an out-of-bounds selection.
        #expect(DisplayGeometry.displayLocalTopLeftRect(CGRect(x: -10, y: 0, width: 20, height: 20), on: single2x).minX == -10)
    }

    @Test func pixelAlignment2x() {
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 10.25, y: 20.75, width: 100.5, height: 50), on: single2x)
                == CGRect(x: 10, y: 20.5, width: 101, height: 50.5))
        for rect in [CGRect(x: 10, y: 20, width: 100, height: 50), CGRect(x: 10.5, y: 20.5, width: 1, height: 1)] {
            #expect(DisplayGeometry.pixelAligned(rect, on: single2x) == rect)
        }
    }

    @Test func pixelAlignment1x() {
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 10.25, y: 20.75, width: 100.5, height: 50), on: single1x)
                == CGRect(x: 10, y: 20, width: 101, height: 51))
    }

    @Test func pixelAlignmentNegativeOrigin() {
        #expect(DisplayGeometry.pixelAligned(CGRect(x: -1900.4, y: 10.6, width: 50, height: 50), on: left)
                == CGRect(x: -1901, y: 10, width: 51, height: 51))
    }

    @Test func pixelAlignmentFloatNoise() {
        for delta: CGFloat in [-0.0000000001, 0.0000000001] {
            #expect(DisplayGeometry.pixelAligned(CGRect(x: 10 + delta, y: 20, width: 100, height: 50), on: single2x)
                    == CGRect(x: 10, y: 20, width: 100, height: 50))
        }
    }

    @Test func pixelAlignmentZeroDimensions() {
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 10.25, y: 10.25, width: 0, height: 0), on: single2x)
                == CGRect(x: 10.5, y: 10.5, width: 0, height: 0))
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 10.25, y: 10.25, width: 0.0000001, height: 0.0000001), on: single2x)
                == CGRect(x: 10.5, y: 10.5, width: 0, height: 0))
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 10.25, y: 10.25, width: 0, height: 1), on: single2x)
                == CGRect(x: 10.5, y: 10, width: 0, height: 1.5))
    }

    @Test func pixelAlignmentClampsToDisplay() {
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 1439.8, y: 899.8, width: 0.2, height: 0.2), on: primary)
                == CGRect(x: 1439.5, y: 899.5, width: 0.5, height: 0.5))
        #expect(DisplayGeometry.pixelAligned(CGRect(x: 2000.25, y: 2000.25, width: 0, height: 0), on: primary)
                == CGRect(x: 1440, y: 900, width: 0, height: 0))
    }

    @Test func pixelSize() {
        let rect = CGRect(x: 0, y: 0, width: 100, height: 50)
        #expect(DisplayGeometry.pixelSize(of: rect, scale: 2) == PixelSize(width: 200, height: 100))
        #expect(DisplayGeometry.pixelSize(of: rect, scale: 1) == PixelSize(width: 100, height: 50))
        #expect(DisplayGeometry.pixelSize(of: CGRect(x: 0, y: 0, width: 100.5, height: 50.5), scale: 2)
                == PixelSize(width: 201, height: 101))
        #expect(DisplayGeometry.pixelSize(of: .zero, scale: 2) == PixelSize(width: 0, height: 0))
    }

    @Test func captureRegionUsesEachDisplayScale() {
        let retinaRect = CGRect(x: 100, y: 100, width: 200, height: 100)
        let externalRect = CGRect(x: 1612, y: 100, width: 200, height: 100)
        #expect(DisplayGeometry.captureRegion(for: retinaRect, on: single2x)
                == CaptureRegion(displayID: 1, appKitRect: retinaRect,
                                 sourceRect: CGRect(x: 100, y: 782, width: 200, height: 100),
                                 pixelSize: PixelSize(width: 400, height: 200)))
        #expect(DisplayGeometry.captureRegion(for: externalRect, on: external)
                == CaptureRegion(displayID: 6, appKitRect: externalRect,
                                 sourceRect: CGRect(x: 100, y: 1240, width: 200, height: 100),
                                 pixelSize: PixelSize(width: 200, height: 100)))
    }

    @Test func captureEntireDisplay() {
        for display in layouts.flatMap({ $0 }) {
            let region = DisplayGeometry.captureRegion(for: display.frame, on: display)
            #expect(region.displayID == display.displayID)
            #expect(region.appKitRect == display.frame)
            #expect(region.sourceRect == CGRect(origin: .zero, size: display.frame.size))
            #expect(region.pixelSize == PixelSize(width: Int(display.frame.width * display.backingScale),
                                                  height: Int(display.frame.height * display.backingScale)))
        }
    }

    @Test func captureClampsAlignsAndStandardizes() {
        let clamped = DisplayGeometry.captureRegion(for: CGRect(x: 1400, y: 850, width: 200, height: 200), on: primary)
        #expect(clamped.appKitRect == CGRect(x: 1400, y: 850, width: 40, height: 50))
        #expect(clamped.sourceRect == CGRect(x: 1400, y: 0, width: 40, height: 50))
        #expect(clamped.pixelSize == PixelSize(width: 80, height: 100))
        let region = DisplayGeometry.captureRegion(for: CGRect(x: 1600, y: 1000, width: -200.25, height: -200.25), on: primary)
        #expect(region.appKitRect == CGRect(x: 1399.5, y: 799.5, width: 40.5, height: 100.5))
        #expect(region.sourceRect == CGRect(x: 1399.5, y: 0, width: 40.5, height: 100.5))
        #expect(region.pixelSize == PixelSize(width: 81, height: 201))
        let empty = DisplayGeometry.captureRegion(for: CGRect(x: 2000, y: 2000, width: 1, height: 1), on: primary)
        #expect(empty.appKitRect == CGRect(x: 1440, y: 900, width: 0, height: 0))
        #expect(empty.pixelSize == PixelSize(width: 0, height: 0))
    }

    @Test func fractionalOriginAndNonstandardPositiveScale() {
        let display = DisplayInfo(displayID: 9, frame: CGRect(x: -100.25, y: 20.25, width: 100, height: 80), backingScale: 1.5)
        let region = DisplayGeometry.captureRegion(for: CGRect(x: -90, y: 30.5, width: 10, height: 10), on: display)
        #expect(abs(region.sourceRect.minX - 10) < 1e-10)
        #expect(abs(region.appKitRect.minY - (20.25 + 10)) < 1e-10)
        #expect(region.pixelSize == PixelSize(width: 16, height: 16))
        #expect(abs(region.sourceRect.width * 1.5 - 16) < 1e-10)
        #expect(abs(region.sourceRect.height * 1.5 - 16) < 1e-10)
    }

    @Test func displayInfoHashing() {
        let copy = DisplayInfo(displayID: primary.displayID, frame: primary.frame, backingScale: primary.backingScale)
        #expect(Set([primary, copy, right]).count == 2)
    }
}
