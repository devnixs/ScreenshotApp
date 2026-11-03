import AppKit
import KeyboardShortcuts

@MainActor
final class StatusItemController: NSObject, NSMenuDelegate {
    private weak var coordinator: AppCoordinator?
    private let statusItem: NSStatusItem
    private let menu = NSMenu()
    private var menuItems: [(entry: StatusMenuEntry, item: NSMenuItem)] = []

    init(coordinator: AppCoordinator) {
        self.coordinator = coordinator
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()
        menu.delegate = self
        for entry in StatusMenuLayout.entries {
            guard let title = entry.title else {
                menu.addItem(.separator())
                continue
            }
            let item = NSMenuItem(title: title, action: #selector(performMenuAction(_:)), keyEquivalent: "")
            item.target = self
            if let purpose = entry.capturePurpose {
                item.setShortcut(for: purpose.shortcutName)
            }
            menuItems.append((entry, item))
            menu.addItem(item)
        }
        if let button = statusItem.button {
            let image = NSImage(systemSymbolName: "camera.viewfinder", accessibilityDescription: "Snapper")
            image?.isTemplate = true
            button.image = image
            button.toolTip = "Snapper"
            button.target = self
            button.action = #selector(handleClick(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }
    }

    func setRecording(_ isRecording: Bool) {
        statusItem.menu = nil
        guard let button = statusItem.button else { return }
        let image = NSImage(systemSymbolName: isRecording ? "record.circle" : "camera.viewfinder",
                            accessibilityDescription: isRecording ? "Stop Recording" : "Snapper")
        image?.isTemplate = !isRecording
        button.image = image
        button.contentTintColor = isRecording ? .systemRed : nil
        button.toolTip = isRecording ? "Stop Recording" : "Snapper"
    }

    func item(for entry: StatusMenuEntry) -> NSMenuItem? {
        menuItems.first { $0.entry == entry }?.item
    }

    func showMenu() {
        guard let button = statusItem.button else { return }
        statusItem.menu = menu
        button.performClick(nil)
    }

    func menuDidClose(_ menu: NSMenu) {
        statusItem.menu = nil
    }

    @objc private func handleClick(_ sender: Any?) {
        let event = NSApp.currentEvent
        let mouseButton: StatusItemButton?
        switch event?.type {
        case .leftMouseUp: mouseButton = .left
        case .rightMouseUp: mouseButton = .right
        default: mouseButton = nil
        }
        let button = StatusItemClickRouter.button(
            mouseButton: mouseButton, controlKeyDown: event?.modifierFlags.contains(.control) ?? false
        )
        coordinator?.send(.statusItemClicked(button))
    }

    @objc private func performMenuAction(_ sender: NSMenuItem) {
        guard let entry = menuItems.first(where: { $0.item === sender })?.entry else { return }
        switch entry {
        case .screenshot: coordinator?.send(.menuItem(.screenshot))
        case .record: coordinator?.send(.menuItem(.record))
        case .annotate: coordinator?.send(.menuItem(.annotate))
        case .settings: coordinator?.openSettings()
        case .quit: coordinator?.quit()
        case .separator: break
        }
    }
}
