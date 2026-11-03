import AppKit

@MainActor
final class AnnotationCanvasView: NSView {
    var document: AnnotationDocument
    let image: CGImage
    var currentTool: AnnotationTool = .rectangle {
        didSet {
            gesture = nil
            previewShape = nil
            needsDisplay = true
            window?.invalidateCursorRects(for: self)
        }
    }
    var currentColor: RGBAColor
    var currentStrokeWidth: StrokeWidth
    var onToolChange: ((AnnotationTool) -> Void)?
    private var gesture: AnnotationDrawingGesture?
    private(set) var previewShape: AnnotationShape?

    init(image: CapturedImage, color: RGBAColor, strokeWidth: StrokeWidth) {
        self.image = image.cgImage
        document = AnnotationDocument(imagePixelSize: CGSize(width: image.cgImage.width, height: image.cgImage.height),
                                      pixelsPerPoint: image.pixelsPerPoint)
        currentColor = color
        currentStrokeWidth = strokeWidth
        super.init(frame: .zero)
        wantsLayer = true
        layerContentsRedrawPolicy = .onSetNeedsDisplay
        autoresizingMask = [.width, .height]
    }
    required init?(coder: NSCoder) { nil }
    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    var canvasTransform: CanvasTransform {
        CanvasTransform(imagePixelSize: document.imagePixelSize, pixelsPerPoint: document.pixelsPerPoint, viewSize: bounds.size)
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.underPageBackgroundColor.setFill()
        bounds.fill()
        let transform = canvasTransform
        guard transform.scale > 0, let context = NSGraphicsContext.current?.cgContext else { return }
        context.saveGState()
        defer { context.restoreGState() }
        context.translateBy(x: transform.imageRectInView.minX, y: transform.imageRectInView.minY)
        context.scaleBy(x: transform.scale, y: transform.scale)
        context.clip(to: CGRect(origin: .zero, size: document.imagePixelSize))
        ShapeRenderer.drawImage(image, in: context)
        ShapeRenderer.draw(document.shapes, pixelsPerPoint: document.pixelsPerPoint, in: context)
        if let previewShape { ShapeRenderer.draw(previewShape, pixelsPerPoint: document.pixelsPerPoint, in: context) }
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        gesture = AnnotationDrawingGesture(tool: currentTool, viewPoint: convert(event.locationInWindow, from: nil),
                                          transform: canvasTransform, color: currentColor, strokeWidth: currentStrokeWidth)
    }
    override func mouseDragged(with event: NSEvent) {
        gesture?.update(viewPoint: convert(event.locationInWindow, from: nil))
        updatePreview(constrained: event.modifierFlags.contains(.shift))
    }
    override func flagsChanged(with event: NSEvent) {
        updatePreview(constrained: event.modifierFlags.contains(.shift))
    }
    private func updatePreview(constrained: Bool) {
        previewShape = gesture?.shape(constrained: constrained, completing: false)
        needsDisplay = true
    }
    override func mouseUp(with event: NSEvent) {
        gesture?.update(viewPoint: convert(event.locationInWindow, from: nil))
        if let shape = gesture?.shape(constrained: event.modifierFlags.contains(.shift), completing: true) { document.add(shape) }
        gesture = nil
        previewShape = nil
        needsDisplay = true
    }
    override func keyDown(with event: NSEvent) {
        let forbidden: NSEvent.ModifierFlags = [.command, .option, .control]
        let characters = event.charactersIgnoringModifiers ?? ""
        // The visible but disabled Text tool must not beep or change tools before 0019.
        if characters.lowercased() == "t", event.modifierFlags.intersection(forbidden).isEmpty { return }
        if let tool = AnnotationTool.forShortcut(characters: characters,
                                                hasCommandOptionOrControl: !event.modifierFlags.intersection(forbidden).isEmpty,
                                                textToolEnabled: false) {
            onToolChange?(tool)
        } else { super.keyDown(with: event) }
    }
    override func resetCursorRects() {
        super.resetCursorRects()
        addCursorRect(bounds, cursor: .arrow)
        if currentTool.createsShapesByDragging { addCursorRect(canvasTransform.imageRectInView, cursor: .crosshair) }
    }
    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        needsDisplay = true
        window?.invalidateCursorRects(for: self)
    }
}
