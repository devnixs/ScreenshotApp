import Foundation
import Testing
@testable import Snapper

struct SymbolicHotKeysParserTests {
    private func entry(_ parameters: Any = [52, 21, 0x120000], enabled: Any = true) -> [String: Any] {
        ["enabled": enabled, "value": ["type": "standard", "parameters": parameters]]
    }

    private func shortcut(_ root: [String: Any], id: Int = 30) -> SystemShortcut? {
        SymbolicHotKeysParser.parse(root).first { $0.id == id }
    }

    @Test func missingPreferencesUseOnlyNonTouchBarDefaults() {
        let result = SymbolicHotKeysParser.parse(nil)
        #expect(result == SymbolicHotKeysParser.parse([:]))
        #expect(result.map(\.id) == [28, 29, 30, 31, 184])
        #expect(result.map(\.chord.keyCode) == [20, 20, 21, 21, 23])
        #expect(result.map(\.chord.modifiers) == [0x120000, 0x160000, 0x120000, 0x160000, 0x120000])
        #expect(result.allSatisfy { $0.isEnabled })
    }

    @Test func realDisabledFixture() {
        let fixture: [String: Any] = [
            "28": entry([51, 20, 1179648], enabled: 0),
            "29": entry([51, 20, 1441792], enabled: 0),
            "30": entry([52, 21, 1179648], enabled: 0),
            "31": entry([52, 21, 1441792], enabled: 0),
            "184": entry([53, 23, 1179648], enabled: 0)
        ]
        let result = SymbolicHotKeysParser.parse(fixture)
        #expect(result.count == 5)
        #expect(result.allSatisfy { !$0.isEnabled })
    }

    @Test func enabledDecodingIsConservative() {
        for value: Any in [true, NSNumber(value: 1), 1, "yes", 2, -1, 0.5] {
            #expect(shortcut(["30": entry(enabled: value)])?.isEnabled == true)
        }
        for value: Any in [false, NSNumber(value: 0), 0] {
            #expect(shortcut(["30": entry(enabled: value)])?.isEnabled == false)
        }
        #expect(shortcut(["30": ["value": ["parameters": [52, 21, 1179648]]]])?.isEnabled == true)
    }

    @Test func readsCustomBindingsAndMasksNoise() {
        #expect(shortcut(["28": entry([54, 22, 1179648])], id: 28)?.chord == KeyChord(keyCode: 22, rawModifiers: 0x120000))
        #expect(shortcut(["30": entry([52, 21, 0x920000])])?.chord.modifiers == 0x120000)
        #expect(shortcut(["30": entry([NSNumber(value: 52), NSNumber(value: 21), NSNumber(value: 1179648)])])?.chord.keyCode == 21)
    }

    @Test func malformedParametersFallBackWithoutLosingEnabled() {
        let malformed: [Any] = [
            [52, 21], ["52", "21", "1179648"], "not an array",
            [52, -1, 1179648], [52, 21, -1], [52, 21.5, 1179648],
            [52, true, 1179648], [52, 21, NSNumber(value: UInt64.max)]
        ]
        for parameters in malformed {
            let result = shortcut(["30": entry(parameters, enabled: false)])
            #expect(result?.chord == KeyChord(keyCode: 21, rawModifiers: 0x120000))
            #expect(result?.isEnabled == false)
        }
        #expect(shortcut(["30": ["enabled": false]])?.chord.keyCode == 21)
        #expect(shortcut(["30": "malformed"])?.chord.keyCode == 21)
    }

    @Test func unassignedAndTouchBarAndUnrelatedEntries() {
        #expect(shortcut(["30": entry([0, 65535, 0])]) == nil)
        let result = SymbolicHotKeysParser.parse([
            "181": entry([54, 22, 1179648]),
            "182": entry([54, 22, 1441792], enabled: false),
            "15": entry(), "164": entry(), "banana": entry()
        ])
        #expect(result.map(\.id) == [28, 29, 30, 31, 181, 182, 184])
        #expect(result.first { $0.id == 181 }?.isEnabled == true)
        #expect(result.first { $0.id == 182 }?.isEnabled == false)
    }
}
