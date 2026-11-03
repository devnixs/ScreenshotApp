import AppKit

@MainActor
final class AnnotationWindowManager {
    private(set) var controllers: [AnnotationWindowController] = []
    private let settings: AppSettings
    init(settings: AppSettings) { self.settings = settings }

    func commitAllTextEdits() {
        for controller in controllers { controller.canvas.textEditingController.commit() }
    }
    var dirtyWindowCount: Int { controllers.filter { $0.canvas.document.isDirty }.count }

    func open(_ captured: CapturedImage, displayID: CGDirectDisplayID?, captureDate: Date) {
        let target = NSScreen.screens.first {
            ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == displayID
        } ?? NSScreen.main ?? NSScreen.screens.first
        guard let target else { return }
        let origins = controllers.compactMap { controller -> CGPoint? in
            guard let frame = controller.window?.frame else { return nil }
            return CGPoint(x: frame.minX, y: frame.maxY)
        }
        let controller = AnnotationWindowController(captured: captured, captureDate: captureDate, screen: target,
                                                    settings: settings, existingOrigins: origins)
        controller.onClose = { [weak self] closed in self?.controllers.removeAll { $0 === closed } }
        controllers.append(controller)
        controller.showWindow(nil)
        NSApp.activate()
        controller.window?.makeKeyAndOrderFront(nil)
        controller.window?.orderFrontRegardless()
        controller.window?.makeFirstResponder(controller.canvas)
    }
}
