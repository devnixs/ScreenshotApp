enum CopyAndCloseDecision {
    enum Start: Equatable {
        case refuse, copy
    }

    enum Finish: Equatable {
        case close, stayOpen
    }

    static func start(isDragging: Bool, hasAttachedSheet: Bool) -> Start {
        isDragging || hasAttachedSheet ? .refuse : .copy
    }

    static func finish(copySucceeded: Bool) -> Finish {
        copySucceeded ? .close : .stayOpen
    }
}
