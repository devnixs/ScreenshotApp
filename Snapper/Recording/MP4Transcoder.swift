import AVFoundation

/// Runs on the caller's detached task. Framework objects stay local to this operation.
enum MP4Transcoder {
    nonisolated static func transcode(input: URL, output: URL, averageBitRate: Int) async throws {
        let asset = AVURLAsset(url: input)
        guard let video = try await asset.loadTracks(withMediaType: .video).first else {
            throw RecordingFailure.compression("The recording contains no video track.")
        }
        let size = try await video.load(.naturalSize)
        let transform = try await video.load(.preferredTransform)
        let duration = try await asset.load(.duration)
        let reader = try AVAssetReader(asset: asset)
        let writer = try AVAssetWriter(outputURL: output, fileType: .mp4)
        let videoOutput = AVAssetReaderTrackOutput(track: video, outputSettings: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
        ])
        let videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: Int(size.width), AVVideoHeightKey: Int(size.height),
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: averageBitRate,
                AVVideoExpectedSourceFrameRateKey: MP4Bitrate.framesPerSecond,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel
            ]
        ])
        videoInput.transform = transform
        guard reader.canAdd(videoOutput), writer.canAdd(videoInput) else {
            throw RecordingFailure.compression("The video format couldn't be converted.")
        }
        reader.add(videoOutput)
        writer.add(videoInput)
        var audioOutput: AVAssetReaderTrackOutput?
        var audioInput: AVAssetWriterInput?
        if let audio = try await asset.loadTracks(withMediaType: .audio).first {
            let formats = try await audio.load(.formatDescriptions)
            let source = AVAssetReaderTrackOutput(track: audio, outputSettings: nil)
            let destination = AVAssetWriterInput(mediaType: .audio, outputSettings: nil,
                                                sourceFormatHint: formats.first)
            guard reader.canAdd(source), writer.canAdd(destination) else {
                throw RecordingFailure.compression("The recording's audio couldn't be preserved.")
            }
            reader.add(source)
            writer.add(destination)
            audioOutput = source
            audioInput = destination
        }
        do {
            guard writer.startWriting() else { throw writer.error ?? RecordingFailure.compression("Couldn't start compression.") }
            writer.startSession(atSourceTime: .zero)
            guard reader.startReading() else { throw reader.error ?? RecordingFailure.compression("Couldn't read the recording.") }
            var videoFinished = false
            var audioFinished = audioInput == nil
            var frameCount = 0
            // Drain both tracks together so backpressure on either track cannot deadlock the other.
            while !videoFinished || !audioFinished {
                try Task.checkCancellation()
                var progressed = false
                if !videoFinished && videoInput.isReadyForMoreMediaData {
                    if let sample = videoOutput.copyNextSampleBuffer() {
                        guard videoInput.append(sample) else { throw writer.error ?? RecordingFailure.compression("Couldn't compress a video frame.") }
                        frameCount += 1
                    } else {
                        videoFinished = true
                        videoInput.markAsFinished()
                    }
                    progressed = true
                }
                if !audioFinished, let audioInput, let audioOutput, audioInput.isReadyForMoreMediaData {
                    if let sample = audioOutput.copyNextSampleBuffer() {
                        guard audioInput.append(sample) else { throw writer.error ?? RecordingFailure.compression("Couldn't preserve an audio sample.") }
                    } else {
                        audioFinished = true
                        audioInput.markAsFinished()
                    }
                    progressed = true
                }
                if reader.status == .failed { throw reader.error ?? RecordingFailure.compression("Couldn't read the recording.") }
                if writer.status == .failed { throw writer.error ?? RecordingFailure.compression("Couldn't write the compressed recording.") }
                if !progressed { try await Task.sleep(for: .milliseconds(1)) }
            }
            guard frameCount > 0 else { throw RecordingFailure.compression("The recording contains no video frames.") }
            // Preserve the held final frame and VFR duration, rather than deriving time from frame count.
            writer.endSession(atSourceTime: duration)
            await writer.finishWriting()
            guard writer.status == .completed else { throw writer.error ?? RecordingFailure.compression("Couldn't finish compression.") }
        } catch {
            reader.cancelReading()
            writer.cancelWriting()
            try? FileManager.default.removeItem(at: output)
            throw error
        }
    }
}
