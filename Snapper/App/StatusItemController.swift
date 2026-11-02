import AppKit

@MainActor
final class StatusItemController: NSObject {
    private weak var coordinator: AppCoordinator?
    private let statusItem: NSStatusItem
    private let menu = NSMenu()
    private var menuItems: [(entry: StatusMenuEntry, item: NSMenuItem)] = []

    init(coordinator: AppCoordinator) {
        self.coordinator = coordinator
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()
        for entry in StatusMenuLayout.entries {
            guard let title = entry.title else {
                menu.addItem(.separator())
                continue
            }
            let item = NSMenuItem(title: title, action: #selector(performMenuAction(_:)), keyEquivalent: "")
            item.target = self
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
            button.sendAction(on: [.leftMouseDown, .rightMouseDown])
        }
    }

    func item(for entry: StatusMenuEntry) -> NSMenuItem? {
        menuItems.first { $0.entry == entry }?.item
    }

    @objc private func handleClick(_ sender: Any?) {
        let event = NSApp.currentEvent
        let button: StatusItemMouseButton?
        switch event?.type {
        case .leftMouseDown: button = .left
        case .rightMouseDown: button = .right
        default: button = nil
        }
        switch StatusItemClickRouter.action(
            button: button, controlKeyDown: event?.modifierFlags.contains(.control) ?? false
        ) {
        case .showMenu:
            statusItem.menu = menu
            statusItem.button?.performClick(nil)
            statusItem.menu = nil
        case .openSettings:
            coordinator?.openSettings()
        }
    }

    @objc private func performMenuAction(_ sender: NSMenuItem) {
        guard let entry = menuItems.first(where: { $0.item === sender })?.entry else { return }
        switch entry {
        case .screenshot: coordinator?.startScreenshot()
        case .record: coordinator?.startRecording()
        case .annotate: coordinator?.startAnnotate()
        case .settings: coordinator?.openSettings()
        case .quit: coordinator?.quit()
        case .separator: break
        }
    }
}
