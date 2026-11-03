import CoreGraphics

/// Placement in AppKit global points, independent of display scale.
enum RecordingPillPlacement {
    enum Position: Equatable, Sendable { case below, above, inside }
    struct Result: Equatable, Sendable {
        let frame: CGRect
        let position: Position
    }
    static let defaultMargin: CGFloat = 8

    static func place(pillSize: CGSize, recordedRect: CGRect, visibleFrame: CGRect,
                      margin: CGFloat = defaultMargin) -> Result {
        let below = recordedRect.minY - margin - pillSize.height
        let above = recordedRect.maxY + margin
        let position: Position
        let proposedY: CGFloat
        if below >= visibleFrame.minY {
            position = .below
            proposedY = below
        } else if above + pillSize.height <= visibleFrame.maxY {
            position = .above
            proposedY = above
        } else {
            position = .inside
            proposedY = max(recordedRect.minY, visibleFrame.minY) + margin
        }
        let y = max(visibleFrame.minY, min(proposedY, visibleFrame.maxY - pillSize.height))
        let x = max(visibleFrame.minX, min(recordedRect.midX - pillSize.width / 2,
                                         visibleFrame.maxX - pillSize.width))
        return Result(frame: CGRect(x: x.rounded(), y: y.rounded(), width: pillSize.width,
                                    height: pillSize.height), position: position)
    }
}
