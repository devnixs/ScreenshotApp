import AppKit

@MainActor
final class AnnotationWindowController: NSWindowController, NSWindowDelegate, NSToolbarDelegate {
    static let toolsIdentifier = NSToolbarItem.Identifier("AnnotationTools")
    let canvas: AnnotationCanvasView
    var onClose: ((AnnotationWindowController) -> Void)?
    private let tools = NSSegmentedControl()
    private let toolbarItems: [NSToolbarItem.Identifier] = [toolsIdentifier]
    var currentTool: AnnotationTool = .rectangle {
        didSet {
            canvas.currentTool = currentTool
            tools.selectedSegment = currentTool.segmentIndex
        }
    }

    init(captured: CapturedImage, captureDate: Date, screen: NSScreen, settings: AppSettings, existingOrigins: [CGPoint]) {
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
        tools.setEnabled(false, forSegment: AnnotationTool.text.segmentIndex)
        tools.selectedSegment = currentTool.segmentIndex
        canvas.onToolChange = { [weak self] tool in self?.currentTool = tool }
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
        guard let tool = AnnotationTool(segmentIndex: sender.selectedSegment), tool != .text else { return }
        currentTool = tool
        window?.makeFirstResponder(canvas)
    }
    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] { toolbarItems }
    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] { toolbarItems }
    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier identifier: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        guard identifier == Self.toolsIdentifier else { return nil }
        let item = NSToolbarItem(itemIdentifier: identifier)
        item.label = "Tools"
        item.view = tools
        return item
    }
    func windowWillClose(_ notification: Notification) { onClose?(self) }
}
