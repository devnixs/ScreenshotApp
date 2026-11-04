import Foundation
import Testing

/// Regression guards for the Icon Composer app icon (`Snapper/AppIcon.icon`) and its wiring into
/// the built app. The icon is a static resource, so these check the source document and the host
/// bundle rather than any product logic.
struct AppIconTests {
    private static let iconURL = URL(filePath: #filePath)
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .appending(path: "Snapper/AppIcon.icon")
    private static let assetsURL = iconURL.appending(path: "Assets")

    private func iconDocument() throws -> [String: Any] {
        let data = try Data(contentsOf: Self.iconURL.appending(path: "icon.json"))
        return try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private func layerImageNames() throws -> Set<String> {
        let groups = try #require(try iconDocument()["groups"] as? [[String: Any]])
        let layers = groups.flatMap { $0["layers"] as? [[String: Any]] ?? [] }
        return Set(layers.compactMap { $0["image-name"] as? String })
    }

    private func svgFileNames() throws -> Set<String> {
        let files = try FileManager.default.contentsOfDirectory(atPath: Self.assetsURL.path(percentEncoded: false))
        return Set(files.filter { $0.hasSuffix(".svg") })
    }

    private func svgSource(_ name: String) throws -> String {
        try String(contentsOf: Self.assetsURL.appending(path: name), encoding: .utf8)
    }

    @Test func iconDocumentDeclaresMacOS() throws {
        let platforms = try #require(try iconDocument()["supported-platforms"] as? [String: Any])
        let squares = try #require(platforms["squares"] as? [String])
        #expect(squares.contains("macOS"))
    }

    @Test func everyLayerImageExists() throws {
        let names = try layerImageNames()
        #expect(names == ["brackets.svg", "lens.svg"])
        for name in names {
            let path = Self.assetsURL.appending(path: name).path(percentEncoded: false)
            #expect(FileManager.default.fileExists(atPath: path), "Missing \(name)")
        }
    }

    @Test func assetsFolderHasNoOrphans() throws {
        #expect(try svgFileNames() == layerImageNames())
    }

    @Test func fillIsSnapperBlue() throws {
        let fill = try #require(try iconDocument()["fill"] as? [String: Any])
        #expect(fill["automatic-gradient"] as? String == "srgb:0.05882,0.42353,0.74118,1.00000")
    }

    @Test func layersAreWhite() throws {
        let hexColor = /#[0-9A-Fa-f]{6}/
        for name in try svgFileNames() {
            let source = try svgSource(name)
            #expect(source.contains("#FFFFFF"), "\(name) isn't white")
            let colors = Set(source.matches(of: hexColor).map { String($0.output) })
            #expect(colors == ["#FFFFFF"], "\(name) uses other colours: \(colors)")
        }
    }

    @Test func lensIsFilledRingNotStrokedCircle() throws {
        let lens = try svgSource("lens.svg")
        #expect(lens.contains(#"fill-rule="evenodd""#))
        #expect(!lens.contains("<circle"))
    }

    /// Stroked open paths render with a filled wedge in Icon Composer's Dark rendition, so every
    /// layer must be a filled outline.
    @Test func layersAreFilledOutlinesNotStrokes() throws {
        for name in try svgFileNames() {
            #expect(!(try svgSource(name)).contains("stroke"), "\(name) uses a stroke")
        }
    }

    @Test func hostBundleNamesAppIcon() {
        #expect(Bundle.main.object(forInfoDictionaryKey: "CFBundleIconName") as? String == "AppIcon")
    }

    @Test func hostBundleHasFallbackIcns() {
        #expect(Bundle.main.url(forResource: "AppIcon", withExtension: "icns") != nil)
    }
}
