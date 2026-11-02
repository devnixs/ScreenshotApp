import Foundation
import Testing
@testable import Snapper

struct FileNamerTests {
    private let paris = TimeZone(identifier: "Europe/Paris")!
    private let utc = TimeZone(identifier: "UTC")!

    private func date(
        year: Int = 2026, month: Int = 11, day: Int = 2,
        hour: Int = 19, minute: Int = 18, second: Int = 4,
        zone: TimeZone? = nil
    ) -> Date {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone ?? paris
        return calendar.date(from: DateComponents(
            year: year, month: month, day: day, hour: hour, minute: minute, second: second
        ))!
    }

    @Test func screenshotNameFormat() {
        #expect(FileNamer(timeZone: paris).fileName(for: .screenshot, date: date()) ==
                "Screenshot 2026-11-02 at 19.18.04.png")
    }

    @Test func recordingNameFormat() {
        #expect(FileNamer(timeZone: paris).fileName(for: .recording, date: date()) ==
                "Recording 2026-11-02 at 19.18.04.mp4")
    }

    @Test func baseNameHasNoExtension() {
        #expect(FileNamer(timeZone: paris).baseName(for: .screenshot, date: date()) ==
                "Screenshot 2026-11-02 at 19.18.04")
    }

    @Test func zeroPadsSingleDigitComponents() {
        #expect(FileNamer(timeZone: paris).fileName(
            for: .screenshot, date: date(year: 2027, month: 1, day: 5, hour: 3, minute: 4, second: 5)
        ) == "Screenshot 2027-01-05 at 03.04.05.png")
    }

    @Test func midnight() {
        #expect(FileNamer(timeZone: paris).fileName(
            for: .screenshot, date: date(day: 3, hour: 0, minute: 0, second: 0)
        ) == "Screenshot 2026-11-03 at 00.00.00.png")
    }

    @Test func usesTwentyFourHourClock() {
        #expect(FileNamer(timeZone: paris).fileName(
            for: .screenshot, date: date(hour: 23, minute: 59, second: 59)
        ) == "Screenshot 2026-11-02 at 23.59.59.png")
    }

    @Test func respectsInjectedTimeZone() {
        let instant = date(hour: 18, zone: utc)
        #expect(FileNamer(timeZone: paris).fileName(for: .screenshot, date: instant) ==
                "Screenshot 2026-11-02 at 19.18.04.png")
        #expect(FileNamer(timeZone: utc).fileName(for: .screenshot, date: instant) ==
                "Screenshot 2026-11-02 at 18.18.04.png")
        #expect(FileNamer(timeZone: paris).fileName(
            for: .screenshot, date: date(hour: 23, minute: 30, second: 0, zone: utc)
        ) == "Screenshot 2026-11-03 at 00.30.00.png")
    }

    @Test func observesDaylightSavingTime() {
        #expect(FileNamer(timeZone: paris).fileName(
            for: .screenshot, date: date(month: 7, hour: 18, zone: utc)
        ) == "Screenshot 2026-07-02 at 20.18.04.png")
    }

    @Test func dropsSubSecondPrecision() {
        #expect(FileNamer(timeZone: paris).fileName(for: .screenshot, date: date().addingTimeInterval(0.999)) ==
                "Screenshot 2026-11-02 at 19.18.04.png")
    }

    @Test func noCollisionReturnsPlainName() {
        #expect(FileNamer(timeZone: paris).uniqueFileName(for: .screenshot, date: date(), exists: { _ in false }) ==
                "Screenshot 2026-11-02 at 19.18.04.png")
    }

    @Test func collisionSequence() {
        let base = "Screenshot 2026-11-02 at 19.18.04"
        var existing = Set(["\(base).png"])
        for suffix in 2...4 {
            let result = FileNamer(timeZone: paris).uniqueFileName(for: .screenshot, date: date()) {
                existing.contains($0)
            }
            #expect(result == "\(base) (\(suffix)).png")
            existing.insert(result)
        }
    }

    @Test func collisionFillsFirstGap() {
        let base = "Screenshot 2026-11-02 at 19.18.04"
        let existing = Set(["\(base).png", "\(base) (3).png"])
        #expect(FileNamer(timeZone: paris).uniqueFileName(for: .screenshot, date: date()) {
            existing.contains($0)
        } == "\(base) (2).png")
    }

    @Test func collisionPreservesExtension() {
        #expect(FileNamer(timeZone: paris).uniqueFileName(for: .recording, date: date()) {
            $0 == "Recording 2026-11-02 at 19.18.04.mp4"
        } == "Recording 2026-11-02 at 19.18.04 (2).mp4")
    }

    @Test func differentExtensionIsNotACollision() {
        #expect(FileNamer(timeZone: paris).uniqueFileName(for: .screenshot, date: date()) {
            $0 == "Screenshot 2026-11-02 at 19.18.04.mp4"
        } == "Screenshot 2026-11-02 at 19.18.04.png")
    }

    @Test func predicateReceivesFullFileNames() {
        var checked: [String] = []
        _ = FileNamer(timeZone: paris).uniqueFileName(for: .screenshot, date: date()) {
            checked.append($0)
            return checked.count == 1
        }
        #expect(checked == ["Screenshot 2026-11-02 at 19.18.04.png", "Screenshot 2026-11-02 at 19.18.04 (2).png"])
    }
}
