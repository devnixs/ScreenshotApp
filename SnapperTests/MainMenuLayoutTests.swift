import Testing
@testable import Snapper

struct MainMenuLayoutTests {
    @Test func editCommandsRouteStandardShortcuts() {
        let commands = MainMenuLayout.edit.compactMap { $0 }
        let expected: [(String, String, MenuModifiers)] = [
            ("undo:", "z", .command), ("redo:", "z", [.command, .shift]),
            ("cut:", "x", .command), ("copy:", "c", .command),
            ("paste:", "v", .command), ("selectAll:", "a", .command)
        ]
        for (action, key, modifiers) in expected {
            #expect(commands.contains { $0.action == action && $0.keyEquivalent == key && $0.modifiers == modifiers })
        }
        #expect(commands.contains { $0.action == "delete:" && $0.keyEquivalent.isEmpty })
    }

    @Test func fileCommandsRouteSaveAndClose() {
        #expect(MainMenuLayout.file.compactMap { $0 } == [
            MenuCommandSpec(title: "Close", keyEquivalent: "w", modifiers: .command, action: "performClose:"),
            MenuCommandSpec(title: "Save…", keyEquivalent: "s", modifiers: .command, action: "saveDocument:"),
            MenuCommandSpec(title: "Copy & Close", keyEquivalent: "\r", modifiers: .command, action: "copyAndClose:")
        ])
    }

    @Test func appCommandsRouteSettingsAndQuit() {
        let commands = MainMenuLayout.app.compactMap { $0 }
        #expect(commands.contains {
            $0.title == "Settings…" && $0.keyEquivalent == "," && $0.modifiers == .command && $0.action == "openSettings:"
        })
        #expect(commands.contains {
            $0.action == "terminate:" && $0.keyEquivalent == "q" && $0.modifiers == .command
        })
    }

    @Test func windowCommandsRouteMinimizeAndZoom() {
        #expect(MainMenuLayout.window.compactMap { $0 } == [
            MenuCommandSpec(title: "Minimize", keyEquivalent: "m", modifiers: .command, action: "performMiniaturize:"),
            MenuCommandSpec(title: "Zoom", keyEquivalent: "", modifiers: [], action: "performZoom:")
        ])
    }

    @Test func shortcutsHaveNoDuplicateBindings() {
        let commands = (MainMenuLayout.app + MainMenuLayout.file + MainMenuLayout.edit + MainMenuLayout.window)
            .compactMap { $0 }.filter { !$0.keyEquivalent.isEmpty }
        for (index, command) in commands.enumerated() {
            #expect(!commands.dropFirst(index + 1).contains {
                $0.keyEquivalent == command.keyEquivalent && $0.modifiers == command.modifiers
            })
        }
    }
}
