import AppKit

@MainActor
final class RecordingOverlayController {
    private var display: DisplayInfo
    private var recordedRect: CGRect
    private let followsWindow: Bool
    private let startedAt: ContinuousClock.Instant
    private let onStop: @MainActor () -> Void
    private let onCancel: @MainActor () -> Void
    private var border: RecordingBorderWindow?
    private var pill: RecordingPillPanel?
    private var timerTask: Task<Void, Never>?
    private var screenObserver: NSObjectProtocol?
    private var elapsedText = "0:00"
    private var dismissed = false
    private var borderVisible = true

    init(display: DisplayInfo, recordedRect: CGRect, followsWindow: Bool = false, startedAt: ContinuousClock.Instant,
         onStop: @escaping @MainActor () -> Void, onCancel: @escaping @MainActor () -> Void) {
        self.display = display
        self.followsWindow = followsWindow
        self.recordedRect = recordedRect
        self.startedAt = startedAt
        self.onStop = onStop
        self.onCancel = onCancel
    }

    private var screen: NSScreen? {
        NSScreen.screens.first {
            ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == display.displayID
        }
    }

    func show() {
        guard !dismissed, border == nil else { return }
        // A window may have changed displays while the stream was starting.
        refreshDisplay()
        guard screen != nil else { return }
        border = RecordingBorderWindow()
        let pill = RecordingPillPanel()
        pill.onStop = { [weak self] in self?.finish(cancel: false) }
        pill.onCancel = { [weak self] in self?.finish(cancel: true) }
        self.pill = pill
        pill.setElapsedText(elapsedText)
        update(recordedRect: recordedRect)
        border?.orderFrontRegardless()
        pill.orderFrontRegardless()
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.refreshDisplay()
            }
        }
        timerTask = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                do { try await Task.sleep(for: .milliseconds(250)) }
                catch { return }
                self?.refreshElapsedTime()
            }
        }
    }

    func setBorderVisible(_ visible: Bool) {
        borderVisible = visible
        if visible { border?.orderFrontRegardless() } else { border?.orderOut(nil) }
    }

    func update(recordedRect: CGRect, on display: DisplayInfo? = nil) {
        if let display { self.display = display }
        self.recordedRect = recordedRect
        guard !dismissed, let screen, let pill else { return }
        // Window recordings can span displays; the border always encloses the whole window.
        border?.update(frame: RecordingBorderGeometry.windowFrame(recordedRect: recordedRect,
            displayFrame: screen.frame, followsWindow: followsWindow))
        let frame = RecordingPillPlacement.place(pillSize: pill.pillSize, recordedRect: recordedRect,
                                                 visibleFrame: screen.visibleFrame).frame
        pill.setFrame(frame, display: true)
    }

    private func refreshDisplay() {
        if followsWindow {
            let displays = DisplayInfo.allCurrent()
            if let current = DisplayGeometry.display(overlapping: recordedRect, in: displays) {
                display = current
            } else if !displays.contains(where: { $0.displayID == display.displayID }), let fallback = displays.first {
                display = fallback
            }
        }
        if borderVisible { update(recordedRect: recordedRect) }
        else if let pill, let screen {
            // Keep the last pill position unless a display change makes it unreachable.
            pill.setFrame(RecordingPillPlacement.reachableFrame(pill.frame, in: screen.visibleFrame), display: true)
        }
    }

    func dismiss() {
        guard !dismissed else { return }
        dismissed = true
        timerTask?.cancel()
        timerTask = nil
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
        screenObserver = nil
        pill?.disableButtons()
        pill?.close()
        border?.close()
        pill = nil
        border = nil
    }

    private func refreshElapsedTime() {
        let text = ElapsedTimeFormatter.string(from: startedAt.duration(to: .now))
        guard text != elapsedText else { return }
        elapsedText = text
        pill?.setElapsedText(text)
        refreshDisplay()
    }

    private func finish(cancel: Bool) {
        guard !dismissed else { return }
        dismiss()
        if cancel { onCancel() } else { onStop() }
    }
}
