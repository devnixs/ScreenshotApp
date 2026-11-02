enum StatusItemMouseButton {
    case left, right
}

enum StatusItemClickAction: Equatable {
    case showMenu, openSettings
}

enum StatusItemClickRouter {
    static func action(button: StatusItemMouseButton?, controlKeyDown: Bool) -> StatusItemClickAction {
        switch button {
        case .right: .openSettings
        case .left: controlKeyDown ? .openSettings : .showMenu
        case nil: .showMenu
        }
    }
}
