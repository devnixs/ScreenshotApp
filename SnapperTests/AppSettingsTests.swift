import Foundation
import Observation
import Testing
@testable import Snapper

@MainActor
struct AppSettingsTests {
    private func withDefaults(_ body: (UserDefaults) throws -> Void) rethrows {
        let suite = "SnapperTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        try body(defaults)
    }

    @Test func defaultsOnEmptySuite() {
        withDefaults { defaults in
            let settings = AppSettings(defaults: defaults)
            #expect(!settings.launchAtLogin)
            #expect(settings.saveFolderURL.path == SaveFolder.defaultURL.path)
            #expect(settings.screenshotDestination == .both)
            #expect(settings.playShutterSound)
            #expect(!settings.recordMicrophone)
            #expect(!settings.onboardingDismissed)
            #expect(settings.annotationColor == .red)
            #expect(settings.annotationStrokeWidth == .m)
            #expect(AppSettings.Key.allCases.allSatisfy { defaults.object(forKey: $0.rawValue) == nil })
        }
    }

    @Test func initWritesNothing() {
        let suite = "SnapperTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        _ = AppSettings(defaults: defaults)
        #expect(defaults.persistentDomain(forName: suite)?.isEmpty ?? true)
    }

    private func roundTrip<T: Equatable>(_ keyPath: ReferenceWritableKeyPath<AppSettings, T>, _ value: T) {
        withDefaults { defaults in
            let first = AppSettings(defaults: defaults)
            first[keyPath: keyPath] = value
            #expect(AppSettings(defaults: defaults)[keyPath: keyPath] == value)
        }
    }

    @Test func launchAtLoginRoundTrip() { roundTrip(\.launchAtLogin, true) }
    @Test func saveFolderRoundTrip() { roundTrip(\.saveFolderURL, URL(filePath: "/tmp/Snapper Test Folder", directoryHint: .isDirectory)) }
    @Test func destinationRoundTrip() {
        for value in [ScreenshotDestination.file, .clipboard] { roundTrip(\.screenshotDestination, value) }
    }
    @Test func shutterSoundRoundTrip() { roundTrip(\.playShutterSound, false) }
    @Test func microphoneRoundTrip() { roundTrip(\.recordMicrophone, true) }
    @Test func onboardingRoundTrip() { roundTrip(\.onboardingDismissed, true) }
    @Test func colorRoundTrip() {
        for value in [RGBAColor.blue, RGBAColor(red: 0.1, green: 0.2, blue: 0.3, alpha: 0.5)] {
            roundTrip(\.annotationColor, value)
        }
    }
    @Test func widthRoundTrip() {
        for value in [StrokeWidth.s, .l] { roundTrip(\.annotationStrokeWidth, value) }
    }

    @Test func settingValueWritesStoredFormat() throws {
        try withDefaults { defaults in
            let settings = AppSettings(defaults: defaults)
            settings.launchAtLogin = true
            settings.saveFolderURL = URL(filePath: "/tmp/a/../b")
            settings.screenshotDestination = .clipboard
            settings.playShutterSound = false
            settings.recordMicrophone = true
            settings.onboardingDismissed = true
            settings.annotationColor = .blue
            settings.annotationStrokeWidth = .l
            #expect(defaults.object(forKey: "saveFolderPath") as? String == "/tmp/b")
            #expect(defaults.object(forKey: "screenshotDestination") as? String == "clipboard")
            #expect(defaults.object(forKey: "annotationStrokeWidth") as? String == "l")
            let data = try #require(defaults.object(forKey: "annotationColor") as? Data)
            #expect(try JSONDecoder().decode(RGBAColor.self, from: data) == .blue)
            for (key, value) in [("launchAtLogin", true), ("playShutterSound", false),
                                 ("recordMicrophone", true), ("onboardingDismissed", true)] {
                #expect(defaults.object(forKey: key) as? Bool == value)
            }
        }
    }

    @Test func invalidBoolsFallBackAndNumbersBridge() {
        withDefaults { defaults in
            for garbage: Any in ["nope", 5] {
                defaults.set(garbage, forKey: "playShutterSound")
                #expect(AppSettings(defaults: defaults).playShutterSound)
                #expect(defaults.object(forKey: "playShutterSound") != nil)
            }
            defaults.set("yes", forKey: "launchAtLogin")
            #expect(!AppSettings(defaults: defaults).launchAtLogin)
            defaults.set(NSNumber(value: 0), forKey: "playShutterSound")
            #expect(!AppSettings(defaults: defaults).playShutterSound)
            defaults.set(NSNumber(value: 1), forKey: "launchAtLogin")
            #expect(AppSettings(defaults: defaults).launchAtLogin)
        }
    }

    @Test func invalidEnumsFallBack() {
        withDefaults { defaults in
            for garbage: Any in ["printer", 42] {
                defaults.set(garbage, forKey: "screenshotDestination")
                #expect(AppSettings(defaults: defaults).screenshotDestination == .both)
            }
            for garbage in ["xl", "M"] {
                defaults.set(garbage, forKey: "annotationStrokeWidth")
                #expect(AppSettings(defaults: defaults).annotationStrokeWidth == .m)
            }
        }
    }

    @Test func invalidSaveFoldersFallBack() {
        withDefaults { defaults in
            for garbage: Any in ["", "Documents/Screenshots", "~/Pictures", 42] {
                defaults.set(garbage, forKey: "saveFolderPath")
                #expect(AppSettings(defaults: defaults).saveFolderURL == SaveFolder.defaultURL)
            }
        }
    }

    @Test func invalidColorsFallBackWithoutRewriting() {
        withDefaults { defaults in
            let invalid: [Any] = [Data("garbage".utf8), "red",
                Data(#"{"red":2,"green":0,"blue":0,"alpha":1}"#.utf8),
                Data(#"{"red":0,"green":0,"blue":0}"#.utf8)]
            for garbage in invalid {
                defaults.set(garbage, forKey: "annotationColor")
                #expect(AppSettings(defaults: defaults).annotationColor == .red)
                #expect(defaults.object(forKey: "annotationColor") as? NSObject == garbage as? NSObject)
            }
        }
    }

    @Test func keyRawValues() {
        let keys = AppSettings.Key.allCases.map(\.rawValue)
        #expect(keys == ["launchAtLogin", "saveFolderPath", "screenshotDestination", "playShutterSound",
                         "recordMicrophone", "onboardingDismissed", "annotationColor", "annotationStrokeWidth"])
        #expect(Set(keys).count == keys.count)
    }

    @Test func destinationValuesFlagsAndTitles() {
        #expect(ScreenshotDestination.allCases.map(\.rawValue) == ["file", "clipboard", "both"])
        #expect(ScreenshotDestination.allCases.map(\.savesToFile) == [true, false, true])
        #expect(ScreenshotDestination.allCases.map(\.copiesToClipboard) == [false, true, true])
        #expect(ScreenshotDestination.allCases.map(\.title) == ["Save to file", "Copy to clipboard", "Both"])
    }
}
