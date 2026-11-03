import CoreGraphics
import Foundation

/// A shareable window's plain metadata, in CG-global points (top-left origin).
struct WindowCandidate: Equatable, Sendable {
    let windowID: CGWindowID
    let frame: CGRect
    let layer: Int
    let ownerPID: pid_t?
    let ownerBundleID: String?
    let isOnScreen: Bool
    var alpha: Double
    let title: String?
}

enum WindowCandidateBuilder {
    static func build(windows: [WindowCandidate], frontToBackIDs: [CGWindowID],
                      alphaByID: [CGWindowID: Double]) -> [WindowCandidate] {
        let byID = Dictionary(windows.map { ($0.windowID, $0) }, uniquingKeysWith: { first, _ in first })
        return frontToBackIDs.compactMap { id in
            guard var candidate = byID[id] else { return nil }
            candidate.alpha = alphaByID[id] ?? 1
            return candidate
        }
    }
}

struct WindowHitTester: Sendable {
    let candidates: [WindowCandidate]
    let excludedPID: pid_t

    func isSelectable(_ candidate: WindowCandidate) -> Bool {
        candidate.ownerPID != nil && candidate.ownerPID != excludedPID
            && candidate.layer == 0 && candidate.isOnScreen && candidate.alpha > 0
            && candidate.frame.width >= 4 && candidate.frame.height >= 4
    }

    func window(at point: CGPoint) -> WindowCandidate? {
        candidates.first { isSelectable($0) && $0.frame.contains(point) }
    }
}

enum WindowDisplayRule {
    static func display(for windowFrame: CGRect, displays: [DisplayInfo],
                        primaryDisplayHeight: CGFloat) -> DisplayInfo? {
        let frames = displays.map { DisplayGeometry.cgRect(fromAppKit: $0.frame, primaryHeight: primaryDisplayHeight) }
        var bestIndex: Int?
        var bestArea: CGFloat = 0
        for (index, frame) in frames.enumerated() {
            let intersection = frame.intersection(windowFrame)
            let area = intersection.isNull ? 0 : intersection.width * intersection.height
            if area > bestArea { bestIndex = index; bestArea = area }
        }
        if let bestIndex { return displays[bestIndex] }
        let centre = CGPoint(x: windowFrame.midX, y: windowFrame.midY)
        var bestDistance = CGFloat.infinity
        for (index, frame) in frames.enumerated() {
            let nearest = DisplayGeometry.clamp(centre, to: frame)
            let distance = pow(centre.x - nearest.x, 2) + pow(centre.y - nearest.y, 2)
            if distance < bestDistance { bestIndex = index; bestDistance = distance }
        }
        return bestIndex.map { displays[$0] }
    }
}
