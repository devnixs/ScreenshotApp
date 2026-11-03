import AppKit

@MainActor
final class SelectionOverlayView: NSView {
    weak var controller: SelectionOverlayController?
    var mode: SelectionMode = .rectangle { didSet { if mode != oldValue { needsDisplay = true } } }
    var windowHighlight: CGRect? { didSet { if windowHighlight != oldValue { needsDisplay = true } } }
    var selectionRect: CGRect? { didSet { needsDisplay = true } }
    var readout: (text: String, origin: CGPoint)? { didSet { needsDisplay = true } }
    private var cursorTrackingArea: NSTrackingArea?
    private static let horizontalPadding: CGFloat = 6
    private static let verticalPadding: CGFloat = 3
    private static var textAttributes: [NSAttributedString.Key: Any] {
        [.font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .regular),
         .foregroundColor: NSColor.white]
    }

    static func labelSize(for text: String) -> CGSize {
        let size = NSAttributedString(string: text, attributes: textAttributes).size()
        return CGSize(width: size.width + horizontalPadding * 2, height: size.height + verticalPadding * 2)
    }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        guard let window else { return }
        if mode == .window {
            NSColor.clear.setFill()
            bounds.fill(using: .copy)
            if let windowHighlight {
                let local = windowHighlight.offsetBy(dx: -window.frame.minX, dy: -window.frame.minY).intersection(bounds)
                if !local.isNull && !local.isEmpty {
                    NSColor.controlAccentColor.withAlphaComponent(0.25).setFill()
                    local.fill()
                    NSColor.controlAccentColor.setStroke()
                    let border = NSBezierPath(rect: local.insetBy(dx: 1, dy: 1))
                    border.lineWidth = 2
                    border.stroke()
                }
            }
            return
        }
        // Copy replaces the previous frame rather than accumulating translucent dimming.
        NSColor.black.withAlphaComponent(0.35).setFill()
        bounds.fill(using: .copy)
        if let selectionRect {
            let local = selectionRect.offsetBy(dx: -window.frame.minX, dy: -window.frame.minY).intersection(bounds)
            if !local.isNull && !local.isEmpty {
                NSColor.clear.setFill()
                local.fill(using: .copy)
                NSColor.white.setStroke()
                let border = NSBezierPath(rect: local.insetBy(dx: 0.5, dy: 0.5))
                border.lineWidth = 1
                border.stroke()
            }
        }
        if let readout {
            let origin = CGPoint(x: readout.origin.x - window.frame.minX, y: readout.origin.y - window.frame.minY)
            let rect = CGRect(origin: origin, size: Self.labelSize(for: readout.text))
            NSColor.black.withAlphaComponent(0.75).setFill()
            NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill()
            NSAttributedString(string: readout.text, attributes: Self.textAttributes)
                .draw(at: CGPoint(x: origin.x + Self.horizontalPadding, y: origin.y + Self.verticalPadding))
        }
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let cursorTrackingArea { removeTrackingArea(cursorTrackingArea) }
        let area = NSTrackingArea(rect: bounds, options: [.cursorUpdate, .mouseMoved, .activeAlways, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        cursorTrackingArea = area
    }

    override func cursorUpdate(with event: NSEvent) { controller?.updateCursor() }
    override func mouseMoved(with event: NSEvent) { controller?.mouseMoved(at: NSEvent.mouseLocation) }

    override func mouseDown(with event: NSEvent) {
        guard let point = window?.convertPoint(toScreen: event.locationInWindow) else { return }
        controller?.mouseDown(at: point)
    }
    override func mouseDragged(with event: NSEvent) {
        guard let point = window?.convertPoint(toScreen: event.locationInWindow) else { return }
        controller?.mouseDragged(to: point)
    }
    override func mouseUp(with event: NSEvent) {
        guard let point = window?.convertPoint(toScreen: event.locationInWindow) else { return }
        controller?.mouseUp(at: point)
    }
    override func keyDown(with event: NSEvent) { controller?.keyDown(with: event) }
    override func cancelOperation(_ sender: Any?) { controller?.cancel() }
}
