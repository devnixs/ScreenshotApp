import AppKit

@MainActor
final class AnnotationCanvasView: NSView, NSMenuItemValidation {
    var document: AnnotationDocument { didSet { onDocumentChange?() } }
    lazy var textEditingController = TextEditingController(canvas: self)
    let image: CGImage
    var currentTool: AnnotationTool = .rectangle {
        didSet {
            textEditingController.commit()
            abortDrag()
            if currentTool != .select { document.select(id: nil) }
            needsDisplay = true
            window?.invalidateCursorRects(for: self)
        }
    }
    var currentColor: RGBAColor
    var currentStrokeWidth: StrokeWidth
    var onDocumentChange: (() -> Void)?
    private var dragSession: ShapeDragSession?
    private var dragPoint: CGPoint?
    private(set) var dragPreview: AnnotationShape?
    var isDragging: Bool { dragSession != nil || gesture != nil }
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
        context.translateBy(x: transform.imageRectInView.minX, y: transform.imageRectInView.minY)
        context.scaleBy(x: transform.scale, y: transform.scale)
        context.clip(to: CGRect(origin: .zero, size: document.imagePixelSize))
        ShapeRenderer.drawImage(image, in: context)
        ShapeRenderer.draw(document.shapes.filter { $0.id != textEditingController.editingShapeID }.map { $0.id == dragPreview?.id ? (dragPreview ?? $0) : $0 }, pixelsPerPoint: document.pixelsPerPoint, in: context)
        if let previewShape { ShapeRenderer.draw(previewShape, pixelsPerPoint: document.pixelsPerPoint, in: context) }
        context.restoreGState()
        drawSelection(transform: transform)
    }

    private func drawSelection(transform: CanvasTransform) {
        guard !textEditingController.isEditing, currentTool == .select, let selected = document.selectedShape else { return }
        let shape = dragPreview ?? selected
        NSColor.controlAccentColor.setStroke()
        if case .text = shape.kind {
            let path = NSBezierPath(rect: transform.viewRect(fromImage: shape.bounds(pixelsPerPoint: document.pixelsPerPoint)))
            path.setLineDash([4, 3], count: 2, phase: 0)
            path.lineWidth = 1
            path.stroke()
        }
        for (_, point) in shape.handles {
            let center = transform.viewPoint(fromImage: point)
            let rect = CGRect(x: center.x - 4, y: center.y - 4, width: 8, height: 8)
            let path: NSBezierPath
            if case .arrow = shape.kind { path = NSBezierPath(ovalIn: rect) } else { path = NSBezierPath(rect: rect) }
            NSColor.white.setFill()
            path.fill()
            path.lineWidth = 1
            path.stroke()
        }
    }

    override func mouseDown(with event: NSEvent) {
        let viewPoint = convert(event.locationInWindow, from: nil)
        let point = canvasTransform.isInsideImage(viewPoint: viewPoint) ? canvasTransform.imagePoint(fromView: viewPoint) : nil
        switch TextInteractionDecision.decide(isEditing: textEditingController.isEditing, tool: currentTool,
                                             clickCount: event.clickCount, point: point, document: document) {
        case .commit: textEditingController.commit(); return
        case .beginNew(let point): textEditingController.begin(newAt: point); return
        case .edit(let id):
            if let point { textEditingController.begin(editing: id, caretAt: point) }
            return
        case .none: break
        }
        window?.makeFirstResponder(self)
        if currentTool == .select {
            guard let point = canvasTransform.imagePoint(fromView: convert(event.locationInWindow, from: nil)) else { return }
            let kind: ShapeDragSession.Kind
            switch CanvasHitTester.hitTest(point, in: document, transform: canvasTransform) {
            case .handle(_, let handle): kind = .resize(handle)
            case .shape(let id): document.select(id: id); kind = .move
            case .none: document.select(id: nil); needsDisplay = true; return
            }
            if let shape = document.selectedShape {
                dragSession = ShapeDragSession(original: shape, kind: kind, startPoint: point,
                                               imageBounds: CGRect(origin: .zero, size: document.imagePixelSize))
                dragPoint = point
            }
            needsDisplay = true
            return
        }
        gesture = AnnotationDrawingGesture(tool: currentTool, viewPoint: convert(event.locationInWindow, from: nil),
                                          transform: canvasTransform, color: currentColor, strokeWidth: currentStrokeWidth)
    }
    override func mouseDragged(with event: NSEvent) {
        dragPoint = canvasTransform.imagePoint(fromView: convert(event.locationInWindow, from: nil))
        gesture?.update(viewPoint: convert(event.locationInWindow, from: nil))
        updatePreview(constrained: event.modifierFlags.contains(.shift))
    }
    override func flagsChanged(with event: NSEvent) {
        updatePreview(constrained: event.modifierFlags.contains(.shift))
    }
    private func updatePreview(constrained: Bool) {
        if let dragPoint { dragPreview = dragSession?.preview(at: dragPoint, shiftHeld: constrained) }
        previewShape = gesture?.shape(constrained: constrained, completing: false)
        needsDisplay = true
    }
    override func mouseUp(with event: NSEvent) {
        if let session = dragSession, let point = canvasTransform.imagePoint(fromView: convert(event.locationInWindow, from: nil)),
           let shape = session.finish(at: point, shiftHeld: event.modifierFlags.contains(.shift)) { document.replace(shape) }
        dragSession = nil
        dragPreview = nil
        dragPoint = nil
        gesture?.update(viewPoint: convert(event.locationInWindow, from: nil))
        if let shape = gesture?.shape(constrained: event.modifierFlags.contains(.shift), completing: true) { document.add(shape) }
        gesture = nil
        previewShape = nil
        needsDisplay = true
    }
    override func keyDown(with event: NSEvent) {
        if let command = CanvasKeyCommand(keyCode: event.keyCode, modifiers: event.modifierFlags) {
            switch command {
            case .deleteSelection: delete(nil)
            case .deselect: cancelOperation(nil)
            }
            return
        }
        let forbidden: NSEvent.ModifierFlags = [.command, .option, .control]
        let characters = event.charactersIgnoringModifiers ?? ""
        if let tool = AnnotationTool.forShortcut(characters: characters,
                                                hasCommandOptionOrControl: !event.modifierFlags.intersection(forbidden).isEmpty,
                                                textToolEnabled: true) {
            onToolChange?(tool)
        } else { super.keyDown(with: event) }
    }
    private func abortDrag() {
        gesture = nil
        previewShape = nil
        dragSession = nil
        dragPreview = nil
        dragPoint = nil
        needsDisplay = true
    }
    override func cancelOperation(_ sender: Any?) {
        if textEditingController.isEditing { textEditingController.commit(); return }
        abortDrag()
        document.select(id: nil)
    }
    @objc func delete(_ sender: Any?) {
        guard !isDragging, let id = document.selectedShapeID else { return }
        document.delete(id: id)
        needsDisplay = true
    }
    @objc func undo(_ sender: Any?) {
        guard !isDragging else { return }
        document.undo()
        if currentTool != .select { document.select(id: nil) }
        needsDisplay = true
    }
    @objc func redo(_ sender: Any?) {
        guard !isDragging else { return }
        document.redo()
        if currentTool != .select { document.select(id: nil) }
        needsDisplay = true
    }
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(undo(_:)): return !isDragging && document.canUndo
        case #selector(redo(_:)): return !isDragging && document.canRedo
        case #selector(delete(_:)): return !isDragging && document.selectedShape != nil
        default: return true
        }
    }
    override func resetCursorRects() {
        super.resetCursorRects()
        addCursorRect(bounds, cursor: .arrow)
        if currentTool.createsShapesByDragging { addCursorRect(canvasTransform.imageRectInView, cursor: .crosshair) }
    }
    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        textEditingController.layout(using: canvasTransform)
        needsDisplay = true
        window?.invalidateCursorRects(for: self)
    }
}
