import AVFoundation
import ImageIO
import UniformTypeIdentifiers

/// Framework objects stay local to this operation on the caller's detached task.
enum GIFEncoder {
    nonisolated static func encode(input: URL, output: URL, settings: GIFEncodingSettings,
                                   width: Int, height: Int) async throws {
        let asset = AVURLAsset(url: input)
        guard let track = try await asset.loadTracks(withMediaType: .video).first else {
            throw RecordingFailure.gifConversion("The recording contains no video track.")
        }
        let duration = try await asset.load(.duration).seconds
        guard duration.isFinite, width > 0, height > 0 else {
            throw RecordingFailure.gifConversion("The recording contains no usable video frames.")
        }
        let timestampReader = try AVAssetReader(asset: asset)
        let timestampOutput = AVAssetReaderTrackOutput(track: track, outputSettings: nil)
        timestampReader.add(timestampOutput)
        guard timestampReader.startReading() else {
            throw timestampReader.error ?? RecordingFailure.gifConversion("Couldn't read the recording.")
        }
        var timestamps: [Double] = []
        while let time = autoreleasepool(invoking: { () -> Double? in
            while let sample = timestampOutput.copyNextSampleBuffer() {
                // AVFoundation may emit a zero-sample drain marker with an invalid timestamp.
                guard CMSampleBufferGetNumSamples(sample) > 0 else { continue }
                // Output PTS applies the H.264 edit offset (raw PTS includes decode reordering delay).
                return CMSampleBufferGetOutputPresentationTimeStamp(sample).seconds
            }
            return nil
        }) {
            try Task.checkCancellation()
            guard time.isFinite else { throw RecordingFailure.gifConversion("The recording contains invalid frame timing.") }
            timestamps.append(time)
        }
        guard timestampReader.status == .completed else {
            throw timestampReader.error ?? RecordingFailure.gifConversion("Couldn't read the recording's frame timing.")
        }
        // Compressed H.264 samples can arrive in decode order. The decoded pass is in presentation order.
        timestamps.sort()
        let frames = GIFFrameSampler.plan(timestamps: timestamps, duration: duration,
                                          framesPerSecond: settings.framesPerSecond)
        guard !frames.isEmpty else {
            throw RecordingFailure.gifConversion("The recording contains no usable video frames.")
        }
        let reader = try AVAssetReader(asset: asset)
        let videoOutput = AVAssetReaderTrackOutput(track: track, outputSettings: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA
        ])
        videoOutput.alwaysCopiesSampleData = false
        reader.add(videoOutput)
        guard let destination = CGImageDestinationCreateWithURL(output as CFURL, UTType.gif.identifier as CFString,
                                                                frames.count, nil) else {
            throw RecordingFailure.gifConversion("Couldn't create the GIF file.")
        }
        CGImageDestinationSetProperties(destination, [
            kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]
        ] as CFDictionary)
        do {
            guard reader.startReading() else {
                throw reader.error ?? RecordingFailure.gifConversion("Couldn't decode the recording.")
            }
            var sourceIndex = 0
            var selectedIndex = 0
            while try autoreleasepool(invoking: { () throws -> Bool in
                guard let sample = videoOutput.copyNextSampleBuffer() else { return false }
                defer { sourceIndex += 1 }
                guard selectedIndex < frames.count, sourceIndex == frames[selectedIndex].sourceIndex else { return true }
                guard let buffer = CMSampleBufferGetImageBuffer(sample) else {
                    throw RecordingFailure.gifConversion("Couldn't decode a video frame.")
                }
                let image = try scaledImage(buffer, width: width, height: height)
                let delay = Double(frames[selectedIndex].delayCentiseconds) / 100
                CGImageDestinationAddImage(destination, image, [
                    kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: delay,
                                                   kCGImagePropertyGIFUnclampedDelayTime: delay]
                ] as CFDictionary)
                selectedIndex += 1
                return true
            }) {
                try Task.checkCancellation()
            }
            guard reader.status == .completed, selectedIndex == frames.count else {
                throw reader.error ?? RecordingFailure.gifConversion("Couldn't decode all GIF frames.")
            }
            guard CGImageDestinationFinalize(destination) else {
                throw RecordingFailure.gifConversion("Couldn't finish writing the GIF.")
            }
        } catch {
            reader.cancelReading()
            try? FileManager.default.removeItem(at: output)
            throw error
        }
    }

    nonisolated private static func scaledImage(_ buffer: CVPixelBuffer, width: Int, height: Int) throws -> CGImage {
        CVPixelBufferLockBaseAddress(buffer, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(buffer, .readOnly) }
        let bitmapInfo = CGBitmapInfo.byteOrder32Little.rawValue | CGImageAlphaInfo.premultipliedFirst.rawValue
        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
              let source = CGContext(data: CVPixelBufferGetBaseAddress(buffer),
                                     width: CVPixelBufferGetWidth(buffer), height: CVPixelBufferGetHeight(buffer),
                                     bitsPerComponent: 8, bytesPerRow: CVPixelBufferGetBytesPerRow(buffer),
                                     space: colorSpace, bitmapInfo: bitmapInfo),
              let sourceImage = source.makeImage(),
              let target = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                     bytesPerRow: 0, space: colorSpace, bitmapInfo: bitmapInfo) else {
            throw RecordingFailure.gifConversion("Couldn't resize a GIF frame.")
        }
        target.interpolationQuality = .high
        target.draw(sourceImage, in: CGRect(x: 0, y: 0, width: width, height: height))
        guard let image = target.makeImage() else { throw RecordingFailure.gifConversion("Couldn't render a GIF frame.") }
        return image
    }
}
