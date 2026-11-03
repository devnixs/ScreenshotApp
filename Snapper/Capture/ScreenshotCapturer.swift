import Foundation
import ScreenCaptureKit
import os

enum ScreenshotCaptureError: LocalizedError {
    case displayUnavailable
    case windowUnavailable
    var errorDescription: String? {
        switch self {
        case .displayUnavailable: "The display is no longer available."
        case .windowUnavailable: "The window is no longer available."
        }
    }
}

@MainActor
final class ScreenshotCapturer {
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "screenshot")

    func captureWindow(windowID: CGWindowID) async throws -> CGImage {
        try await captureWindowImage(windowID: windowID).cgImage
    }

    /// Preserve SCK's scale alongside pixels for PNG resolution metadata and later annotation.
    func captureWindowImage(windowID: CGWindowID) async throws -> CapturedImage {
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
            throw ScreenshotCaptureError.windowUnavailable
        }
        let filter = SCContentFilter(desktopIndependentWindow: window)
        let scale = CGFloat(filter.pointPixelScale)
        let configuration = SCStreamConfiguration()
        configuration.width = Int((filter.contentRect.width * scale).rounded())
        configuration.height = Int((filter.contentRect.height * scale).rounded())
        configuration.showsCursor = false
        configuration.ignoreShadowsSingleWindow = true
        configuration.ignoreGlobalClipSingleWindow = true
        configuration.captureResolution = .best
        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
        return CapturedImage(cgImage: image, pixelsPerPoint: scale)
    }

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
