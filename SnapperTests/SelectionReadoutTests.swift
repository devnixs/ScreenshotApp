import CoreGraphics
import Testing
@testable import Snapper

struct SelectionReadoutTests {
    @Test func textUsesRoundedPointsAndMultiplicationSign() {
        #expect(SelectionReadout.text(for: CGSize(width: 1440, height: 900)) == "1440 \u{00D7} 900")
        #expect(SelectionReadout.text(for: .zero) == "0 × 0")
        #expect(SelectionReadout.text(for: CGSize(width: 99.5, height: 10.4)) == "100 × 10")
        #expect(SelectionReadout.text(for: CGSize(width: 12345, height: 4)) == "12345 × 4")
    }

    @Test func defaultAndEdgePlacement() {
        let display = CGRect(x: 0, y: 0, width: 1440, height: 900)
        let size = CGSize(width: 60, height: 20)
        for (cursor, expected) in [
            (CGPoint(x: 500, y: 500), CGPoint(x: 512, y: 468)),
            (CGPoint(x: 1430, y: 500), CGPoint(x: 1358, y: 468)),
            (CGPoint(x: 500, y: 10), CGPoint(x: 512, y: 22)),
            (CGPoint(x: 1430, y: 10), CGPoint(x: 1358, y: 22)),
            (CGPoint(x: 0, y: 900), CGPoint(x: 12, y: 868))
        ] {
            #expect(SelectionReadout.origin(labelSize: size, cursor: cursor, display: display) == expected)
        }
    }

    @Test func negativeOriginAndOversizedLabel() {
        let display = CGRect(x: -1920, y: -200, width: 1920, height: 1080)
        #expect(SelectionReadout.origin(labelSize: CGSize(width: 60, height: 20),
                                       cursor: CGPoint(x: -5, y: -195), display: display) == CGPoint(x: -77, y: -183))
        #expect(SelectionReadout.origin(labelSize: CGSize(width: 2000, height: 1200),
                                       cursor: CGPoint(x: -100, y: 100), display: display) == CGPoint(x: -1916, y: -196))
    }

    @Test func labelStaysInsideEachDisplayAcrossCursorGrid() {
        let displays = [
            DisplayInfo(displayID: 1, frame: CGRect(x: 0, y: 0, width: 1440, height: 900), backingScale: 2),
            DisplayInfo(displayID: 2, frame: CGRect(x: 1440, y: 0, width: 1920, height: 1080), backingScale: 1),
            DisplayInfo(displayID: 3, frame: CGRect(x: -1920, y: -200, width: 1920, height: 1080), backingScale: 1)
        ]
        for display in displays {
            for x in stride(from: display.frame.minX, through: display.frame.maxX, by: 60) {
                for y in stride(from: display.frame.minY, through: display.frame.maxY, by: 30) {
                    let size = CGSize(width: 100, height: 20)
                    let origin = SelectionReadout.origin(labelSize: size, cursor: CGPoint(x: x, y: y), display: display.frame)
                    #expect(display.frame.insetBy(dx: 4, dy: 4).contains(CGRect(origin: origin, size: size)))
                }
            }
        }
    }
}
