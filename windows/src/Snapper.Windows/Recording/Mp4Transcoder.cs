using System.Runtime.InteropServices;
using Snapper.Core.Recording;
using Snapper.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
using static Snapper.Windows.Interop.MediaFoundationRuntime;
namespace Snapper.Windows.Recording;

/// <summary>Offline MTA conversion. Reader samples, including encoded AAC, retain their time and duration.</summary>
internal static class Mp4Transcoder
{
    public static Task TranscodeAsync(string input, string output, int averageBitRate, CancellationToken token) =>
        Task.Run(() => Transcode(input, output, averageBitRate, token), token);
    private static void Transcode(string input, string output, int averageBitRate, CancellationToken token)
    {
        IMFSourceReader? reader = null;
        IMFSinkWriter? writer = null;
        IMFAttributes? readerAttributes = null, writerAttributes = null, parameters = null;
        IMFMediaType? requested = null, video = null, target = null, audio = null, audioTarget = null;
        WriterStarted();
        try
        {
            token.ThrowIfCancellationRequested();
            PInvoke.MFCreateAttributes(out readerAttributes, 2).ThrowOnFailure();
            readerAttributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            readerAttributes.SetUINT32(PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            PInvoke.MFCreateSourceReaderFromURL(input, readerAttributes, out reader).ThrowOnFailure();
            reader.SetStreamSelection(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS), false);
            const uint videoSelector = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            const uint audioSelector = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
            reader.SetStreamSelection(videoSelector, true);
            PInvoke.MFCreateMediaType(out requested).ThrowOnFailure();
            requested.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            requested.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_NV12);
            reader.SetCurrentMediaType(videoSelector, requested);
            reader.GetCurrentMediaType(videoSelector, out video);
            video.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out var size);
            var width = checked((int)(size >> 32)); var height = checked((int)(size & uint.MaxValue));
            // This exception alone means no audio stream; decoder/read errors must remain conversion failures.
            try { reader.GetNativeMediaType(audioSelector, 0, out audio); }
            catch (COMException error) when (unchecked((uint)error.HResult) == 0xC00D36B3) { } // MF_E_INVALIDSTREAMNUMBER, mferror.h.
            if (audio is not null)
            {
                audio.GetGUID(PInvoke.MF_MT_SUBTYPE, out var subtype);
                if (subtype != PInvoke.MFAudioFormat_AAC) throw new InvalidOperationException("The recording's encoded audio couldn't be preserved.");
                reader.SetStreamSelection(audioSelector, true);
                reader.SetCurrentMediaType(audioSelector, audio);
            }
            PInvoke.MFCreateAttributes(out writerAttributes, 3).ThrowOnFailure();
            writerAttributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            writerAttributes.SetUINT32(PInvoke.MF_SINK_WRITER_DISABLE_THROTTLING, 1);
            writerAttributes.SetGUID(PInvoke.MF_TRANSCODE_CONTAINERTYPE, PInvoke.MFTranscodeContainerType_MPEG4);
            PInvoke.MFCreateSinkWriterFromURL(output, null, writerAttributes, out writer).ThrowOnFailure();
            PInvoke.MFCreateMediaType(out target).ThrowOnFailure();
            target.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
            target.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_H264);
            SetSize(target, PInvoke.MF_MT_FRAME_SIZE, width, height);
            SetSize(target, PInvoke.MF_MT_FRAME_RATE, 30, 1);
            SetSize(target, PInvoke.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            target.SetUINT32(PInvoke.MF_MT_INTERLACE_MODE, (uint)MFVideoInterlaceMode.MFVideoInterlace_Progressive);
            target.SetUINT32(PInvoke.MF_MT_AVG_BITRATE, checked((uint)averageBitRate));
            target.SetUINT32(PInvoke.MF_MT_MPEG2_PROFILE, (uint)eAVEncH264VProfile.eAVEncH264VProfile_High);
            writer.AddStream(target, out var videoStream);
            PInvoke.MFCreateAttributes(out parameters, 2).ThrowOnFailure();
            parameters.SetUINT32(typeof(CODECAPI_AVEncCommonRateControlMode).GUID, (uint)eAVEncCommonRateControlMode.eAVEncCommonRateControlMode_UnconstrainedVBR);
            parameters.SetUINT32(typeof(CODECAPI_AVEncCommonMeanBitRate).GUID, checked((uint)averageBitRate));
            try { writer.SetInputMediaType(videoStream, video, parameters); }
            catch (COMException) { writer.SetInputMediaType(videoStream, video, null); } // MF_MT_AVG_BITRATE still supplies the requested rate.
            uint audioStream = 0;
            if (audio is not null)
            {
                PInvoke.MFCreateMediaType(out audioTarget).ThrowOnFailure();
                audio.CopyAllItems(audioTarget);
                writer.AddStream(audioTarget, out audioStream);
                // Sink Writer requires both formats. Identical compressed AAC types remux, without an encoder.
                // https://learn.microsoft.com/windows/win32/medfound/using-the-sink-writer
                writer.SetInputMediaType(audioStream, audio, null);
            }
            writer.BeginWriting();
            var progress = new TranscodeProgress(audio is not null);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                reader.ReadSample(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ANY_STREAM), 0,
                    out var stream, out var flags, out var timestamp, out var sample);
                try
                {
                    if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
                        throw new InvalidOperationException("The recording couldn't be read.");
                    if ((flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                        throw new InvalidOperationException("The recording format changed during compression.");
                    // FIRST_* selectors are resolved to actual indices by MF; inspect each selected stream's major type.
                    reader.GetCurrentMediaType(stream, out var current);
                    Guid major;
                    try { current.GetGUID(PInvoke.MF_MT_MAJOR_TYPE, out major); }
                    finally { Release(current); }
                    var kind = major == PInvoke.MFMediaType_Video ? TranscodeStream.Video : TranscodeStream.Audio;
                    var destination = kind == TranscodeStream.Video ? videoStream : audioStream;
                    var eos = (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0;
                    var step = progress.OnRead(kind, eos, (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_STREAMTICK) != 0, sample is not null);
                    if (step == TranscodeStep.WriteSample) writer.WriteSample(destination, sample ?? throw new InvalidOperationException("Missing media sample."));
                    else if (step == TranscodeStep.SendStreamTick) writer.SendStreamTick(destination, timestamp);
                    // Deselect exhausted streams so ANY_STREAM cannot repeatedly return their EOS ahead of live audio.
                    if (eos) reader.SetStreamSelection(stream, false);
                    if (step == TranscodeStep.Finish) break;
                    if (eos && progress.OnRead(kind, false, false, false) == TranscodeStep.Finish) break;
                }
                finally { Release(sample); }
            }
            if (progress.FailureMessage is { } message) throw new InvalidOperationException(message);
            token.ThrowIfCancellationRequested();
            writer.Finalize();
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            Release(audioTarget); Release(audio); Release(target); Release(video); Release(requested);
            Release(parameters); Release(writer); Release(reader); Release(writerAttributes); Release(readerAttributes);
            WriterEnded();
        }
    }
}
