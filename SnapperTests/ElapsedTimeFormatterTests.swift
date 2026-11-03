import Foundation
import Testing
@testable import Snapper

struct ElapsedTimeFormatterTests {
    @Test func truncatesAndFormatsBoundaries() {
        let cases: [(Duration, String)] = [
            (.zero, "0:00"), (.milliseconds(999), "0:00"), (.seconds(59), "0:59"),
            (.seconds(60), "1:00"), (.seconds(61), "1:01"), (.seconds(3599), "59:59"),
            (.seconds(3600), "1:00:00"), (.seconds(36309), "10:05:09"), (.seconds(-5), "0:00"),
            (.milliseconds(-999), "0:00")
        ]
        for (duration, text) in cases { #expect(ElapsedTimeFormatter.string(from: duration) == text) }
    }
}
