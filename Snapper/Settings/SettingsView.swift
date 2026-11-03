import SwiftUI
import KeyboardShortcuts

@MainActor
struct SettingsView: View {
    @Bindable var model: SettingsModel

    var body: some View {
        TabView {
            general.tabItem { Label("General", systemImage: "gearshape") }
            shortcuts.tabItem { Label("Shortcuts", systemImage: "keyboard") }
            recording.tabItem { Label("Recording", systemImage: "video") }
        }
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
        .onAppear { model.refreshOnAppear() }
    }

    private var general: some View {
        Form {
            Section {
                Toggle("Launch at login", isOn: Binding(get: { model.launchAtLogin.isOn }, set: { model.setLaunchAtLogin($0) }))
                if let note = model.launchAtLogin.note {
                    switch note {
                    case .requiresApproval:
                        Text("Allow Snapper in System Settings to launch it at login.").foregroundStyle(.secondary)
                        Button("Open Login Items", action: model.openLoginItems)
                    case .unavailable:
                        Text("Launch at login isn't available for this copy of Snapper.").foregroundStyle(.secondary)
                    case .failed(let message):
                        Text("Couldn't change launch at login: \(message)").foregroundStyle(.secondary)
                    }
                }
            }
            Section("Save folder") {
                Text((model.saveFolderURL.path as NSString).abbreviatingWithTildeInPath)
                    .lineLimit(1).truncationMode(.middle).help(model.saveFolderURL.path)
                HStack {
                    Button("Choose…", action: model.chooseSaveFolder)
                    Button("Show in Finder", action: model.showSaveFolder)
                }
            }
            Section {
                Picker("Screenshot destination", selection: $model.screenshotDestination) {
                    Text("Save to file").tag(ScreenshotDestination.file)
                    Text("Copy to clipboard").tag(ScreenshotDestination.clipboard)
                    Text("Both").tag(ScreenshotDestination.both)
                }.pickerStyle(.radioGroup)
                Toggle("Play shutter sound", isOn: $model.playShutterSound)
            }
        }.formStyle(.grouped)
    }

    private var shortcuts: some View {
        Form {
            ForEach(CapturePurpose.allCases, id: \.self) { purpose in
                Section {
                    KeyboardShortcuts.Recorder("\(purpose.rawValue.capitalized):", name: purpose.shortcutName,
                                               onChange: { _ in model.refreshConflicts() })
                        .shortcutValidation { candidate in
                            if let owner = model.duplicateOwner(of: candidate, for: purpose) {
                                return .disallow(reason: ShortcutConflictDetector.duplicateReason(owner: owner))
                            }
                            return .allow
                        }
                    ForEach(Array(model.conflicts.filter { $0.purpose == purpose }.enumerated()), id: \.offset) { _, conflict in
                        Label {
                            Text(ShortcutConflictDetector.warningText(for: conflict)).foregroundStyle(.secondary)
                        } icon: {
                            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                        }.font(.callout)
                    }
                }
            }
            if !model.conflicts.isEmpty {
                Button("Open Keyboard Shortcuts", action: model.openKeyboardShortcuts)
            }
        }.formStyle(.grouped)
    }

    private var recording: some View {
        Form {
            Picker("MP4 quality", selection: $model.mp4Quality) {
                ForEach(RecordingQuality.allCases, id: \.self) { quality in
                    Text(quality.title).tag(quality)
                }
            }.pickerStyle(.radioGroup)
            Text("Medium and Low re-encode after you stop, for smaller files.").foregroundStyle(.secondary)
            Toggle("Record microphone", isOn: Binding(get: { model.recordMicrophone }, set: { model.setRecordMicrophone($0) }))
            if model.showsMicrophoneDeniedNote {
                Text("Snapper doesn't have access to the microphone.").foregroundStyle(.secondary)
                Button("Open System Settings", action: model.openMicrophoneSettings)
            }
        }.formStyle(.grouped)
    }
}
