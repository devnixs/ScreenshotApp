import CoreGraphics

struct TextBox: Equatable, Sendable {
    var origin: CGPoint
    var string: String
    /// Authoritative layout size in image pixels, including the contrasting outline.
    /// The view layer must remeasure and update this in the same edit whenever text or
    /// stroke width changes. The pure model never measures text or imports font APIs.
    var measuredSize: CGSize
}
