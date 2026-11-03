import AppKit
import UniformTypeIdentifiers

@MainActor
final class AnnotationWindowController: NSWindowController, NSWindowDelegate, NSToolbarDelegate, NSMenuItemValidation {
    static let toolsIdentifier = NSToolbarItem.Identifier("AnnotationTools")
    static let styleIdentifier = NSToolbarItem.Identifier("AnnotationStyle")
    static let saveIdentifier = NSToolbarItem.Identifier("AnnotationSave")
    static let copyIdentifier = NSToolbarItem.Identifier("AnnotationCopy")
    let captureDate: Date
    private var isClosingConfirmed = false
    private let settings: AppSettings
    private(set) var toolbarStyleState: ToolbarStyleState
    private let styleControls = NSStackView()
    private var swatches: [AnnotationSwatchButton] = []
    private let customColor = AnnotationSwatchButton(title: "+", target: nil, action: nil)
    private let stroke = NSSegmentedControl()
    private var suppressColorAction = false
    let canvas: AnnotationCanvasView
    var onClose: ((AnnotationWindowController) -> Void)?
    private let tools = NSSegmentedControl()
    private let toolbarItems: [NSToolbarItem.Identifier] = [toolsIdentifier, styleIdentifier, .flexibleSpace, saveIdentifier, copyIdentifier]
    var currentTool: AnnotationTool = .rectangle {
        didSet {
            canvas.currentTool = currentTool
            tools.selectedSegment = currentTool.segmentIndex
        }
    }

    init(captured: CapturedImage, captureDate: Date, screen: NSScreen, settings: AppSettings, existingOrigins: [CGPoint]) {
        self.settings = settings
        self.captureDate = captureDate
        toolbarStyleState = ToolbarStyleState(currentStyle: ShapeStyle(color: settings.annotationColor, strokeWidth: settings.annotationStrokeWidth))
        canvas = AnnotationCanvasView(image: captured, color: settings.annotationColor, strokeWidth: settings.annotationStrokeWidth)
        let window = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 480, height: 300),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        super.init(window: window)
        window.delegate = self
        window.isReleasedWhenClosed = false
        window.contentMinSize = CGSize(width: 480, height: 300)
        window.tabbingMode = .disallowed
        window.title = FileNamer().baseName(for: .screenshot, date: captureDate)
        window.contentView = canvas
        window.initialFirstResponder = canvas
        tools.segmentCount = AnnotationTool.allCases.count
        tools.trackingMode = .selectOne
        tools.target = self
        tools.action = #selector(toolChanged(_:))
        let symbols = ["cursorarrow", "rectangle", "arrow.up.right", "textformat"]
        let labels = ["Select (V)", "Rectangle (R)", "Arrow (A)", "Text (T)"]
        for tool in AnnotationTool.allCases {
            let i = tool.segmentIndex
            tools.setImage(NSImage(systemSymbolName: symbols[i], accessibilityDescription: labels[i]), forSegment: i)
            tools.setToolTip(labels[i], forSegment: i)
            tools.setWidth(36, forSegment: i)
        }
        tools.setAccessibilityLabel("Annotation tools")
        tools.refusesFirstResponder = true
        tools.selectedSegment = currentTool.segmentIndex
        canvas.onToolChange = { [weak self] tool in self?.currentTool = tool }
        canvas.onDocumentChange = { [weak self] in self?.refreshStyle() }
        configureStyleControls()
        let toolbar = NSToolbar(identifier: "AnnotationToolbar")
        toolbar.delegate = self
        toolbar.displayMode = .iconOnly
        toolbar.allowsUserCustomization = false
        window.toolbar = toolbar
        window.toolbarStyle = .unified
        let content = CGRect(x: 0, y: 0, width: 480, height: 300)
        let chrome = window.frameRect(forContentRect: content).height - content.height
        let placement = AnnotationWindowSizing.frame(imagePixelSize: canvas.document.imagePixelSize,
            pixelsPerPoint: captured.pixelsPerPoint, screenVisibleFrame: screen.visibleFrame,
            chromeHeight: chrome, minContentSize: window.contentMinSize)
        // Tiny displays must still allow a frame that fits their available content area.
        window.contentMinSize = CGSize(width: min(480, screen.visibleFrame.width),
                                       height: min(300, max(0, screen.visibleFrame.height - chrome)))
        window.setFrame(AnnotationWindowSizing.cascaded(placement.windowFrame, avoiding: existingOrigins,
                                                       within: screen.visibleFrame), display: false)
    }
    required init?(coder: NSCoder) { nil }
    @objc private func toolChanged(_ sender: NSSegmentedControl) {
        guard let tool = AnnotationTool(segmentIndex: sender.selectedSegment) else { return }
        currentTool = tool
        window?.makeFirstResponder(canvas)
    }
    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] { toolbarItems }
    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] { toolbarItems }
    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier identifier: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        if identifier == Self.saveIdentifier || identifier == Self.copyIdentifier {
            let saving = identifier == Self.saveIdentifier
            let item = NSToolbarItem(itemIdentifier: identifier)
            item.label = saving ? "Save" : "Copy"
            item.toolTip = saving ? "Save (⌘S)" : "Copy (⌘C)"
            item.image = NSImage(systemSymbolName: saving ? "square.and.arrow.down" : "doc.on.doc", accessibilityDescription: item.label)
            item.target = self
            item.action = saving ? #selector(saveDocument(_:)) : #selector(copy(_:))
            return item
        }
        guard identifier == Self.toolsIdentifier || identifier == Self.styleIdentifier else { return nil }
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.label = identifier == Self.toolsIdentifier ? "Tools" : "Style"
        item.view = identifier == Self.toolsIdentifier ? tools : styleControls
        return item
    }
    private func configureStyleControls() {
        stroke.refusesFirstResponder = true
        customColor.refusesFirstResponder = true
        styleControls.orientation = .horizontal
        styleControls.spacing = 4
        for (index, preset) in ColorPreset.allCases.enumerated() {
            let button = AnnotationSwatchButton(title: "", target: self, action: #selector(swatchPicked(_:)))
            button.refusesFirstResponder = true
            button.tag = index
            button.swatchColor = preset.color.nsColor
            button.toolTip = preset.displayName
            button.setAccessibilityLabel(preset.displayName)
            swatches.append(button)
            styleControls.addArrangedSubview(button)
        }
        customColor.target = self
        customColor.action = #selector(openColorPanel(_:))
        customColor.toolTip = "Custom color"
        customColor.setAccessibilityLabel("Custom color")
        styleControls.addArrangedSubview(customColor)
        stroke.segmentCount = 3
        stroke.trackingMode = .selectOne
        for (index, label) in ["S", "M", "L"].enumerated() {
            stroke.setLabel(label, forSegment: index)
            stroke.setWidth(26, forSegment: index)
        }
        stroke.setAccessibilityLabel("Stroke width")
        stroke.target = self
        stroke.action = #selector(strokePicked(_:))
        styleControls.addArrangedSubview(stroke)
        refreshStyle()
    }
    func refreshStyle() {
        _ = toolbarStyleState.reduce(.selectedShapeStyleChanged(canvas.textEditingController.style ?? canvas.document.selectedShape?.style))
        canvas.currentColor = toolbarStyleState.currentStyle.color
        canvas.currentStrokeWidth = toolbarStyleState.currentStyle.strokeWidth
        for (index, button) in swatches.enumerated() {
            button.isHighlightedSwatch = toolbarStyleState.swatchSelection == .preset(index)
        }
        customColor.isHighlightedSwatch = toolbarStyleState.swatchSelection == .custom
        stroke.selectedSegment = StrokeWidth.allCases.firstIndex(of: toolbarStyleState.displayedStyle.strokeWidth) ?? 1
        canvas.needsDisplay = true
    }
    private func pick(_ event: ToolbarStyleEvent) {
        guard !canvas.isDragging else { return }
        let effects = toolbarStyleState.reduce(event)
        if canvas.textEditingController.isEditing {
            if let color = effects.persistColor { canvas.textEditingController.applyColor(color) }
            if let width = effects.persistStrokeWidth { canvas.textEditingController.applyStrokeWidth(width) }
        } else if var shape = canvas.document.selectedShape {
            if let color = effects.applyColorToSelection { shape.color = color }
            if let width = effects.applyStrokeWidthToSelection { shape = shape.withStrokeWidth(width, pixelsPerPoint: canvas.document.pixelsPerPoint, measurer: AppKitTextMeasurer()) }
            canvas.document.replace(shape)
        }
        if let color = effects.persistColor { settings.annotationColor = color }
        if let width = effects.persistStrokeWidth { settings.annotationStrokeWidth = width }
        refreshStyle()
        if !canvas.textEditingController.isEditing { window?.makeFirstResponder(canvas) }
    }
    @objc private func swatchPicked(_ sender: NSButton) { pick(.colorPicked(RGBAColor.presets[sender.tag])) }
    @objc private func strokePicked(_ sender: NSSegmentedControl) {
        guard StrokeWidth.allCases.indices.contains(sender.selectedSegment) else { return }
        pick(.strokeWidthPicked(StrokeWidth.allCases[sender.selectedSegment]))
    }
    @objc private func openColorPanel(_ sender: Any?) {
        retargetColorPanel()
        NSColorPanel.shared.orderFront(nil)
    }
    private func retargetColorPanel() {
        let panel = NSColorPanel.shared
        Self.colorPanelOwner = self
        suppressColorAction = true
        panel.showsAlpha = false
        panel.isContinuous = true
        panel.color = toolbarStyleState.displayedStyle.color.nsColor
        panel.setTarget(self)
        panel.setAction(#selector(colorPanelDidPick(_:)))
        suppressColorAction = false
    }
    @objc private func colorPanelDidPick(_ panel: NSColorPanel) {
        guard !suppressColorAction, let color = RGBAColor(nsColor: panel.color) else { return }
        pick(.colorPicked(color))
    }
    func windowDidBecomeKey(_ notification: Notification) {
        if !canvas.textEditingController.isEditing { window?.makeFirstResponder(canvas) }
        if NSColorPanel.shared.isVisible { retargetColorPanel() }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        canvas.textEditingController.commit()
        if isClosingConfirmed { return true }
        guard sender.attachedSheet == nil else { NSSound.beep(); return false }
        guard UnsavedAnnotationsPolicy.closeDecision(isDirtyAfterCommittingText: canvas.document.isDirty) == .confirmDiscard else { return true }
        let alert = NSAlert()
        alert.messageText = UnsavedAnnotationsPolicy.discardTitle
        alert.alertStyle = .warning
        alert.addButton(withTitle: UnsavedAnnotationsPolicy.discardButton).hasDestructiveAction = true
        alert.addButton(withTitle: UnsavedAnnotationsPolicy.cancelButton)
        alert.beginSheetModal(for: sender) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self else { return }
            self.isClosingConfirmed = true
            self.window?.close()
        }
        return false
    }

    @objc func saveDocument(_ sender: Any?) {
        guard !canvas.isDragging else { NSSound.beep(); return }
        canvas.textEditingController.commit()
        guard let window, window.attachedSheet == nil else { NSSound.beep(); return }
        let directory = try? SaveFolder(url: settings.saveFolderURL).prepareForWriting()
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.canCreateDirectories = true
        panel.directoryURL = directory
        panel.nameFieldStringValue = AnnotationSaveDefaults.suggestedFileName(captureDate: captureDate, directory: directory) {
            FileManager.default.fileExists(atPath: $0.path)
        }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let self, let url = panel.url else { return }
            do {
                // Snapshot at confirmation, so only the exported revision becomes clean.
                let image = try AnnotationExporter.flatten(image: self.canvas.image, document: self.canvas.document)
                let png = try PNGEncoder.data(for: image, pixelsPerPoint: self.canvas.document.pixelsPerPoint)
                try png.write(to: url, options: .atomic)
                self.canvas.document.markClean()
            } catch {
                ErrorAlerts.showError(title: "Couldn't save to \(url.deletingLastPathComponent().lastPathComponent)", error: error)
            }
        }
    }

    @objc func copy(_ sender: Any?) {
        guard !canvas.isDragging else { NSSound.beep(); return }
        canvas.textEditingController.commit()
        do {
            let image = try AnnotationExporter.flatten(image: canvas.image, document: canvas.document)
            let png = try PNGEncoder.data(for: image, pixelsPerPoint: canvas.document.pixelsPerPoint)
            guard Clipboard.copy(pngData: png, image: image) else {
                throw NSError(domain: "com.thera-engineering.snapper.clipboard", code: 1,
                    userInfo: [NSLocalizedDescriptionKey: "The clipboard couldn't accept the annotated image."])
            }
            canvas.document.markClean()
        } catch { ErrorAlerts.showError(title: "Couldn't copy to clipboard", error: error) }
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        menuItem.action == #selector(copy(_:)) || menuItem.action == #selector(saveDocument(_:))
    }

    func windowWillClose(_ notification: Notification) {
        // NSColorPanel exposes setters but no target getter. Clear only our panel ownership.
        if Self.colorPanelOwner === self {
            NSColorPanel.shared.setTarget(nil)
            Self.colorPanelOwner = nil
        }
        onClose?(self)
    }
    private static weak var colorPanelOwner: AnnotationWindowController?

}

/// View-space toolbar adornments; excluded from the shared shape renderer.
@MainActor
private final class AnnotationSwatchButton: NSButton {
    var swatchColor: NSColor? { didSet { needsDisplay = true } }
    var isHighlightedSwatch = false { didSet { needsDisplay = true } }
    override var intrinsicContentSize: NSSize { NSSize(width: 24, height: 26) }
    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 4, dy: 5)
        let path = NSBezierPath(ovalIn: rect)
        (swatchColor ?? .controlBackgroundColor).setFill()
        path.fill()
        NSColor.separatorColor.setStroke()
        path.lineWidth = 1
        path.stroke()
        if swatchColor == nil {
            let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 16), .foregroundColor: NSColor.labelColor]
            let size = title.size(withAttributes: attributes)
            title.draw(at: CGPoint(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2), withAttributes: attributes)
        }
        if isHighlightedSwatch {
            NSColor.controlAccentColor.setStroke()
            let ring = NSBezierPath(ovalIn: rect.insetBy(dx: -2, dy: -2))
            ring.lineWidth = 2
            ring.stroke()
        }
    }
}
