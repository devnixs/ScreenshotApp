import AppKit

@MainActor
private final class RecordingBorderView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(rect: RecordingBorderGeometry.strokeRect(windowSize: bounds.size))
        NSColor.black.withAlphaComponent(0.35).setStroke()
        path.lineWidth = 2
        path.stroke()
        NSColor.white.setStroke()
        path.lineWidth = 1
        path.setLineDash([4, 4], count: 2, phase: 0)
        path.lineCapStyle = .butt
        path.stroke()
    }
}

@MainActor
final class RecordingBorderWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    init() {
        super.init(contentRect: .zero, styleMask: .borderless, backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        ignoresMouseEvents = true
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        isReleasedWhenClosed = false
        let border = RecordingBorderView()
        border.autoresizingMask = [.width, .height]
        border.postsFrameChangedNotifications = false
        contentView = border
    }

    func update(frame: CGRect) {
        setFrame(frame, display: true)
        contentView?.needsDisplay = true
    }
}
