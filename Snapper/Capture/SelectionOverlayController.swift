import AppKit
import QuartzCore
import os

@MainActor
final class SelectionOverlayController: NSObject {
    static let dismissalDelay: Duration = .milliseconds(50)
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "selection")
    private var panels: [SelectionOverlayPanel] = []
    private var displays: [DisplayInfo] = []
    private var tracker = RectSelectionTracker()
    private var continuation: CheckedContinuation<SelectionResult?, Never>?
    private var previousApplication: NSRunningApplication?
    private var isDismissing = false

    func selectRegion() async -> SelectionResult? {
        guard continuation == nil else {
            logger.fault("Selection requested while already presenting")
            return nil
        }
        return await withCheckedContinuation { continuation in
            self.continuation = continuation
            present()
        }
    }

    private func present() {
        let screens = NSScreen.screens
        displays = screens.compactMap(DisplayInfo.from)
        let frontmost = NSWorkspace.shared.frontmostApplication
        previousApplication = frontmost?.processIdentifier == ProcessInfo.processInfo.processIdentifier ? nil : frontmost
        panels = screens.compactMap { screen in
            guard let info = DisplayInfo.from(screen) else { return nil }
            return SelectionOverlayPanel(screen: screen, displayInfo: info, controller: self)
        }
        guard !panels.isEmpty else { cancel(); return }
        NotificationCenter.default.addObserver(self, selector: #selector(screenParametersChanged),
                                              name: NSApplication.didChangeScreenParametersNotification, object: nil)
        for panel in panels { panel.orderFrontRegardless() }
        NSApp.activate()
        let mouseDisplay = DisplayGeometry.display(containing: NSEvent.mouseLocation, in: displays)
        if let keyPanel = panels.first(where: { $0.displayInfo == mouseDisplay }) ?? panels.first {
            keyPanel.makeKeyAndOrderFront(nil)
            keyPanel.makeFirstResponder(keyPanel.overlayView)
        }
        NSCursor.crosshair.set()
    }

    func mouseDown(at point: CGPoint) {
        guard !panels.isEmpty else { return }
        tracker.mouseDown(at: point, displays: displays)
        updateViews()
    }

    func mouseDragged(to point: CGPoint) {
        tracker.drag(to: point)
        updateViews()
    }

    func mouseUp(at point: CGPoint) {
        switch tracker.mouseUp(at: point) {
        case .selected(let result): finish(with: result)
        case .tooSmall, .ignored: updateViews()
        }
    }

    func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { cancel() }
    }

    func cancel() {
        guard continuation != nil, !isDismissing else { return }
        close()
        let pending = continuation
        continuation = nil
        pending?.resume(returning: nil)
    }

    @objc private func screenParametersChanged() {
        if DisplayConfigurationChange.requiresCancel(old: displays, new: DisplayInfo.allCurrent()) { cancel() }
    }

    private func updateViews() {
        for panel in panels {
            let view = panel.overlayView
            guard panel.displayInfo == tracker.startDisplay, let rect = tracker.currentRect,
                  let cursor = tracker.currentPoint else {
                view.selectionRect = nil
                view.readout = nil
                continue
            }
            let text = SelectionReadout.text(for: rect.size)
            view.selectionRect = rect
            view.readout = (text, SelectionReadout.origin(labelSize: SelectionOverlayView.labelSize(for: text),
                                                        cursor: cursor, display: panel.displayInfo.frame))
        }
    }

    private func finish(with result: SelectionResult) {
        guard !panels.isEmpty else { return }
        isDismissing = true
        close()
        Task { @MainActor in
            await Task.yield()
            try? await Task.sleep(for: Self.dismissalDelay)
            let pending = continuation
            continuation = nil
            isDismissing = false
            pending?.resume(returning: result)
        }
    }

    private func close() {
        tracker.reset()
        NotificationCenter.default.removeObserver(self, name: NSApplication.didChangeScreenParametersNotification, object: nil)
        for panel in panels { panel.orderOut(nil) }
        panels.removeAll()
        CATransaction.flush()
        NSCursor.arrow.set()
        if let previousApplication, !previousApplication.isTerminated { previousApplication.activate() }
        previousApplication = nil
    }
}
