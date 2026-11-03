import Foundation
import CoreGraphics

/// Text placement and re-entry routing, independent of AppKit event delivery.
enum TextInteractionDecision: Equatable {
    case commit, beginNew(CGPoint), edit(UUID), none
    static func decide(isEditing: Bool, tool: AnnotationTool, clickCount: Int,
                       point: CGPoint?, document: AnnotationDocument) -> Self {
        if isEditing { return .commit }
        guard let point, tool == .text || (tool == .select && clickCount == 2) else { return .none }
        if let text = document.shapes.reversed().first(where: {
            if case .text = $0.kind { return $0.hitTest(point, pixelsPerPoint: document.pixelsPerPoint, slop: 0) }
            return false
        }) { return .edit(text.id) }
        return tool == .text ? .beginNew(point) : .none
    }
}
