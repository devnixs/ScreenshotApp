/// Normalizes mouse and accessibility presses; AppModeMachine decides their meaning.
enum StatusItemClickRouter {
    static func button(mouseButton: StatusItemButton?, controlKeyDown: Bool) -> StatusItemButton {
        switch mouseButton {
        case .right: .right
        case .left: controlKeyDown ? .right : .left
        case nil: .left
        }
    }
}
