import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

enum PNGEncoderError: LocalizedError {
    case cannotCreateDestination, cannotFinalize

    var errorDescription: String? { "The screenshot couldn't be encoded as PNG." }
}

enum PNGEncoder {
    static func data(for image: CGImage, pixelsPerPoint: CGFloat) throws -> Data {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
            throw PNGEncoderError.cannotCreateDestination
        }
        let properties = [kCGImagePropertyDPIWidth: 72 * pixelsPerPoint,
                          kCGImagePropertyDPIHeight: 72 * pixelsPerPoint] as CFDictionary
        CGImageDestinationAddImage(destination, image, properties)
        guard CGImageDestinationFinalize(destination) else { throw PNGEncoderError.cannotFinalize }
        return data as Data
    }
}
