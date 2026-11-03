import Foundation
import CoreGraphics

struct TextEditDraft {
    var original: AnnotationShape?
    var string: String
    var origin: CGPoint
    var color: RGBAColor
    var strokeWidth: StrokeWidth
    var measuredSize: CGSize
}

enum TextCommitDecision: Equatable {
    case discard, noChange, add(AnnotationShape), update(AnnotationShape), delete(UUID)
    static func decide(_ draft: TextEditDraft) -> Self {
        if draft.string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return draft.original.map { .delete($0.id) } ?? .discard
        }
        if let original = draft.original, case .text(let box) = original.kind,
           box.string == draft.string, original.color == draft.color, original.strokeWidth == draft.strokeWidth {
            return .noChange
        }
        let box = TextBox(origin: draft.origin, string: draft.string, measuredSize: draft.measuredSize)
        let shape = AnnotationShape(id: draft.original?.id ?? UUID(), kind: .text(box), color: draft.color, strokeWidth: draft.strokeWidth)
        return draft.original == nil ? .add(shape) : .update(shape)
    }
}

extension AnnotationDocument {
    mutating func apply(_ decision: TextCommitDecision) {
        switch decision {
        case .discard, .noChange: break
        case .add(let shape): add(shape)
        case .update(let shape): replace(shape)
        case .delete(let id): delete(id: id)
        }
    }
}
