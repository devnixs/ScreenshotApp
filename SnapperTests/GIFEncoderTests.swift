import AVFoundation
import ImageIO
import Testing
@testable import Snapper

struct GIFEncoderTests {
    private func folder() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("SnapperGIFTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func makeVideo(at url: URL, times: [Int64], duration: Int64) async throws {
        let writer = try AVAssetWriter(outputURL: url, fileType: .mp4)
        let input = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: 320, AVVideoHeightKey: 240
        ])
        let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: input,
            sourcePixelBufferAttributes: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                kCVPixelBufferWidthKey as String: 320, kCVPixelBufferHeightKey as String: 240])
        writer.add(input)
        #expect(writer.startWriting())
        writer.startSession(atSourceTime: .zero)
        for (index, time) in times.enumerated() {
            while !input.isReadyForMoreMediaData { try await Task.sleep(for: .milliseconds(1)) }
            var buffer: CVPixelBuffer?
            #expect(CVPixelBufferCreate(kCFAllocatorDefault, 320, 240, kCVPixelFormatType_32BGRA, nil, &buffer) == kCVReturnSuccess)
            let pixels = try #require(buffer)
            CVPixelBufferLockBaseAddress(pixels, [])
            let base = CVPixelBufferGetBaseAddress(pixels)!.assumingMemoryBound(to: UInt32.self)
            let rowStride = CVPixelBufferGetBytesPerRow(pixels) / 4
            for row in 0..<240 {
                for column in 0..<320 { base[row * rowStride + column] = index.isMultiple(of: 2) ? 0xFFFF0000 : 0xFF0000FF }
            }
            CVPixelBufferUnlockBaseAddress(pixels, [])
            #expect(adaptor.append(pixels, withPresentationTime: CMTime(value: time, timescale: 30)))
        }
        input.markAsFinished()
        writer.endSession(atSourceTime: CMTime(value: duration, timescale: 30))
        await writer.finishWriting()
        #expect(writer.status == .completed)
    }

    private func delays(_ source: CGImageSource) throws -> [Double] {
        try (0..<CGImageSourceGetCount(source)).map { index in
            let properties = try #require(CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [String: Any])
            let gif = try #require(properties[kCGImagePropertyGIFDictionary as String] as? [String: Any])
            return try #require(gif[kCGImagePropertyGIFUnclampedDelayTime as String] as? Double)
        }
    }

    @Test func encodesLoopingGifWithExpectedDimensionsFramesAndTiming() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("source.mp4")
        let output = directory.appendingPathComponent("output.gif")
        try await makeVideo(at: input, times: (0..<30).map(Int64.init), duration: 30)
        try await GIFEncoder.encode(input: input, output: output, settings: .forQuality(.medium), width: 160, height: 120)
        let source = try #require(CGImageSourceCreateWithURL(output as CFURL, nil))
        #expect(CGImageSourceGetType(source) as String? == "com.compuserve.gif")
        #expect(CGImageSourceGetCount(source) == 10)
        for index in 0..<CGImageSourceGetCount(source) {
            let image = try #require(CGImageSourceCreateImageAtIndex(source, index, nil))
            #expect(image.width == 160)
            #expect(image.height == 120)
        }
        let properties = try #require(CGImageSourceCopyProperties(source, nil) as? [String: Any])
        let gif = try #require(properties[kCGImagePropertyGIFDictionary as String] as? [String: Any])
        #expect(gif[kCGImagePropertyGIFLoopCount as String] as? Int == 0)
        #expect(abs(try delays(source).reduce(0, +) - 1) < 0.01)
        // The first frame is red and the next selected source frame (index 3) is blue.
        for (index, redDominates) in [(0, true), (1, false)] {
            let image = try #require(CGImageSourceCreateImageAtIndex(source, index, nil))
            let context = try #require(CGContext(data: nil, width: 1, height: 1, bitsPerComponent: 8,
                bytesPerRow: 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: 1, height: 1))
            let bytes = try #require(context.data).assumingMemoryBound(to: UInt8.self)
            #expect(redDominates ? bytes[0] > bytes[2] : bytes[2] > bytes[0])
        }
    }

    @Test func sparseFramesKeepHeldTiming() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("sparse.mp4")
        let output = directory.appendingPathComponent("sparse.gif")
        try await makeVideo(at: input, times: [0, 15, 60], duration: 90)
        try await GIFEncoder.encode(input: input, output: output, settings: .forQuality(.medium), width: 160, height: 120)
        let source = try #require(CGImageSourceCreateWithURL(output as CFURL, nil))
        #expect(CGImageSourceGetCount(source) == 3)
        let actual = try delays(source)
        #expect(actual == [0.5, 1.5, 1])
    }

    @Test func singleFrameKeepsDuration() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("single.mp4")
        let output = directory.appendingPathComponent("single.gif")
        try await makeVideo(at: input, times: [0], duration: 45)
        try await GIFEncoder.encode(input: input, output: output, settings: .forQuality(.medium), width: 160, height: 120)
        let source = try #require(CGImageSourceCreateWithURL(output as CFURL, nil))
        #expect(CGImageSourceGetCount(source) == 1)
        #expect(try delays(source) == [1.5])
    }

    @Test func invalidMediaThrowsWithoutOutput() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("invalid.mp4")
        let output = directory.appendingPathComponent("output.gif")
        try Data("not media".utf8).write(to: input)
        await #expect(throws: (any Error).self) {
            try await GIFEncoder.encode(input: input, output: output, settings: .forQuality(.medium), width: 160, height: 120)
        }
        #expect(!FileManager.default.fileExists(atPath: output.path))
    }
}
