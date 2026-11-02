import Testing
@testable import Snapper

struct StatusItemClickRouterTests {
    @Test func primaryClickNormalizesToLeft() {
        #expect(StatusItemClickRouter.button(mouseButton: .left, controlKeyDown: false) == .left)
    }

    @Test func controlClickNormalizesToRight() {
        #expect(StatusItemClickRouter.button(mouseButton: .left, controlKeyDown: true) == .right)
    }

    @Test func secondaryClickNormalizesToRight() {
        #expect(StatusItemClickRouter.button(mouseButton: .right, controlKeyDown: false) == .right)
    }

    @Test func secondaryControlClickNormalizesToRight() {
        #expect(StatusItemClickRouter.button(mouseButton: .right, controlKeyDown: true) == .right)
    }

    @Test func accessibilityPressNormalizesToLeft() {
        #expect(StatusItemClickRouter.button(mouseButton: nil, controlKeyDown: false) == .left)
        #expect(StatusItemClickRouter.button(mouseButton: nil, controlKeyDown: true) == .left)
    }
}
