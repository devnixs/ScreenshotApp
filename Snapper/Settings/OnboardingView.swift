import SwiftUI

@MainActor
struct OnboardingView: View {
    let model: OnboardingModel
    let dismissPermanently: () -> Void
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text("Set up Snapper").font(.largeTitle.bold())
            Text("Two quick steps to start capturing your screen.")
                .foregroundStyle(.secondary)
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    stepTitle("1. Allow Screen Recording", done: model.status.isPermissionStepDone)
                    Text("Snapper needs Screen Recording permission to capture your screen.")
                    Button("Open System Settings") {
                        ScreenRecordingPermission.request()
                        SystemSettingsLinks.open(SystemSettingsLinks.screenRecording)
                    }
                    if !model.status.isPermissionStepDone {
                        Text("After granting access, macOS may ask you to quit and reopen Snapper.")
                            .font(.callout).foregroundStyle(.secondary)
                    }
                }.frame(maxWidth: .infinity, alignment: .leading).padding(8)
            }
            GroupBox {
                VStack(alignment: .leading, spacing: 10) {
                    stepTitle("2. Turn off macOS screenshot shortcuts", done: model.status.isShortcutStepDone)
                    if model.status.isShortcutStepDone {
                        Text("No conflicts with macOS shortcuts.")
                    } else {
                        Text("In Keyboard Shortcuts… → Screenshots, turn off the shortcuts that Snapper uses:")
                        ForEach(Array(model.status.collisions.enumerated()), id: \.offset) { _, collision in
                            Text("\(collision.system.chord.displayString): \(SystemScreenshotHotKeyID(rawValue: collision.system.id)?.actionName ?? "Screenshot shortcut")")
                                .font(.callout)
                        }
                    }
                    Button("Open System Settings") {
                        SystemSettingsLinks.open(SystemSettingsLinks.keyboardShortcuts)
                    }
                }.frame(maxWidth: .infinity, alignment: .leading).padding(8)
            }
            HStack {
                Button("Don't show again", action: dismissPermanently)
                Spacer()
                Button("Done", action: close).keyboardShortcut(.defaultAction)
            }
        }.padding(24).frame(width: 540)
    }

    private func stepTitle(_ title: String, done: Bool) -> some View {
        HStack {
            Text(title).font(.headline)
            Spacer()
            Label(done ? "Done" : "Needed", systemImage: done ? "checkmark.circle.fill" : "exclamationmark.circle.fill")
                .foregroundStyle(done ? Color.green : Color.orange)
        }
    }
}
