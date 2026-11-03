import AppKit

@MainActor
final class WindowFrameTracker {
    private let windowID: CGWindowID
    private var state: WindowTrackingState
    private let onEvent: @MainActor (WindowTrackingEvent) -> Void
    private var timer: Timer?

    init(windowID: CGWindowID, initialFrame: CGRect, onEvent: @escaping @MainActor (WindowTrackingEvent) -> Void) {
        self.windowID = windowID
        state = WindowTrackingState(appKitFrame: initialFrame)
        self.onEvent = onEvent
    }

    static func poll(windowID: CGWindowID) -> WindowPollResult {
        WindowInfoParser.parse(CGWindowListCopyWindowInfo([.optionIncludingWindow], windowID) as? [[String: Any]],
                               windowID: windowID)
    }

    func start() {
        guard timer == nil, !state.isClosed else { return }
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            // This timer is installed exclusively on the main run loop.
            MainActor.assumeIsolated { self?.tick() }
        }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
        tick()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    private func tick() {
        guard let height = DisplayGeometry.primaryHeight(in: DisplayInfo.allCurrent()) else { return }
        let (next, event) = WindowTrackingReducer.reduce(state, poll: Self.poll(windowID: windowID), primaryDisplayHeight: height)
        state = next
        if event == .closed { stop() }
        if event != .unchanged { onEvent(event) }
    }
}
