import Testing
@testable import Snapper

struct StatusItemClickRouterTests {
    @Test func primaryClickShowsMenu() {
        #expect(StatusItemClickRouter.action(button: .left, controlKeyDown: false) == .showMenu)
    }

    @Test func controlClickOpensSettings() {
        #expect(StatusItemClickRouter.action(button: .left, controlKeyDown: true) == .openSettings)
    }

    @Test func secondaryClickOpensSettings() {
        #expect(StatusItemClickRouter.action(button: .right, controlKeyDown: false) == .openSettings)
    }

    @Test func secondaryControlClickOpensSettings() {
        #expect(StatusItemClickRouter.action(button: .right, controlKeyDown: true) == .openSettings)
    }

    @Test func accessibilityPressShowsMenu() {
        #expect(StatusItemClickRouter.action(button: nil, controlKeyDown: false) == .showMenu)
        #expect(StatusItemClickRouter.action(button: nil, controlKeyDown: true) == .showMenu)
    }
}
