import CoreGraphics
import Foundation
import Testing
@testable import Snapper

struct RGBAColorTests {
    @Test func presetOrder() {
        #expect(ColorPreset.allCases == [.red, .orange, .yellow, .green, .blue, .purple, .black, .white])
        #expect(RGBAColor.presets == [.red, .orange, .yellow, .green, .blue, .purple, .black, .white])
        #expect(ColorPreset.allCases.map(\.displayName) == ["Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White"])
    }

    @Test func presetValues() {
        let components: [(Double, Double, Double)] = [
            (255, 59, 48), (255, 149, 0), (255, 204, 0), (52, 199, 89),
            (0, 122, 255), (175, 82, 222), (0, 0, 0), (255, 255, 255)
        ]
        for (color, (r, g, b)) in zip(RGBAColor.presets, components) {
            #expect(color.red == r / 255)
            #expect(color.green == g / 255)
            #expect(color.blue == b / 255)
            #expect(color.alpha == 1)
        }
    }

    @Test func codableRoundTrip() throws {
        for color in RGBAColor.presets + [RGBAColor(red: 0.1, green: 0.2, blue: 0.3, alpha: 0.5)] {
            let data = try JSONEncoder().encode(color)
            #expect(try JSONDecoder().decode(RGBAColor.self, from: data) == color)
        }
    }

    @Test func encodedKeys() throws {
        let data = try JSONEncoder().encode(RGBAColor.red)
        let object = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
        #expect(Set(object.keys) == ["red", "green", "blue", "alpha"])
    }

    @Test func decodeRejectsInvalid() throws {
        for key in ["red", "green", "blue", "alpha"] {
            for value in [-0.1, 1.5] {
                var object = ["red": 0.5, "green": 0.5, "blue": 0.5, "alpha": 1.0]
                object[key] = value
                let data = try JSONSerialization.data(withJSONObject: object)
                #expect(throws: DecodingError.self) { try JSONDecoder().decode(RGBAColor.self, from: data) }
            }
            var object = ["red": 0.5, "green": 0.5, "blue": 0.5, "alpha": 1.0]
            object.removeValue(forKey: key)
            let data = try JSONSerialization.data(withJSONObject: object)
            do {
                _ = try JSONDecoder().decode(RGBAColor.self, from: data)
                Issue.record("Missing component should fail")
            } catch DecodingError.dataCorrupted { } // Missing keys must use the persisted-format error.
        }
        let decoder = JSONDecoder()
        decoder.nonConformingFloatDecodingStrategy = .convertFromString(positiveInfinity: "inf", negativeInfinity: "-inf", nan: "nan")
        for value in ["inf", "-inf", "nan"] {
            let data = Data("{\"red\":\"\(value)\",\"green\":0,\"blue\":0,\"alpha\":1}".utf8)
            #expect(throws: DecodingError.self) { try decoder.decode(RGBAColor.self, from: data) }
        }
    }

    @Test func initClamps() {
        #expect(RGBAColor(red: 2, green: -1, blue: 0.5, alpha: 3) == RGBAColor(red: 1, green: 0, blue: 0.5, alpha: 1))
        let color = RGBAColor(red: .nan, green: .infinity, blue: -.infinity, alpha: .nan)
        #expect(color == RGBAColor(red: 0, green: 0, blue: 0, alpha: 1))
    }

    private func expectClose(_ actual: RGBAColor, _ expected: RGBAColor, accuracy: Double = 1e-6) {
        #expect(abs(actual.red - expected.red) <= accuracy)
        #expect(abs(actual.green - expected.green) <= accuracy)
        #expect(abs(actual.blue - expected.blue) <= accuracy)
        #expect(abs(actual.alpha - expected.alpha) <= accuracy)
    }

    @Test func cgColorRoundTrip() throws {
        for color in RGBAColor.presets + [RGBAColor(red: 0.1, green: 0.2, blue: 0.3, alpha: 0.5)] {
            expectClose(try #require(RGBAColor(cgColor: color.cgColor)), color)
        }
    }

    @Test func cgColorFromGrayAndP3() throws {
        let gray = try #require(RGBAColor(cgColor: CGColor(gray: 0.5, alpha: 1)))
        #expect(abs(gray.red - gray.green) < 1e-6)
        #expect(abs(gray.green - gray.blue) < 1e-6)
        #expect(gray.red > 0 && gray.red < 1)
        #expect(gray.alpha == 1)
        let space = try #require(CGColorSpace(name: CGColorSpace.displayP3))
        let p3 = try #require(CGColor(colorSpace: space, components: [1, 0, 0, 1]))
        expectClose(try #require(RGBAColor(cgColor: p3)), RGBAColor(red: 1, green: 0, blue: 0), accuracy: 1e-3)
    }
}
