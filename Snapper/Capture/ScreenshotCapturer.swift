import Foundation
import ScreenCaptureKit
import os

enum ScreenshotCaptureError: LocalizedError {
    case displayUnavailable
    var errorDescription: String? { "The display is no longer available." }
}

@MainActor
final class ScreenshotCapturer {
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "screenshot")

    func capture(_ region: CaptureRegion, pixelsPerPoint: CGFloat) async throws -> CapturedImage {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let display = content.displays.first(where: { $0.displayID == region.displayID }) else {
            throw ScreenshotCaptureError.displayUnavailable
        }
        let ourApp = content.applications.first { $0.processID == getpid() }
            ?? content.applications.first { $0.bundleIdentifier == Bundle.main.bundleIdentifier }
        if ourApp == nil { logger.warning("Snapper was absent from shareable applications; overlay is already hidden") }
        let filter = SCContentFilter(display: display, excludingApplications: ourApp.map { [$0] } ?? [], exceptingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.sourceRect = region.sourceRect
        configuration.width = region.pixelSize.width
        configuration.height = region.pixelSize.height
        configuration.showsCursor = false
        configuration.scalesToFit = false
        configuration.colorSpaceName = CGColorSpace.sRGB
        configuration.capturesAudio = false
        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
        return CapturedImage(cgImage: image, pixelsPerPoint: pixelsPerPoint)
    }
}
