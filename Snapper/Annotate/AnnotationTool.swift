enum AnnotationTool: Int, CaseIterable, Sendable {
    case select, rectangle, arrow, text

    var segmentIndex: Int { rawValue }
    init?(segmentIndex: Int) { self.init(rawValue: segmentIndex) }
    var createsShapesByDragging: Bool { self == .rectangle || self == .arrow }

    static func forShortcut(characters: String, hasCommandOptionOrControl: Bool, textToolEnabled: Bool) -> Self? {
        guard !hasCommandOptionOrControl else { return nil }
        switch characters.lowercased() {
        case "v": return .select
        case "r": return .rectangle
        case "a": return .arrow
        case "t" where textToolEnabled: return .text
        default: return nil
        }
    }
}
