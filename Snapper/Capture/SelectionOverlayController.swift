import AppKit
import QuartzCore
import os

@MainActor
final class SelectionOverlayController: NSObject {
    static let dismissalDelay: Duration = .milliseconds(50)
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "selection")
    private var panels: [SelectionOverlayPanel] = []
    private var displays: [DisplayInfo] = []
    private var session = SelectionSession()
    private var hitTester: WindowHitTester?
    private var hoveredWindow: WindowCandidate?
    private var candidateTask: Task<Void, Never>?
    private var sessionToken: UUID?
    private var continuation: CheckedContinuation<SelectionResult?, Never>?
    private var previousApplication: NSRunningApplication?
    private var purpose: CapturePurpose?
    private var isDismissing = false

    func selectRegion(for purpose: CapturePurpose) async -> SelectionResult? {
        guard continuation == nil else {
            logger.fault("Selection requested while already presenting")
            return nil
        }
        return await withCheckedContinuation { continuation in
            self.continuation = continuation
            self.purpose = purpose
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
        updateCursor()
        let token = UUID()
        sessionToken = token
        candidateTask = Task { @MainActor [weak self] in
            do {
                let candidates = try await WindowCandidateProvider().loadCandidates()
                guard let self, self.sessionToken == token else { return }
                self.hitTester = WindowHitTester(candidates: candidates,
                                                excludedPID: ProcessInfo.processInfo.processIdentifier)
                self.mouseMoved(at: NSEvent.mouseLocation)
            } catch {
                guard let self, self.sessionToken == token else { return }
                self.logger.error("Window candidates unavailable: \(error.localizedDescription, privacy: .public)")
            }
        }
    }

    func mouseDown(at point: CGPoint) {
        guard !panels.isEmpty else { return }
        session.mouseDown(at: point, displays: displays)
        updateViews()
    }

    func mouseDragged(to point: CGPoint) {
        session.mouseDragged(to: point)
        if session.mode == .window { mouseMoved(at: point) }
        else { updateViews() }
    }

    func mouseUp(at point: CGPoint) {
        switch session.mouseUp(at: point) {
        case .rectangle(let result): finish(with: result)
        case .windowClick(let point):
            guard let primaryHeight = DisplayGeometry.primaryHeight(in: displays),
                  let candidate = hitTester?.window(at: DisplayGeometry.cgPoint(fromAppKit: point, primaryHeight: primaryHeight)),
                  let display = WindowDisplayRule.display(for: candidate.frame, displays: displays,
                                                          primaryDisplayHeight: primaryHeight) else { return }
            finish(with: .window(windowID: candidate.windowID, frame: candidate.frame, display: display))
        case .none: updateViews()
        }
    }

    func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { cancel() }
        else if event.keyCode == 49 && !event.isARepeat {
            session.toggleMode()
            hoveredWindow = nil
            updateCursor()
            mouseMoved(at: NSEvent.mouseLocation)
            updateViews()
        }
    }

    func updateCursor() {
        (session.mode == .rectangle ? NSCursor.crosshair : NSCursor.arrow).set()
    }

    func mouseMoved(at point: CGPoint) {
        updateCursor()
        guard session.mode == .window, let height = DisplayGeometry.primaryHeight(in: displays) else { return }
        let candidate = hitTester?.window(at: DisplayGeometry.cgPoint(fromAppKit: point, primaryHeight: height))
        guard hoveredWindow?.windowID != candidate?.windowID else { return }
        hoveredWindow = candidate
        updateViews()
    }

    func cancel() {
        guard continuation != nil, !isDismissing else { return }
        close(outcome: .cancelled)
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
            view.mode = session.mode
            view.windowHighlight = hoveredWindow.flatMap { candidate in
                DisplayGeometry.primaryHeight(in: displays).map {
                    DisplayGeometry.appKitRect(fromCG: candidate.frame, primaryHeight: $0)
                }
            }
            guard session.mode == .rectangle, panel.displayInfo == session.tracker.startDisplay,
                  let rect = session.tracker.currentRect, let cursor = session.tracker.currentPoint else {
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
        close(outcome: .selected)
        Task { @MainActor in
            await Task.yield()
            try? await Task.sleep(for: Self.dismissalDelay)
            let pending = continuation
            continuation = nil
            isDismissing = false
            pending?.resume(returning: result)
        }
    }

    private func close(outcome: SelectionFocusPolicy.Outcome) {
        sessionToken = nil
        candidateTask?.cancel()
        candidateTask = nil
        hitTester = nil
        hoveredWindow = nil
        session = SelectionSession()
        NotificationCenter.default.removeObserver(self, name: NSApplication.didChangeScreenParametersNotification, object: nil)
        for panel in panels { panel.orderOut(nil) }
        panels.removeAll()
        CATransaction.flush()
        NSCursor.arrow.set()
        if let purpose, SelectionFocusPolicy.restoresPreviousApplication(purpose: purpose, outcome: outcome),
           let previousApplication, !previousApplication.isTerminated {
            previousApplication.activate()
        }
        previousApplication = nil
        purpose = nil
    }
}
