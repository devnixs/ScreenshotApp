import Foundation
import Testing
@testable import Snapper

struct ShutterSoundResolverTests {
    @Test func systemFileWinsWithoutLookingUpNames() {
        #expect(ShutterSoundResolver.resolve(fileExists: { _ in true }, namedSoundExists: { _ in
            Issue.record("Named sound lookup should not run")
            return true
        }) == .file(ShutterSoundResolver.systemSoundURL))
    }
    @Test func fallbacks() {
        #expect(ShutterSoundResolver.resolve(fileExists: { _ in false }, namedSoundExists: { $0 == "Grab" }) == .named("Grab"))
        #expect(ShutterSoundResolver.resolve(fileExists: { _ in false }, namedSoundExists: { $0 == "Pop" }) == .named("Pop"))
        #expect(ShutterSoundResolver.resolve(fileExists: { _ in false }, namedSoundExists: { _ in false }) == nil)
    }
    @Test func exactSystemPath() {
        #expect(ShutterSoundResolver.systemSoundURL.path == "/System/Library/Components/CoreAudio.component/Contents/SharedSupport/SystemSounds/system/Screen Capture.aif")
    }
}
