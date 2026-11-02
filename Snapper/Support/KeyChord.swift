struct KeyChord: Hashable, Sendable {
    static let relevantModifierMask: UInt = 0x1E0000
    let keyCode: Int
    let modifiers: UInt

    init(keyCode: Int, rawModifiers: UInt) {
        self.keyCode = keyCode
        modifiers = rawModifiers & Self.relevantModifierMask
    }

    var displayString: String {
        let symbols: [(UInt, String)] = [
            (0x40000, "⌃"), (0x80000, "⌥"), (0x20000, "⇧"), (0x100000, "⌘")
        ]
        let digits = [29: "0", 18: "1", 19: "2", 20: "3", 21: "4",
                      23: "5", 22: "6", 26: "7", 28: "8", 25: "9"]
        return symbols.filter { modifiers & $0.0 != 0 }.map(\.1).joined()
            + (digits[keyCode] ?? "Key \(keyCode)")
    }
}
