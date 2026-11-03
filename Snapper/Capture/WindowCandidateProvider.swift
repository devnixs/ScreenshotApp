import Foundation
import ScreenCaptureKit

@MainActor
struct WindowCandidateProvider {
    func loadCandidates() async throws -> [WindowCandidate] {
        let windows = try await Self.shareableWindows()
        let info = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
            as? [[String: Any]] ?? []
        var ids: [CGWindowID] = []
        var alpha: [CGWindowID: Double] = [:]
        for entry in info {
            guard let number = entry[kCGWindowNumber as String] as? NSNumber else { continue }
            let id = number.uint32Value
            ids.append(id)
            alpha[id] = (entry[kCGWindowAlpha as String] as? NSNumber)?.doubleValue
        }
        return WindowCandidateBuilder.build(windows: windows, frontToBackIDs: ids, alphaByID: alpha)
    }

    // SCK objects stay in this nonisolated operation; only Sendable values cross to UI.
    private nonisolated static func shareableWindows() async throws -> [WindowCandidate] {
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        return content.windows.map {
            WindowCandidate(windowID: $0.windowID, frame: $0.frame, layer: $0.windowLayer,
                            ownerPID: $0.owningApplication?.processID,
                            ownerBundleID: $0.owningApplication?.bundleIdentifier,
                            isOnScreen: $0.isOnScreen, alpha: 1, title: $0.title)
        }
    }
}
