import AppKit

@MainActor
private final class PillButton: NSButton {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

@MainActor
final class RecordingPillPanel: NSPanel {
    private let elapsedLabel = NSTextField(labelWithString: "0:00")
    private let stopButton = PillButton(title: "Stop", target: nil, action: nil)
    private let cancelButton = PillButton(title: "Cancel", target: nil, action: nil)
    private let stack = NSStackView()
    var onStop: (@MainActor () -> Void)?
    var onCancel: (@MainActor () -> Void)?
    var pillSize: CGSize { CGSize(width: ceil(stack.fittingSize.width), height: 32) }
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    init() {
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .statusBar
        becomesKeyOnlyIfNeeded = true
        hidesOnDeactivate = false
        isMovable = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        let hud = NSVisualEffectView()
        hud.material = .hudWindow
        hud.blendingMode = .behindWindow
        hud.state = .active
        hud.appearance = NSAppearance(named: .darkAqua)
        hud.wantsLayer = true
        hud.layer?.cornerRadius = 16
        hud.layer?.masksToBounds = true
        contentView = hud
        elapsedLabel.font = .monospacedDigitSystemFont(ofSize: 13, weight: .medium)
        elapsedLabel.textColor = .white
        stack.orientation = .horizontal
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 6, left: 14, bottom: 6, right: 14)
        for view in [elapsedLabel, stopButton, cancelButton] { stack.addArrangedSubview(view) }
        for button in [stopButton, cancelButton] {
            button.bezelStyle = .inline
            button.controlSize = .small
            button.target = self
        }
        stopButton.action = #selector(stopClicked)
        cancelButton.action = #selector(cancelClicked)
        stack.translatesAutoresizingMaskIntoConstraints = false
        hud.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: hud.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: hud.trailingAnchor),
            stack.centerYAnchor.constraint(equalTo: hud.centerYAnchor)
        ])
    }

    func setElapsedText(_ text: String) { elapsedLabel.stringValue = text }
    func disableButtons() {
        stopButton.isEnabled = false
        cancelButton.isEnabled = false
    }
    @objc private func stopClicked() { onStop?() }
    @objc private func cancelClicked() { onCancel?() }
}
