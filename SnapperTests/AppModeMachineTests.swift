import Testing
@testable import Snapper

struct AppModeMachineTests {
    struct Transition: Sendable {
        let initial: AppMode
        let event: AppModeEvent
        let action: AppModeAction
        let final: AppMode
    }

    // Literal expectations in event order, independent of the production switch.
    private static let events: [AppModeEvent] = [
        .shortcut(.screenshot), .shortcut(.record), .shortcut(.annotate),
        .menuItem(.screenshot), .menuItem(.record), .menuItem(.annotate),
        .statusItemClicked(.left), .statusItemClicked(.right),
        .selectionFinished, .selectionCancelled, .recordingEnded
    ]
    private static let table: [(AppMode, [(AppModeAction, AppMode)])] = [
        (.idle, [
            (.showOverlay(.screenshot), .selecting(.screenshot)),
            (.showOverlay(.record), .selecting(.record)),
            (.showOverlay(.annotate), .selecting(.annotate)),
            (.showOverlay(.screenshot), .selecting(.screenshot)),
            (.showOverlay(.record), .selecting(.record)),
            (.showOverlay(.annotate), .selecting(.annotate)),
            (.openMenu, .idle), (.openSettings, .idle),
            (.none, .idle), (.none, .idle), (.none, .idle)
        ]),
        (.selecting(.screenshot), [
            (.none, .selecting(.screenshot)), (.none, .selecting(.screenshot)), (.none, .selecting(.screenshot)),
            (.none, .selecting(.screenshot)), (.none, .selecting(.screenshot)), (.none, .selecting(.screenshot)),
            (.none, .selecting(.screenshot)), (.none, .selecting(.screenshot)),
            (.completeSelection(.screenshot), .idle), (.none, .idle), (.none, .selecting(.screenshot))
        ]),
        (.selecting(.record), [
            (.none, .selecting(.record)), (.none, .selecting(.record)), (.none, .selecting(.record)),
            (.none, .selecting(.record)), (.none, .selecting(.record)), (.none, .selecting(.record)),
            (.none, .selecting(.record)), (.none, .selecting(.record)),
            (.completeSelection(.record), .recording), (.none, .idle), (.none, .selecting(.record))
        ]),
        (.selecting(.annotate), [
            (.none, .selecting(.annotate)), (.none, .selecting(.annotate)), (.none, .selecting(.annotate)),
            (.none, .selecting(.annotate)), (.none, .selecting(.annotate)), (.none, .selecting(.annotate)),
            (.none, .selecting(.annotate)), (.none, .selecting(.annotate)),
            (.completeSelection(.annotate), .idle), (.none, .idle), (.none, .selecting(.annotate))
        ]),
        (.recording, [
            (.none, .recording), (.stopRecording, .recording), (.none, .recording),
            (.none, .recording), (.stopRecording, .recording), (.none, .recording),
            (.stopRecording, .recording), (.stopRecording, .recording),
            (.none, .recording), (.none, .recording), (.none, .idle)
        ])
    ]
    static var transitions: [Transition] {
        table.flatMap { initial, expected in
            zip(events, expected).map { event, result in
                Transition(initial: initial, event: event, action: result.0, final: result.1)
            }
        }
    }

    private func machine(in mode: AppMode) -> AppModeMachine {
        var machine = AppModeMachine()
        switch mode {
        case .idle: break
        case .selecting(let purpose): _ = machine.handle(.shortcut(purpose))
        case .recording:
            _ = machine.handle(.shortcut(.record))
            _ = machine.handle(.selectionFinished)
        }
        return machine
    }

    @Test func literalTableCoversEveryCell() {
        #expect(Self.table.count == 5)
        #expect(Self.table.allSatisfy { $0.1.count == 11 })
        #expect(Self.transitions.count == 55)
    }

    @Test(arguments: transitions)
    func transitionMatchesProductRules(_ row: Transition) {
        var machine = machine(in: row.initial)
        #expect(machine.mode == row.initial)
        #expect(machine.handle(row.event) == row.action)
        #expect(machine.mode == row.final)
    }

    @Test func repeatedStopsRemainRecordingUntilSessionEnds() {
        var machine = machine(in: .recording)
        for event: AppModeEvent in [
            .shortcut(.record), .shortcut(.record), .statusItemClicked(.left),
            .statusItemClicked(.left), .statusItemClicked(.right), .menuItem(.record)
        ] {
            #expect(machine.handle(event) == .stopRecording)
            #expect(machine.mode == .recording)
        }
        #expect(machine.handle(.recordingEnded) == .none)
        #expect(machine.mode == .idle)
        #expect(machine.handle(.shortcut(.screenshot)) == .showOverlay(.screenshot))
        #expect(machine.mode == .selecting(.screenshot))
    }

    @Test func cancellationAndCompletionAllowNewSelections() {
        var machine = AppModeMachine()
        #expect(machine.handle(.shortcut(.annotate)) == .showOverlay(.annotate))
        #expect(machine.mode == .selecting(.annotate))
        #expect(machine.handle(.selectionCancelled) == .none)
        #expect(machine.mode == .idle)
        #expect(machine.handle(.menuItem(.screenshot)) == .showOverlay(.screenshot))
        #expect(machine.mode == .selecting(.screenshot))
        #expect(machine.handle(.selectionFinished) == .completeSelection(.screenshot))
        #expect(machine.mode == .idle)
    }
}
