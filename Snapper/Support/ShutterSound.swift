import AppKit
import os

enum ShutterSoundSource: Equatable {
    case file(URL), named(String)
}

struct ShutterSoundResolver {
    static let systemSoundURL = URL(fileURLWithPath: "/System/Library/Components/CoreAudio.component/Contents/SharedSupport/SystemSounds/system/Screen Capture.aif")

    static func resolve(fileExists: (URL) -> Bool, namedSoundExists: (String) -> Bool) -> ShutterSoundSource? {
        if fileExists(systemSoundURL) { return .file(systemSoundURL) }
        if namedSoundExists("Grab") { return .named("Grab") }
        if namedSoundExists("Pop") { return .named("Pop") }
        return nil
    }
}

@MainActor
final class ShutterSound {
    private var resolved = false
    private var sound: NSSound?
    private let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "screenshot")

    func play() {
        if !resolved {
            resolved = true
            switch ShutterSoundResolver.resolve(fileExists: { FileManager.default.fileExists(atPath: $0.path) },
                                                namedSoundExists: { NSSound(named: $0) != nil }) {
            case .file(let url): sound = NSSound(contentsOf: url, byReference: true)
            case .named(let name): sound = NSSound(named: name)
            case nil: break
            }
            if sound == nil { logger.warning("No shutter sound available") }
        }
        sound?.stop()
        sound?.play()
    }
}
