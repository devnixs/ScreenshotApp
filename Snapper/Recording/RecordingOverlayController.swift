import AppKit

@MainActor
final class RecordingOverlayController {
    private let display: DisplayInfo
    private var recordedRect: CGRect
    private let startedAt: ContinuousClock.Instant
    private let onStop: @MainActor () -> Void
    private let onCancel: @MainActor () -> Void
    private var border: RecordingBorderWindow?
    private var pill: RecordingPillPanel?
    private var timerTask: Task<Void, Never>?
    private var screenObserver: NSObjectProtocol?
    private var elapsedText = "0:00"
    private var dismissed = false

    init(display: DisplayInfo, recordedRect: CGRect, startedAt: ContinuousClock.Instant,
         onStop: @escaping @MainActor () -> Void, onCancel: @escaping @MainActor () -> Void) {
        self.display = display
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
        guard !dismissed, border == nil, screen != nil else { return }
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
                self.update(recordedRect: self.recordedRect)
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

    func update(recordedRect: CGRect) {
        self.recordedRect = recordedRect
        guard !dismissed, let screen, let pill else { return }
        border?.update(frame: RecordingBorderGeometry.windowFrame(recordedRect: recordedRect, displayFrame: screen.frame))
        let frame = RecordingPillPlacement.place(pillSize: pill.pillSize, recordedRect: recordedRect,
                                                 visibleFrame: screen.visibleFrame).frame
        pill.setFrame(frame, display: true)
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
        update(recordedRect: recordedRect)
    }

    private func finish(cancel: Bool) {
        guard !dismissed else { return }
        dismiss()
        if cancel { onCancel() } else { onStop() }
    }
}
