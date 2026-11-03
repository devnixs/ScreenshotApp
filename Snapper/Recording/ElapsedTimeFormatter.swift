import Foundation

enum ElapsedTimeFormatter {
    static func string(from duration: Duration) -> String {
        let seconds = max(0, duration.components.seconds)
        let minutes = seconds / 60
        if seconds < 3600 { return String(format: "%lld:%02lld", minutes, seconds % 60) }
        return String(format: "%lld:%02lld:%02lld", seconds / 3600, minutes % 60, seconds % 60)
    }
}
