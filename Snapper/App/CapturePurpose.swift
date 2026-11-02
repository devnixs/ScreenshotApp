/// Raw values are persisted KeyboardShortcuts identifiers and must never change after shipping.
enum CapturePurpose: String, CaseIterable, Sendable, Equatable {
    case screenshot, record, annotate
}
