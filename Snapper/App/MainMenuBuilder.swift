import AppKit

@MainActor
enum MainMenuBuilder {
    static func makeMainMenu(settingsTarget: AnyObject, settingsAction: Selector) -> NSMenu {
        let mainMenu = NSMenu()
        let layouts = [("Snapper", MainMenuLayout.app), ("File", MainMenuLayout.file),
                       ("Edit", MainMenuLayout.edit), ("Window", MainMenuLayout.window)]
        for (title, commands) in layouts {
            let menu = NSMenu(title: title)
            for command in commands {
                guard let command else {
                    menu.addItem(.separator())
                    continue
                }
                let item = NSMenuItem(title: command.title,
                                      action: NSSelectorFromString(command.action),
                                      keyEquivalent: command.keyEquivalent)
                var modifiers: NSEvent.ModifierFlags = []
                if command.modifiers.contains(.command) { modifiers.insert(.command) }
                if command.modifiers.contains(.shift) { modifiers.insert(.shift) }
                if command.modifiers.contains(.option) { modifiers.insert(.option) }
                if command.modifiers.contains(.control) { modifiers.insert(.control) }
                item.keyEquivalentModifierMask = modifiers
                if command.action == "openSettings:" {
                    item.target = settingsTarget
                    item.action = settingsAction
                }
                menu.addItem(item)
            }
            let parent = NSMenuItem(title: title, action: nil, keyEquivalent: "")
            parent.submenu = menu
            mainMenu.addItem(parent)
            if title == "Window" { NSApp.windowsMenu = menu }
        }
        return mainMenu
    }
}
