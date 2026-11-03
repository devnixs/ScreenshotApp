import Foundation

enum RecordingQuality: String, CaseIterable, Sendable {
    case high, medium, low
    var title: String { rawValue.capitalized }
}
