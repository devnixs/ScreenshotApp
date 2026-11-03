import Foundation
import Testing
@testable import Snapper

struct ScreenshotOutputPlanTests {
    @Test func destinations() {
        for (destination, save, copy) in [(ScreenshotDestination.file, true, false), (.clipboard, false, true), (.both, true, true)] {
            let plan = ScreenshotOutputPlan(destination: destination)
            #expect(plan.savesToFile == save)
            #expect(plan.copiesToClipboard == copy)
        }
    }

    @Test func folderErrors() {
        let errors: [Error] = [SaveFolderError.notADirectory(URL(filePath: "/tmp/file")),
                               CocoaError(.fileWriteNoPermission), CocoaError(.fileWriteVolumeReadOnly),
                               CocoaError(.fileNoSuchFile)] + [EACCES, EPERM, EROFS].map {
            NSError(domain: NSPOSIXErrorDomain, code: Int($0))
        } + [NSError(domain: NSCocoaErrorDomain, code: CocoaError.fileWriteUnknown.rawValue,
                     userInfo: [NSUnderlyingErrorKey: NSError(domain: NSPOSIXErrorDomain, code: Int(EROFS))])]
        for error in errors { #expect(ScreenshotOutputPlan.saveFailureAlert(for: error) == .saveFolder) }
        for error in [CocoaError(.fileWriteOutOfSpace) as NSError, NSError(domain: "x", code: 1)] {
            #expect(ScreenshotOutputPlan.saveFailureAlert(for: error) == .generic)
        }
    }

    @Test func folderNames() {
        let home = FileManager.default.homeDirectoryForCurrentUser
        #expect(ScreenshotOutputPlan.folderDisplayName(home.appending(path: "Documents/Screenshots")) == "~/Documents/Screenshots")
        #expect(ScreenshotOutputPlan.folderDisplayName(URL(filePath: "/Volumes/Data/Shots")) == "/Volumes/Data/Shots")
    }
}
