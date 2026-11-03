import AVFoundation
import Testing
@testable import Snapper

struct MP4TranscoderTests {
    private func folder() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("SnapperTranscoderTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    /// A synthetic VFR source, ending with a held frame longer than the preceding interval.
    private func makeVideo(at url: URL, times: [Int64] = (0..<30).map { Int64($0 + $0 / 3) }) async throws {
        let writer = try AVAssetWriter(outputURL: url, fileType: .mp4)
        let input = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: 320, AVVideoHeightKey: 240
        ])
        let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: input,
            sourcePixelBufferAttributes: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32ARGB,
                kCVPixelBufferWidthKey as String: 320, kCVPixelBufferHeightKey as String: 240])
        writer.add(input)
        #expect(writer.startWriting())
        writer.startSession(atSourceTime: .zero)
        for (index, time) in times.enumerated() {
            while !input.isReadyForMoreMediaData { try await Task.sleep(for: .milliseconds(1)) }
            var buffer: CVPixelBuffer?
            #expect(CVPixelBufferCreate(kCFAllocatorDefault, 320, 240, kCVPixelFormatType_32ARGB, nil, &buffer) == kCVReturnSuccess)
            let pixels = try #require(buffer)
            CVPixelBufferLockBaseAddress(pixels, [])
            memset(CVPixelBufferGetBaseAddress(pixels), Int32(index * 30), CVPixelBufferGetDataSize(pixels))
            CVPixelBufferUnlockBaseAddress(pixels, [])
            #expect(adaptor.append(pixels, withPresentationTime: CMTime(value: time, timescale: 30)))
        }
        input.markAsFinished()
        writer.endSession(atSourceTime: CMTime(value: 45, timescale: 30))
        await writer.finishWriting()
        #expect(writer.status == .completed)
    }

    private func timestamps(_ url: URL) async throws -> [Double] {
        let asset = AVURLAsset(url: url)
        let video = try #require(await asset.loadTracks(withMediaType: .video).first)
        let reader = try AVAssetReader(asset: asset)
        let output = AVAssetReaderTrackOutput(track: video, outputSettings: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32ARGB])
        reader.add(output)
        #expect(reader.startReading())
        var times: [Double] = []
        while let sample = output.copyNextSampleBuffer() {
            times.append(CMSampleBufferGetPresentationTimeStamp(sample).seconds)
        }
        #expect(reader.status == .completed)
        return times
    }

    @Test func preservesDimensionsVfrTimestampsAndHeldFinalFrame() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("source.mp4")
        let output = directory.appendingPathComponent("source-out.mp4")
        try await makeVideo(at: input)
        try await MP4Transcoder.transcode(input: input, output: output, averageBitRate: 115_200)
        let asset = AVURLAsset(url: output)
        let track = try #require(await asset.loadTracks(withMediaType: .video).first)
        #expect(try await track.load(.naturalSize) == CGSize(width: 320, height: 240))
        let sourceDuration = try await AVURLAsset(url: input).load(.duration).seconds
        #expect(abs(try await asset.load(.duration).seconds - sourceDuration) < 1.0 / 30)
        let sourceTimes = try await timestamps(input)
        let outputTimes = try await timestamps(output)
        #expect(sourceTimes.count == outputTimes.count)
        for (source, output) in zip(sourceTimes, outputTimes) { #expect(abs(source - output) < 0.001) }
        let final = directory.appendingPathComponent("final.mp4")
        try FileManager.default.moveItem(at: output, to: final)
        #expect(FileManager.default.fileExists(atPath: final.path))
        #expect(!FileManager.default.fileExists(atPath: output.path))
    }

    @Test func singleFrameFinishesCleanly() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("single.mp4")
        let output = directory.appendingPathComponent("output.mp4")
        try await makeVideo(at: input, times: [0])
        try await MP4Transcoder.transcode(input: input, output: output, averageBitRate: 115_200)
        #expect(try await timestamps(output).count == 1)
    }

    @Test func invalidMediaThrows() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("invalid.mp4")
        let output = directory.appendingPathComponent("output.mp4")
        try Data("not media".utf8).write(to: input)
        await #expect(throws: (any Error).self) {
            try await MP4Transcoder.transcode(input: input, output: output, averageBitRate: 115_200)
        }
        #expect(!FileManager.default.fileExists(atPath: output.path))
    }
    private func addAudio(to videoURL: URL, in directory: URL) async throws -> URL {
        let wav = directory.appendingPathComponent("tone.wav")
        var wavData = Data()
        func append<T: FixedWidthInteger>(_ value: T) {
            var littleEndian = value.littleEndian
            withUnsafeBytes(of: &littleEndian) { wavData.append(contentsOf: $0) }
        }
        let sampleCount = 72_000
        wavData.append(contentsOf: "RIFF".utf8)
        append(UInt32(36 + sampleCount * 2))
        wavData.append(contentsOf: "WAVEfmt ".utf8)
        append(UInt32(16)); append(UInt16(1)); append(UInt16(1))
        append(UInt32(48_000)); append(UInt32(96_000)); append(UInt16(2)); append(UInt16(16))
        wavData.append(contentsOf: "data".utf8)
        append(UInt32(sampleCount * 2))
        for index in 0..<sampleCount {
            append(Int16(sin(Double(index) * 2 * .pi * 440 / 48_000) * 8_000))
        }
        try wavData.write(to: wav)
        let composition = AVMutableComposition()
        let videoAsset = AVURLAsset(url: videoURL)
        let duration = try await videoAsset.load(.duration)
        let video = try #require(await videoAsset.loadTracks(withMediaType: .video).first)
        let audioAsset = AVURLAsset(url: wav)
        defer { withExtendedLifetime(audioAsset) {} }
        let audio = try #require(await audioAsset.loadTracks(withMediaType: .audio).first)
        let videoDestination = try #require(composition.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid))
        let audioDestination = try #require(composition.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid))
        try videoDestination.insertTimeRange(CMTimeRange(start: .zero, duration: duration), of: video, at: .zero)
        do { try audioDestination.insertTimeRange(CMTimeRange(start: .zero, duration: duration), of: audio, at: .zero) }
        catch { Issue.record("Audio fixture insertion failed: \(error)"); throw error }
        let exporter = try #require(AVAssetExportSession(asset: composition, presetName: AVAssetExportPresetHighestQuality))
        let output = directory.appendingPathComponent("with-audio.mp4")
        do { try await exporter.export(to: output, as: .mp4) }
        catch { Issue.record("Audio fixture export failed: \(error)"); throw error }
        return output
    }

    private func encodedAudio(_ url: URL) async throws -> (Data, [Double]) {
        let asset = AVURLAsset(url: url)
        let track = try #require(await asset.loadTracks(withMediaType: .audio).first)
        let reader = try AVAssetReader(asset: asset)
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: nil)
        reader.add(output)
        #expect(reader.startReading())
        var bytes = Data()
        var times: [Double] = []
        while let sample = output.copyNextSampleBuffer() {
            // AVFoundation can emit a zero-sample drain marker after the final packet.
            guard CMSampleBufferGetNumSamples(sample) > 0 else { continue }
            let buffer = try #require(CMSampleBufferGetDataBuffer(sample))
            var chunk = Data(count: CMBlockBufferGetDataLength(buffer))
            let result = chunk.withUnsafeMutableBytes { destination in
                CMBlockBufferCopyDataBytes(buffer, atOffset: 0, dataLength: destination.count,
                    destination: destination.baseAddress!)
            }
            #expect(result == kCMBlockBufferNoErr)
            bytes.append(chunk)
            times.append(CMSampleBufferGetPresentationTimeStamp(sample).seconds)
        }
        #expect(reader.status == .completed)
        return (bytes, times)
    }

    @Test func passesThroughEncodedAudioAndTimingUnchanged() async throws {
        let directory = try folder()
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("video.mp4")
        try await makeVideo(at: video)
        let input = try await addAudio(to: video, in: directory)
        let output = directory.appendingPathComponent("compressed.mp4")
        do { try await MP4Transcoder.transcode(input: input, output: output, averageBitRate: 115_200) }
        catch { Issue.record("Audio transcode failed: \(error)"); throw error }
        let source = try await encodedAudio(input)
        let result = try await encodedAudio(output)
        #expect(!source.0.isEmpty)
        #expect(result.0 == source.0)
        #expect(result.1.count == source.1.count)
        for (before, after) in zip(source.1, result.1) { #expect(abs(before - after) < 0.001) }
    }

}
