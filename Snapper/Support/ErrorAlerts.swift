import AppKit

@MainActor
enum ErrorAlerts {
    static func showCaptureFailure(_ error: Error) {
        showError(title: "Couldn't take screenshot", error: error)
    }

    static func showError(title: String, error: Error) {
        NSApp.activate()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = error.localizedDescription
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    static func showSaveFolderFailure(folder: URL, error: Error, settings: AppSettings) -> URL? {
        NSApp.activate()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Couldn't save to \(ScreenshotOutputPlan.folderDisplayName(folder))"
        alert.informativeText = error.localizedDescription
        alert.addButton(withTitle: "Choose Folder…")
        alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        let panel = folderPanel(directory: folder)
        guard panel.runModal() == .OK, let chosen = panel.url else { return nil }
        settings.saveFolderURL = chosen
        return chosen
    }

    static func chooseFolder(current: URL, on window: NSWindow, completion: @escaping @MainActor (URL) -> Void) {
        let panel = folderPanel(directory: current)
        panel.beginSheetModal(for: window) { response in
            if response == .OK, let chosen = panel.url { completion(chosen) }
        }
    }

    private static func folderPanel(directory folder: URL) -> NSOpenPanel {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        panel.prompt = "Choose"
        var directory = folder
        var isDirectory: ObjCBool = false
        while directory.path != "/" {
            if FileManager.default.fileExists(atPath: directory.path, isDirectory: &isDirectory), isDirectory.boolValue { break }
            directory.deleteLastPathComponent()
        }
        panel.directoryURL = directory
        return panel
    }
}
