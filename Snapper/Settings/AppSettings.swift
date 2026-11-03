import Foundation
import Observation

@Observable @MainActor
final class AppSettings {
    enum Key: String, CaseIterable {
        case launchAtLogin, saveFolderPath, screenshotDestination, playShutterSound
        case recordMicrophone, onboardingDismissed, annotationColor, annotationStrokeWidth
        case recordingFormat, mp4Quality, gifQuality
    }

    enum Defaults {
        static let launchAtLogin = false
        static var saveFolderURL: URL { SaveFolder.defaultURL }
        static let screenshotDestination: ScreenshotDestination = .both
        static let playShutterSound = true
        static let recordingFormat: RecordingFormat = .mp4
        static let mp4Quality: RecordingQuality = .high
        static let gifQuality: RecordingQuality = .medium
        static let recordMicrophone = false
        static let onboardingDismissed = false
        static let annotationColor: RGBAColor = .red
        static let annotationStrokeWidth: StrokeWidth = .m
    }

    @ObservationIgnored private let defaults: UserDefaults

    var launchAtLogin: Bool {
        didSet { defaults.set(launchAtLogin, forKey: Key.launchAtLogin.rawValue) }
    }
    var saveFolderURL: URL {
        didSet {
            guard saveFolderURL.isFileURL else {
                assertionFailure("Save folder must be a file URL")
                return
            }
            defaults.set(saveFolderURL.standardizedFileURL.path, forKey: Key.saveFolderPath.rawValue)
        }
    }
    var screenshotDestination: ScreenshotDestination {
        didSet { defaults.set(screenshotDestination.rawValue, forKey: Key.screenshotDestination.rawValue) }
    }
    var playShutterSound: Bool {
        didSet { defaults.set(playShutterSound, forKey: Key.playShutterSound.rawValue) }
    }
    var recordingFormat: RecordingFormat {
        didSet { defaults.set(recordingFormat.rawValue, forKey: Key.recordingFormat.rawValue) }
    }
    var mp4Quality: RecordingQuality {
        didSet { defaults.set(mp4Quality.rawValue, forKey: Key.mp4Quality.rawValue) }
    }
    var gifQuality: RecordingQuality {
        didSet { defaults.set(gifQuality.rawValue, forKey: Key.gifQuality.rawValue) }
    }
    var recordMicrophone: Bool {
        didSet { defaults.set(recordMicrophone, forKey: Key.recordMicrophone.rawValue) }
    }
    var onboardingDismissed: Bool {
        didSet { defaults.set(onboardingDismissed, forKey: Key.onboardingDismissed.rawValue) }
    }
    var annotationColor: RGBAColor {
        didSet {
            // RGBAColor guarantees finite components, so JSON encoding cannot fail.
            guard let data = try? JSONEncoder().encode(annotationColor) else {
                assertionFailure("Valid RGBAColor must encode")
                return
            }
            defaults.set(data, forKey: Key.annotationColor.rawValue)
        }
    }
    var annotationStrokeWidth: StrokeWidth {
        didSet { defaults.set(annotationStrokeWidth.rawValue, forKey: Key.annotationStrokeWidth.rawValue) }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        launchAtLogin = Self.decodeBool(.launchAtLogin, in: defaults, default: Defaults.launchAtLogin)
        saveFolderURL = Self.decodeSaveFolder(in: defaults)
        screenshotDestination = Self.decodeEnum(.screenshotDestination, in: defaults, default: Defaults.screenshotDestination)
        playShutterSound = Self.decodeBool(.playShutterSound, in: defaults, default: Defaults.playShutterSound)
        recordingFormat = Self.decodeEnum(.recordingFormat, in: defaults, default: Defaults.recordingFormat)
        mp4Quality = Self.decodeEnum(.mp4Quality, in: defaults, default: Defaults.mp4Quality)
        gifQuality = Self.decodeEnum(.gifQuality, in: defaults, default: Defaults.gifQuality)
        recordMicrophone = Self.decodeBool(.recordMicrophone, in: defaults, default: Defaults.recordMicrophone)
        onboardingDismissed = Self.decodeBool(.onboardingDismissed, in: defaults, default: Defaults.onboardingDismissed)
        annotationColor = Self.decodeColor(in: defaults)
        annotationStrokeWidth = Self.decodeEnum(.annotationStrokeWidth, in: defaults, default: Defaults.annotationStrokeWidth)
    }

    private static func decodeBool(_ key: Key, in defaults: UserDefaults, default fallback: Bool) -> Bool {
        defaults.object(forKey: key.rawValue) as? Bool ?? fallback
    }

    private static func decodeEnum<T: RawRepresentable>(_ key: Key, in defaults: UserDefaults, default fallback: T) -> T
    where T.RawValue == String {
        guard let raw = defaults.object(forKey: key.rawValue) as? String else { return fallback }
        return T(rawValue: raw) ?? fallback
    }

    private static func decodeSaveFolder(in defaults: UserDefaults) -> URL {
        guard let path = defaults.object(forKey: Key.saveFolderPath.rawValue) as? String,
              path.hasPrefix("/") else { return Defaults.saveFolderURL }
        return URL(filePath: path, directoryHint: .isDirectory).standardizedFileURL
    }

    private static func decodeColor(in defaults: UserDefaults) -> RGBAColor {
        guard let data = defaults.object(forKey: Key.annotationColor.rawValue) as? Data,
              let color = try? JSONDecoder().decode(RGBAColor.self, from: data) else { return Defaults.annotationColor }
        return color
    }
}
