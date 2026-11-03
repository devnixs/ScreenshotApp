import Foundation

struct GIFFrame: Equatable, Sendable {
    let sourceIndex: Int
    let delayCentiseconds: Int
}

enum GIFFrameSampler {
    static func plan(timestamps: [Double], duration: Double, framesPerSecond: Int) -> [GIFFrame] {
        guard let first = timestamps.first, framesPerSecond > 0 else { return [] }
        let fps = Double(framesPerSecond)
        var selected = [0]
        // A tiny tolerance keeps rational frame rates on their slot at floating-point boundaries.
        let tolerance = 1e-9
        var nextSlot = floor(first * fps + tolerance) + 1
        for index in timestamps.indices.dropFirst() {
            let slot = timestamps[index] * fps
            if slot + tolerance >= nextSlot {
                selected.append(index)
                nextSlot = floor(slot + tolerance) + 1
            }
        }
        return selected.enumerated().map { position, index in
            let start = timestamps[index] - first
            let end = position + 1 < selected.count
                ? timestamps[selected[position + 1]] - first
                : max(duration, timestamps[index] + 1 / fps) - first
            let delay = Int((end * 100).rounded() - (start * 100).rounded())
            return GIFFrame(sourceIndex: index, delayCentiseconds: max(2, delay))
        }
    }
}
