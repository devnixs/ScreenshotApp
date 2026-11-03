import Foundation

enum SaveFailureAlert: Equatable { case saveFolder, generic }

struct ScreenshotOutputPlan {
    let copiesToClipboard: Bool
    let savesToFile: Bool

    init(destination: ScreenshotDestination) {
        copiesToClipboard = destination.copiesToClipboard
        savesToFile = destination.savesToFile
    }

    static func saveFailureAlert(for error: Error) -> SaveFailureAlert {
        if error is SaveFolderError { return .saveFolder }
        var current = error as NSError
        // Bound traversal in case a malformed error contains a cycle.
        for _ in 0..<16 {
            if current.domain == NSCocoaErrorDomain,
               [CocoaError.fileWriteNoPermission.rawValue, CocoaError.fileWriteVolumeReadOnly.rawValue,
                CocoaError.fileNoSuchFile.rawValue].contains(current.code) { return .saveFolder }
            if current.domain == NSPOSIXErrorDomain, [Int(EACCES), Int(EPERM), Int(EROFS)].contains(current.code) {
                return .saveFolder
            }
            guard let underlying = current.userInfo[NSUnderlyingErrorKey] as? NSError else { break }
            current = underlying
        }
        return .generic
    }

    static func folderDisplayName(_ url: URL) -> String {
        (url.path as NSString).abbreviatingWithTildeInPath
    }
}
