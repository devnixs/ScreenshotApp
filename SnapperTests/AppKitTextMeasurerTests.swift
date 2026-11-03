import AppKit
import Testing
@testable import Snapper

struct AppKitTextMeasurerTests {
    @Test func stableLayoutProperties() {
        let measurer = AppKitTextMeasurer()
        let empty = measurer.layoutSize(of: "", fontPixelSize: 24)
        #expect(empty.height == measurer.lineHeight(fontPixelSize: 24) && empty.height > 0)
        let one = measurer.layoutSize(of: "W", fontPixelSize: 24)
        let two = measurer.layoutSize(of: "W\nW", fontPixelSize: 24)
        #expect(two.height > one.height)
        #expect(measurer.layoutSize(of: "W\n", fontPixelSize: 24).height > one.height)
        let wide = measurer.layoutSize(of: "WWWW", fontPixelSize: 24)
        #expect(wide.width > one.width)
        let big = measurer.layoutSize(of: "WWWW", fontPixelSize: 48)
        #expect(abs(big.width / wide.width - 2) < 0.2)
        #expect(abs(big.height / wide.height - 2) < 0.2)
        for size in [empty, one, two, wide, big] { #expect(size.width == ceil(size.width) && size.height == ceil(size.height)) }
    }
}
