import AppKit

extension DisplayInfo {
    @MainActor static func from(_ screen: NSScreen) -> DisplayInfo? {
        guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else {
            return nil
        }
        return DisplayInfo(displayID: CGDirectDisplayID(truncating: number),
                           frame: screen.frame, backingScale: screen.backingScaleFactor)
    }

    @MainActor static func allCurrent() -> [DisplayInfo] {
        NSScreen.screens.compactMap(from)
    }
}
