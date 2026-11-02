import Foundation

struct FileNamer: Sendable {
    private let timeZone: TimeZone

    init(timeZone: TimeZone = .current) {
        self.timeZone = timeZone
    }

    /// Pass the capture time for screenshots and the recording start time for recordings.
    /// Returns the base name without an extension or collision suffix.
    func baseName(for kind: CaptureKind, date: Date) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        let components = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        let timestamp = String(
            format: "%04d-%02d-%02d at %02d.%02d.%02d",
            locale: Locale(identifier: "en_US_POSIX"),
            components.year ?? 0, components.month ?? 0, components.day ?? 0,
            components.hour ?? 0, components.minute ?? 0, components.second ?? 0
        )
        return "\(kind.namePrefix) \(timestamp)"
    }

    /// Pass the capture time for screenshots and the recording start time for recordings.
    func fileName(for kind: CaptureKind, date: Date) -> String {
        "\(baseName(for: kind, date: date)).\(kind.fileExtension)"
    }

    /// Returns the first unused full filename, using the capture or recording start time.
    func uniqueFileName(for kind: CaptureKind, date: Date, exists: (String) -> Bool) -> String {
        let base = baseName(for: kind, date: date)
        var candidate = "\(base).\(kind.fileExtension)"
        var suffix = 2
        while exists(candidate) {
            candidate = "\(base) (\(suffix)).\(kind.fileExtension)"
            suffix += 1
        }
        return candidate
    }
}
