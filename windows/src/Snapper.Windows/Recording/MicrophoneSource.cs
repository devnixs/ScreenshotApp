using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Snapper.Core.Recording;
using Snapper.Core.Support;
using Snapper.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.MediaFoundation;
using static Snapper.Windows.Interop.MediaFoundationRuntime;
namespace Snapper.Windows.Recording;

/// <summary>The callback owns each rented chunk and must dispose it after writing or dropping it.</summary>
internal sealed class MicrophoneChunk(long qpc, int frames, byte[] bytes, int length) : IDisposable
{
    public long Qpc { get; } = qpc;
    public int Frames { get; } = frames;
    public byte[] Bytes { get; } = bytes;
    public int Length { get; } = length;
    private int returned;
    public void Dispose() { if (Interlocked.Exchange(ref returned, 1) == 0) ArrayPool<byte>.Shared.Return(Bytes); }
}

/// <summary>Synchronous source reader on its own MTA thread. Shutdown interrupts a blocked ReadSample.</summary>
internal sealed class MicrophoneSource : IDisposable
{
    private readonly IMFMediaSource source;
    private readonly IMFSourceReader reader;
    private readonly object gate = new();
    private readonly AudioClockMapper clock = new();
    private Thread? thread;
    private volatile bool stopping;
    private bool disposed, released;
    public AudioFormatChoice Format { get; }
    private MicrophoneSource(IMFMediaSource source, IMFSourceReader reader, AudioFormatChoice format)
    { this.source = source; this.reader = reader; Format = format; }
    public static MicrophoneOpenResult TryOpen(out MicrophoneSource? result, bool validateFirstRead = false)
    {
        result = null;
        IMFMediaSource? source = null;
        IMFSourceReader? reader = null;
        IMFAttributes? attributes = null;
        var active = false;
        try
        {
            WriterStarted(); active = true;
            PInvoke.MFCreateAttributes(out attributes, 2).ThrowOnFailure();
            attributes.SetGUID(PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID);
            attributes.SetUINT32(PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ROLE, (uint)ERole.eConsole);
            PInvoke.MFCreateDeviceSource(attributes, out source).ThrowOnFailure();
            PInvoke.MFCreateSourceReaderFromMediaSource(source, null, out reader).ThrowOnFailure();
            reader.SetStreamSelection(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS), false);
            reader.SetStreamSelection(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM), true);
            var channels = 0;
            try
            {
                reader.GetNativeMediaType(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out var native);
                try { native.GetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, out var count); channels = (int)count; }
                finally { Release(native); }
            }
            catch (COMException error) when (error.HResult != MicrophoneAccessMapping.AccessDenied) { }
            foreach (var format in MicrophoneAudioFormat.Candidates(channels))
            {
                PInvoke.MFCreateMediaType(out var type).ThrowOnFailure();
                try
                {
                    ConfigurePcm(type, format);
                    reader.SetCurrentMediaType(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM), type);
                    if (validateFirstRead)
                    {
                        var validation = ValidateFirstRead(source ?? throw new InvalidOperationException("Missing microphone source."), reader ?? throw new InvalidOperationException("Missing microphone reader."));
                        if (validation != MicrophoneOpenResult.Opened) return validation;
                    }
                    result = new(source ?? throw new InvalidOperationException("Missing microphone source."), reader ?? throw new InvalidOperationException("Missing microphone reader."), format);
                    source = null; reader = null; active = false;
                    return MicrophoneOpenResult.Opened;
                }
                catch (COMException error) when (error.HResult != MicrophoneAccessMapping.AccessDenied) { }
                finally { Release(type); }
            }
            return MicrophoneOpenResult.Failed;
        }
        catch (Exception error)
        { Debug.WriteLine($"Snapper: Opening microphone: {error.Message}"); return MicrophoneAccessMapping.ClassifyHResult(error.HResult); }
        finally
        {
            Release(reader);
            if (source is not null) { try { source.Shutdown(); } catch (COMException) { } Release(source); }
            Release(attributes);
            if (active) WriterEnded();
        }
    }
    private static MicrophoneOpenResult ValidateFirstRead(IMFMediaSource source, IMFSourceReader reader)
    {
        // An unpackaged app may be denied only by ReadSample. Resolve that before adding an AAC stream.
        // This recording-only validation discards pre-T0 PCM; the Settings probe never reads or requests consent.
        var timeoutGate = new object();
        var complete = false;
        using var timeout = new Timer(_ =>
        {
            lock (timeoutGate)
            {
                if (complete) return;
                try { source.Shutdown(); } catch (COMException) { }
            }
        }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        try
        {
            reader.ReadSample(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out _, out var flags, out _, out var sample);
            Release(sample);
            return (flags & (uint)(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR | MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM)) != 0
                ? MicrophoneOpenResult.Failed : MicrophoneOpenResult.Opened;
        }
        catch (Exception error) { return MicrophoneAccessMapping.ClassifyHResult(error.HResult); }
        finally { lock (timeoutGate) complete = true; }
    }
    internal static void ConfigurePcm(IMFMediaType type, AudioFormatChoice format)
    {
        type.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
        type.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFAudioFormat_PCM);
        type.SetUINT32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)format.SampleRate);
        type.SetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, (uint)format.Channels);
        type.SetUINT32(PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, (uint)format.BitsPerSample);
        type.SetUINT32(PInvoke.MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)format.BlockAlign);
        type.SetUINT32(PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)format.PcmAverageBytesPerSecond);
    }
    public void Start(Action<MicrophoneChunk> onChunk, Action<MicrophoneOpenResult> onEnded)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (thread is not null) throw new InvalidOperationException("Microphone already started.");
            thread = new Thread(() => Read(onChunk, onEnded)) { IsBackground = true, Name = "Snapper microphone" };
            thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
    }
    private unsafe void Read(Action<MicrophoneChunk> onChunk, Action<MicrophoneOpenResult> onEnded)
    {
        var outcome = MicrophoneOpenResult.Failed;
        try
        {
            while (!stopping)
            {
                reader.ReadSample(unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out _, out var flags, out var timestamp, out var sample);
                try
                {
                    if ((flags & (uint)(MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR | MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM)) != 0) break;
                    if (stopping || sample is null || (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_STREAMTICK) != 0) continue;
                    sample.ConvertToContiguousBuffer(out var buffer);
                    try
                    {
                        buffer.Lock(out var pointer, out _, out var length);
                        MicrophoneChunk? chunk = null;
                        try
                        {
                            var frames = checked((int)length) / Format.BlockAlign;
                            if (frames == 0) continue;
                            var bytes = ArrayPool<byte>.Shared.Rent(frames * Format.BlockAlign);
                            new ReadOnlySpan<byte>(pointer, frames * Format.BlockAlign).CopyTo(bytes);
                            var duration = (long)frames * TimeSpan.TicksPerSecond / Format.SampleRate;
                            chunk = new(clock.ToQpc(timestamp, duration, PInvoke.MFGetSystemTime()), frames, bytes, frames * Format.BlockAlign);
                        }
                        finally { buffer.Unlock(); }
                        if (chunk is not null) { try { onChunk(chunk); } catch { chunk.Dispose(); throw; } }
                    }
                    finally { Release(buffer); }
                }
                finally { Release(sample); }
            }
        }
        catch (Exception error)
        { outcome = MicrophoneAccessMapping.ClassifyHResult(error.HResult); if (!stopping) Debug.WriteLine($"Snapper: Microphone ended: {error.Message}"); }
        finally
        {
            if (!stopping) onEnded(outcome);
            lock (gate) { if (disposed) ReleaseNative(); }
        }
    }
    public void Stop()
    {
        stopping = true;
        Thread? active;
        lock (gate) { active = thread; if (released) return; }
        if (active is not null && active != Thread.CurrentThread) active.Join(TimeSpan.FromSeconds(1));
        lock (gate) { if (!released) { try { source.Shutdown(); } catch (COMException) { } } }
        if (active is not null && active != Thread.CurrentThread && active.IsAlive) active.Join(TimeSpan.FromSeconds(1));
    }
    private void ReleaseNative()
    {
        if (released) return;
        released = true;
        try { source.Shutdown(); } catch (COMException) { }
        Release(reader); Release(source); WriterEnded();
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        Stop();
        lock (gate) { if (thread is null || !thread.IsAlive) ReleaseNative(); }
    }
}
