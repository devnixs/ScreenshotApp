import SwiftUI

@MainActor
struct SettingsView: View {
    var body: some View {
        VStack(spacing: 12) {
            Text("Snapper Settings").font(.title2)
            Text("Settings will appear here.").foregroundStyle(.secondary)
        }
        .padding()
        .frame(width: 420, height: 200)
    }
}
