import Testing
@testable import Snapper

struct GIFFrameSamplerTests {
    @Test func uniformTenFps() {
        let plan = GIFFrameSampler.plan(timestamps: (0..<30).map { Double($0) / 30 }, duration: 1, framesPerSecond: 10)
        #expect(plan.map(\.sourceIndex) == Array(stride(from: 0, to: 30, by: 3)))
        #expect(plan.map(\.delayCentiseconds) == Array(repeating: 10, count: 10))
    }
    @Test func uniformFifteenFpsCumulativeRounding() {
        let plan = GIFFrameSampler.plan(timestamps: (0..<30).map { Double($0) / 30 }, duration: 1, framesPerSecond: 15)
        #expect(plan.map(\.sourceIndex) == Array(stride(from: 0, to: 30, by: 2)))
        #expect(plan.allSatisfy { [6, 7].contains($0.delayCentiseconds) })
        #expect(plan.reduce(0) { $0 + $1.delayCentiseconds } == 100)
    }
    @Test func variableFramesKeepGapsAndFinalHold() {
        let plan = GIFFrameSampler.plan(timestamps: [0, 0.5, 2], duration: 3, framesPerSecond: 10)
        #expect(plan.map(\.sourceIndex) == [0, 1, 2])
        #expect(plan.map(\.delayCentiseconds) == [50, 150, 100])
    }
    @Test func burstsAreSampledAndLastFrameGetsAnInterval() {
        let plan = GIFFrameSampler.plan(timestamps: [0, 0.01, 0.02, 0.03, 0.2], duration: 0.2, framesPerSecond: 10)
        #expect(plan.map(\.sourceIndex) == [0, 4])
        #expect(plan.map(\.delayCentiseconds) == [20, 10])
    }
    @Test func nonzeroStartUsesRelativeDelays() {
        let plan = GIFFrameSampler.plan(timestamps: [0.35, 0.5, 0.7], duration: 1, framesPerSecond: 10)
        #expect(plan.map(\.sourceIndex) == [0, 1, 2])
        #expect(plan.map(\.delayCentiseconds) == [15, 20, 30])
    }
    @Test func tinyDelaysAreClampedForBrowsers() {
        let plan = GIFFrameSampler.plan(timestamps: [0.095, 0.1], duration: 0.3, framesPerSecond: 10)
        #expect(plan.map(\.sourceIndex) == [0, 1])
        #expect(plan.map(\.delayCentiseconds) == [2, 20])
    }
    @Test func singleFrameHasFullDuration() {
        #expect(GIFFrameSampler.plan(timestamps: [0], duration: 3, framesPerSecond: 10) == [.init(sourceIndex: 0, delayCentiseconds: 300)])
        #expect(GIFFrameSampler.plan(timestamps: [0], duration: 0, framesPerSecond: 10) == [.init(sourceIndex: 0, delayCentiseconds: 10)])
    }
    @Test func emptyInput() {
        #expect(GIFFrameSampler.plan(timestamps: [], duration: 1, framesPerSecond: 10).isEmpty)
    }
}
