import CoreGraphics

struct GIFEncodingSettings: Equatable, Sendable {
    let framesPerSecond: Int
    let usesPointSize: Bool
    let maxLongestSide: Int?

    static func forQuality(_ quality: RecordingQuality) -> GIFEncodingSettings {
        switch quality {
        case .high: .init(framesPerSecond: 15, usesPointSize: false, maxLongestSide: nil)
        case .medium: .init(framesPerSecond: 10, usesPointSize: true, maxLongestSide: nil)
        case .low: .init(framesPerSecond: 8, usesPointSize: true, maxLongestSide: 640)
        }
    }

    func outputPixelSize(sourceWidth: Int, sourceHeight: Int, pointPixelScale: CGFloat) -> (width: Int, height: Int) {
        var width = Double(max(1, sourceWidth))
        var height = Double(max(1, sourceHeight))
        if usesPointSize && pointPixelScale.isFinite && pointPixelScale > 1 {
            width = (width / Double(pointPixelScale)).rounded()
            height = (height / Double(pointPixelScale)).rounded()
        }
        if let maxLongestSide, max(width, height) > Double(maxLongestSide) {
            let factor = Double(maxLongestSide) / max(width, height)
            width = (width * factor).rounded()
            height = (height * factor).rounded()
        }
        return (min(max(1, Int(width)), max(1, sourceWidth)),
                min(max(1, Int(height)), max(1, sourceHeight)))
    }
}
