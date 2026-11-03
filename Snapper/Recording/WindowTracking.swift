import CoreGraphics
import Foundation

enum WindowPollResult: Equatable {
    case unavailable
    case gone
    case present(cgBounds: CGRect, isOnscreen: Bool)
}

enum WindowInfoParser {
    static func parse(_ list: [[String: Any]]?, windowID: CGWindowID) -> WindowPollResult {
        guard let list else { return .unavailable }
        guard let entry = list.first(where: { ($0[kCGWindowNumber as String] as? NSNumber)?.uint32Value == windowID }) else {
            return .gone
        }
        guard let dictionary = entry[kCGWindowBounds as String] as? [String: Any],
              let bounds = CGRect(dictionaryRepresentation: dictionary as CFDictionary),
              bounds.origin.x.isFinite, bounds.origin.y.isFinite,
              bounds.width.isFinite, bounds.height.isFinite else { return .unavailable }
        return .present(cgBounds: bounds, isOnscreen: (entry[kCGWindowIsOnscreen as String] as? Bool) ?? false)
    }
}

struct WindowTrackingState: Equatable {
    var appKitFrame: CGRect
    var isVisible = true
    var isClosed = false
}

enum WindowTrackingEvent: Equatable {
    case unchanged
    case moved(CGRect)
    case hidden
    case shown(CGRect)
    case closed
}

enum WindowTrackingReducer {
    private static func rounded(_ rect: CGRect) -> CGRect {
        CGRect(x: rect.origin.x.rounded(), y: rect.origin.y.rounded(),
               width: rect.width.rounded(), height: rect.height.rounded())
    }

    static func reduce(_ state: WindowTrackingState, poll: WindowPollResult,
                       primaryDisplayHeight: CGFloat) -> (WindowTrackingState, WindowTrackingEvent) {
        guard !state.isClosed else { return (state, .unchanged) }
        var next = state
        switch poll {
        case .unavailable: return (state, .unchanged)
        case .gone:
            next.isClosed = true
            return (next, .closed)
        case .present(let bounds, let onscreen):
            let frame = rounded(DisplayGeometry.appKitRect(fromCG: bounds, primaryHeight: primaryDisplayHeight))
            next.appKitFrame = frame
            next.isVisible = onscreen
            if !onscreen { return (next, state.isVisible ? .hidden : .unchanged) }
            if !state.isVisible { return (next, .shown(frame)) }
            return (next, rounded(state.appKitFrame) == frame ? .unchanged : .moved(frame))
        }
    }
}
