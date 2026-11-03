import AppKit

@MainActor
final class TextEditingController: NSObject, NSTextViewDelegate {
    private unowned let canvas: AnnotationCanvasView
    private let measurer = AppKitTextMeasurer()
    private var draft: TextEditDraft?
    private var startedWithSelect = false
    private var sessionUndoManager = UndoManager()
    private let clippingView = TextClippingView()
    private(set) var textView: AnnotationTextView?
    var isEditing: Bool { draft != nil }
    var editingShapeID: UUID? { draft?.original?.id }
    var style: ShapeStyle? { draft.map { ShapeStyle(color: $0.color, strokeWidth: $0.strokeWidth) } }

    init(canvas: AnnotationCanvasView) {
        self.canvas = canvas
        super.init()
        clippingView.wantsLayer = true
        clippingView.layer?.masksToBounds = true
    }
    func begin(newAt point: CGPoint) {
        let fontSize = TextMetrics.fontPixelSize(for: canvas.currentStrokeWidth, pixelsPerPoint: canvas.document.pixelsPerPoint)
        let origin = TextMetrics.newBoxOrigin(click: point, lineHeight: measurer.lineHeight(fontPixelSize: fontSize), imageSize: canvas.document.imagePixelSize)
        start(TextEditDraft(original: nil, string: "", origin: origin, color: canvas.currentColor,
                            strokeWidth: canvas.currentStrokeWidth, measuredSize: measurer.layoutSize(of: "", fontPixelSize: fontSize)), caretAt: point)
    }
    func begin(editing id: UUID, caretAt point: CGPoint) {
        guard let shape = canvas.document.shape(withID: id), case .text(let box) = shape.kind else { return }
        start(TextEditDraft(original: shape, string: box.string, origin: box.origin, color: shape.color,
                            strokeWidth: shape.strokeWidth, measuredSize: box.measuredSize), caretAt: point)
    }
    private func start(_ draft: TextEditDraft, caretAt point: CGPoint) {
        guard !isEditing else { return }
        self.draft = draft
        startedWithSelect = canvas.currentTool == .select
        sessionUndoManager = UndoManager()
        let view = AnnotationTextView()
        textView = view
        view.string = draft.string
        view.delegate = self
        canvas.addSubview(clippingView)
        clippingView.addSubview(view)
        layout(using: canvas.canvasTransform)
        canvas.window?.makeFirstResponder(view)
        let transform = canvas.canvasTransform
        let local = CGPoint(x: (point.x - draft.origin.x) * transform.scale,
                            y: (point.y - draft.origin.y) * transform.scale)
        view.setSelectedRange(NSRange(location: view.characterIndexForInsertion(at: local), length: 0))
        canvas.onDocumentChange?()
        canvas.needsDisplay = true
    }
    func textDidChange(_ notification: Notification) {
        guard let textView else { return }
        draft?.string = textView.string
        remeasure()
        layout(using: canvas.canvasTransform)
    }
    private func remeasure() {
        guard let draft else { return }
        self.draft?.measuredSize = measurer.layoutSize(of: draft.string,
            fontPixelSize: TextMetrics.fontPixelSize(for: draft.strokeWidth, pixelsPerPoint: canvas.document.pixelsPerPoint))
    }
    func applyColor(_ color: RGBAColor) {
        draft?.color = color
        layout(using: canvas.canvasTransform)
    }
    func applyStrokeWidth(_ width: StrokeWidth) {
        draft?.strokeWidth = width
        remeasure()
        layout(using: canvas.canvasTransform)
    }
    func layout(using transform: CanvasTransform) {
        guard let draft, let view = textView, transform.scale > 0 else { return }
        clippingView.frame = transform.imageRectInView
        let fontSize = TextMetrics.editorFontSize(
            fontPixelSize: TextMetrics.fontPixelSize(for: draft.strokeWidth, pixelsPerPoint: canvas.document.pixelsPerPoint),
            canvasScale: transform.scale)
        let attributes = TextStyle.strokeAttributes(size: fontSize, color: draft.color, editing: true)
        let selection = view.selectedRange()
        view.textStorage?.setAttributes(attributes, range: NSRange(location: 0, length: (view.string as NSString).length))
        view.typingAttributes = attributes
        view.setSelectedRange(selection)
        // The clipping host uses a flipped coordinate system through this subclass.
        view.frame = CGRect(x: draft.origin.x * transform.scale, y: draft.origin.y * transform.scale,
                            width: max(2, draft.measuredSize.width * transform.scale + 2),
                            height: max(1, draft.measuredSize.height * transform.scale))
    }
    func commit() {
        guard draft != nil else { return }
        textView?.unmarkText()
        draft?.string = textView?.string ?? draft?.string ?? ""
        remeasure()
        guard let final = draft else { return }
        draft = nil
        textView?.delegate = nil
        textView?.removeFromSuperview()
        textView = nil
        clippingView.removeFromSuperview()
        let decision = TextCommitDecision.decide(final)
        canvas.document.apply(decision)
        let selectedID: UUID?
        switch decision {
        case .add(let shape), .update(let shape): selectedID = shape.id
        case .noChange: selectedID = final.original?.id
        case .discard, .delete: selectedID = nil
        }
        canvas.document.select(id: startedWithSelect ? selectedID : nil)
        sessionUndoManager.removeAllActions()
        canvas.window?.makeFirstResponder(canvas)
        canvas.needsDisplay = true
    }
    func undoManager(for view: NSTextView) -> UndoManager? { sessionUndoManager }
    func textView(_ textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        if commandSelector == #selector(NSResponder.cancelOperation(_:)) || commandSelector == #selector(NSTextView.complete(_:)) {
            commit()
            return true
        }
        return false
    }
}

@MainActor
private final class TextClippingView: NSView {
    override var isFlipped: Bool { true }
}
