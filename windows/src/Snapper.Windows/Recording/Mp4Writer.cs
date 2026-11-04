using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Interop;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Media.MediaFoundation;
using static Snapper.Windows.Interop.MediaFoundationRuntime;
namespace Snapper.Windows.Recording;

/// <summary>Owns the sink and its MTA thread. Capture never waits for the encoder's bounded queue.</summary>
internal sealed class Mp4Writer : IDisposable
{
    private readonly string path;
    private readonly RecordingConfiguration configuration;
    private readonly Direct3D11Helper.DeviceLease device;
    private readonly AudioFormatChoice? audio;
    private readonly BlockingCollection<(IMFSample Sample, long Time)> queue = new(8);
    private readonly object sinkGate = new(), allocatorGate = new(), acceptingGate = new();
    private readonly ManualResetEventSlim begin = new(false);
    private readonly TaskCompletionSource<Mp4Writer> created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource began = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private IMFSinkWriter? sink;
    private IMFDXGIDeviceManager? manager;
    private IMFVideoSampleAllocatorEx? allocator;
    private IMFSample? lastSample;
    private ID3D11Texture2D? staging, compositionTexture;
    private LetterboxCompositor? compositor;
    private uint videoStream, audioStream;
    private long audioSamplesWritten, lastVideoTime, lastVideoTick;
    private long originQpc = long.MinValue;
    private Exception? audioFailure;
    private bool finishing;
    private long samplesWritten;
    private long? holdTime;
    private volatile bool abort;
    private bool accepting = true;
    private bool disposed;
    private string startupStage = "MFStartup";
    private bool hardwareAttempt;
    public ManualResetEventSlim Completed { get; } = new(false);
    public long? TimelineOriginQpc { get { var value = Interlocked.Read(ref originQpc); return value == long.MinValue ? null : value; } }
    public bool HasAudio => audio is not null;
    public bool FileHasContent { get { try { return Interlocked.Read(ref samplesWritten) > 0 && new FileInfo(path).Length > 0; } catch (IOException) { return false; } } }
    public event Action? FirstSampleWritten;
    public event Action? Finished;
    public event Action<RecordingFailure, bool>? Failed;
    private Mp4Writer(string path, RecordingConfiguration configuration, Direct3D11Helper.DeviceLease device, AudioFormatChoice? audio)
    {
        this.path = path; this.configuration = configuration; this.device = device; this.audio = audio;
        thread = new Thread(Run) { IsBackground = false, Name = "Snapper MP4 writer" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    public static Task<Mp4Writer> CreateAsync(string path, RecordingConfiguration configuration, Direct3D11Helper.DeviceLease device, AudioFormatChoice? audio = null) =>
        new Mp4Writer(path, configuration, device, audio).created.Task;
    public Task BeginWritingAsync() { begin.Set(); return began.Task; }

    private void Run()
    {
        Exception? failure = null;
        var runtimeActive = false;
        try
        {
            WriterStarted(); runtimeActive = true;
            try { Create(true); }
            catch (Exception error) { LogFailure(error); ResetForFallback(); Create(false); }
            created.TrySetResult(this);
            begin.Wait();
            if (!abort)
            {
                startupStage = "BeginWriting";
                try { lock (sinkGate) RequireSink().BeginWriting(); }
                catch (Exception error) when (allocator is not null)
                {
                    LogFailure(error); ResetForFallback(); Create(false);
                    startupStage = "BeginWriting";
                    lock (sinkGate) RequireSink().BeginWriting();
                }
                began.TrySetResult();
                foreach (var (sample, time) in queue.GetConsumingEnumerable())
                {
                    if (abort) { Release(sample); continue; }
                    try
                    {
                        Write(sample, time);
                        Release(lastSample); lastSample = sample;
                    }
                    catch { Release(sample); throw; }
                }
                if (!abort)
                {
                    if (Volatile.Read(ref audioFailure) is { } error) throw error;
                    if (holdTime is { } time && lastSample is not null) WriteHoldFrame(time);
                    lock (sinkGate)
                    {
                        if (audio is { } format && audioSamplesWritten == 0 && samplesWritten > 0)
                            WriteAudioSample(new(0, 0, Math.Max(1, format.SampleRate / configuration.FramesPerSecond), true), []);
                        finishing = true;
                        startupStage = "Finalize";
                        RequireSink().Finalize();
                    }
                }
            }
        }
        catch (Exception error)
        {
            LogFailure(error);
            failure = error;
            if (!abort && Interlocked.Read(ref samplesWritten) > 0)
                try { lock (sinkGate) { finishing = true; sink?.Finalize(); } } catch (Exception finalize) { Debug.WriteLine($"Snapper: Finalizing partial recording: {finalize.Message}"); }
        }
        finally
        {
            lock (acceptingGate) { accepting = false; queue.CompleteAdding(); }
            while (queue.TryTake(out var queued)) Release(queued.Sample);
            Release(lastSample); lastSample = null;
            Release(staging); staging = null;
            Release(compositionTexture); compositionTexture = null;
            compositor?.Dispose(); compositor = null;
            ReleaseSink();
            if (runtimeActive) WriterEnded();
            Completed.Set();
            // A failed CreateAsync caller has no writer instance to wait on. Publish its
            // failure only after the sink releases its file and stops using the device lease.
            if (failure is not null) { created.TrySetException(failure); began.TrySetException(failure); }
            else if (abort) { created.TrySetCanceled(); began.TrySetCanceled(); }
        }
        // The same writer thread posts first-sample before all completion callbacks; dispatcher FIFO preserves startup order.
        if (abort) return;
        if (failure is null) Finished?.Invoke();
        else Failed?.Invoke(new RecordingFailure(failure.Message), FileHasContent);
    }
    private IMFSinkWriter RequireSink() => sink ?? throw new ObjectDisposedException(nameof(Mp4Writer));
    private void LogFailure(Exception error) => Debug.WriteLine(
        $"Snapper: Recording {startupStage}; hardware={hardwareAttempt}, {configuration}, audioIncluded={audio is not null}, audio={audio}; " +
        $"{error.GetType().FullName}, HRESULT=0x{error.HResult:X8}: {error}");
    private void Create(bool hardware)
    {
        hardwareAttempt = hardware;
        startupStage = "MFCreateAttributes";
        PInvoke.MFCreateAttributes(out var attributes, 3).ThrowOnFailure();
        IMFMediaType? output = null, input = null;
        try
        {
            attributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1u : 0);
            attributes.SetGUID(PInvoke.MF_TRANSCODE_CONTAINERTYPE, PInvoke.MFTranscodeContainerType_MPEG4);
            if (hardware)
            {
                startupStage = "DXGI device manager";
                PInvoke.MFCreateDXGIDeviceManager(out var token, out var createdManager).ThrowOnFailure(); manager = createdManager;
                manager.ResetDevice(device.Device, token);
                attributes.SetUnknown(PInvoke.MF_SINK_WRITER_D3D_MANAGER, manager);
            }
            startupStage = "MFCreateSinkWriterFromURL";
            PInvoke.MFCreateSinkWriterFromURL(path, null, attributes, out var createdSink).ThrowOnFailure(); sink = createdSink;
            startupStage = "output media type attributes";
            PInvoke.MFCreateMediaType(out output).ThrowOnFailure(); ConfigureType(output, PInvoke.MFVideoFormat_H264);
            output.SetUINT32(PInvoke.MF_MT_AVG_BITRATE, (uint)configuration.VideoBitrate);
            output.SetUINT32(PInvoke.MF_MT_MPEG2_PROFILE, (uint)eAVEncH264VProfile.eAVEncH264VProfile_High);
            output.SetUINT32(PInvoke.MF_MT_YUV_MATRIX, (uint)MFVideoTransferMatrix.MFVideoTransferMatrix_BT709);
            startupStage = "AddStream(video)";
            lock (sinkGate) RequireSink().AddStream(output, out videoStream);
            startupStage = "input media type attributes";
            PInvoke.MFCreateMediaType(out input).ThrowOnFailure(); // GPU allocator textures must match WGC B8G8R8A8; RGB32 maps to B8G8R8X8.
            ConfigureType(input, hardware ? PInvoke.MFVideoFormat_ARGB32 : PInvoke.MFVideoFormat_RGB32);
            input.SetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, checked((uint)configuration.PixelWidth * 4));
            startupStage = "SetInputMediaType(video)";
            lock (sinkGate) RequireSink().SetInputMediaType(videoStream, input, null);
            if (hardware)
            {
                startupStage = "video sample allocator creation";
                PInvoke.MFCreateVideoSampleAllocatorEx(out IMFVideoSampleAllocatorEx createdAllocator).ThrowOnFailure(); allocator = createdAllocator;
                allocator.SetDirectXManager(manager ?? throw new InvalidOperationException("Missing device manager."));
                PInvoke.MFCreateAttributes(out var allocatorAttributes, 2).ThrowOnFailure();
                try
                {
                    allocatorAttributes.SetUINT32(PInvoke.MF_SA_D3D11_BINDFLAGS, (uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET);
                    allocatorAttributes.SetUINT32(PInvoke.MF_SA_D3D11_USAGE, (uint)D3D11_USAGE.D3D11_USAGE_DEFAULT);
                    startupStage = "InitializeSampleAllocatorEx";
                    allocator.InitializeSampleAllocatorEx(4, 8, allocatorAttributes, input);
                    // Validate the allocated texture before capture: incompatible GPU copies have no HRESULT.
                    startupStage = "allocator texture validation";
                    allocator.AllocateSample(out var probe);
                    try { ValidateTexture(probe); }
                    finally { Release(probe); }
                }
                finally { Release(allocatorAttributes); }
            }
            if (audio is { } format) AddAudioStream(format);
        }
        finally { Release(input); Release(output); Release(attributes); }
    }
    private void AddAudioStream(AudioFormatChoice format)
    {
        startupStage = "audio media types and stream negotiation";
        PInvoke.MFCreateMediaType(out var output).ThrowOnFailure();
        IMFMediaType? input = null;
        try
        {
            output.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Audio);
            output.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFAudioFormat_AAC);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)format.SampleRate);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS, (uint)format.Channels);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            output.SetUINT32(PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)format.AacAverageBytesPerSecond);
            output.SetUINT32(PInvoke.MF_MT_AAC_PAYLOAD_TYPE, 0);
            output.SetUINT32(PInvoke.MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29);
            lock (sinkGate) RequireSink().AddStream(output, out audioStream);
            PInvoke.MFCreateMediaType(out input).ThrowOnFailure();
            MicrophoneSource.ConfigurePcm(input, format);
            lock (sinkGate) RequireSink().SetInputMediaType(audioStream, input, null);
        }
        finally { Release(input); Release(output); }
    }
    private static unsafe void ValidateTexture(IMFSample sample)
    {
        var texture = SampleTexture(sample);
        try
        {
            D3D11_TEXTURE2D_DESC description;
            texture.GetDesc(&description);
            if (description.Format != DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM)
                throw new NotSupportedException("The encoder allocated an incompatible GPU pixel format.");
        }
        finally { Release(texture); }
    }
    private void ConfigureType(IMFMediaType type, Guid subtype)
    {
        type.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
        type.SetGUID(PInvoke.MF_MT_SUBTYPE, subtype);
        SetSize(type, PInvoke.MF_MT_FRAME_SIZE, configuration.PixelWidth, configuration.PixelHeight);
        SetSize(type, PInvoke.MF_MT_FRAME_RATE, configuration.FramesPerSecond, 1);
        SetSize(type, PInvoke.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
        type.SetUINT32(PInvoke.MF_MT_INTERLACE_MODE, (uint)MFVideoInterlaceMode.MFVideoInterlace_Progressive);
    }
    private void ResetForFallback() { ReleaseSink(); if (File.Exists(path)) File.Delete(path); }
    private void ReleaseSink()
    {
        lock (allocatorGate) { Release(allocator); allocator = null; }
        lock (sinkGate) { Release(sink); sink = null; Release(manager); manager = null; }
    }
    private void Write(IMFSample sample, long time)
    {
        startupStage = "video sample metadata";
        var relative = time - (TimelineOriginQpc ?? time);
        sample.SetSampleTime(relative); sample.SetSampleDuration((long)Math.Round(configuration.FrameIntervalTicks));
        lock (sinkGate)
        {
            if (allocator is not null)
            {
                // GPU copies do not update IMFMediaBuffer's valid-data length. Allocated
                // frames start at zero, which makes WriteSample reject them with E_INVALIDARG.
                // This applies to both captured frames and the final held frame.
                sample.GetBufferByIndex(0, out var buffer);
                try { buffer.GetMaxLength(out var length); buffer.SetCurrentLength(length); }
                finally { Release(buffer); }
            }
            startupStage = "WriteSample(video)";
            RequireSink().WriteSample(videoStream, sample);
            Interlocked.CompareExchange(ref originQpc, time, long.MinValue);
            lastVideoTime = relative;
        }
        if (Interlocked.Increment(ref samplesWritten) == 1) FirstSampleWritten?.Invoke();
    }
    public void WriteAudio(AudioPlacement placement, ReadOnlySpan<byte> pcm)
    {
        if (audio is not { } format) return;
        try
        {
            lock (sinkGate)
            {
                if (abort || finishing || sink is null || Completed.IsSet || Volatile.Read(ref audioFailure) is not null) return;
                // Long gaps are represented by short silence buffers, never a multi-minute allocation.
                var remaining = placement.FrameCount;
                var written = 0;
                while (remaining > 0)
                {
                    var frames = placement.IsSilence ? Math.Min(remaining, format.SampleRate / 10) : remaining;
                    var offset = (long)written * TimeSpan.TicksPerSecond / format.SampleRate;
                    WriteAudioSample(new(placement.OutputTimeTicks + offset, placement.SkipFrames + written, frames, placement.IsSilence), pcm);
                    remaining -= frames; written += frames;
                }
            }
        }
        catch (Exception error)
        {
            lock (acceptingGate)
            {
                if (!abort && !Completed.IsSet) { audioFailure = error; accepting = false; queue.CompleteAdding(); }
            }
        }
    }
    private void WriteAudioSample(AudioPlacement placement, ReadOnlySpan<byte> pcm)
    {
        if (audio is not { } format) return;
        var length = checked(placement.FrameCount * format.BlockAlign);
        var bytes = placement.IsSilence ? new byte[length] : pcm.Slice(checked(placement.SkipFrames * format.BlockAlign), length).ToArray();
        var sample = MemorySample(bytes);
        try
        {
            var duration = (long)placement.FrameCount * TimeSpan.TicksPerSecond / format.SampleRate;
            sample.SetSampleTime(placement.OutputTimeTicks); sample.SetSampleDuration(duration);
            var end = placement.OutputTimeTicks + duration;
            // Advancing video ticks prevents the MPEG-4 sink from buffering unbounded audio on static screens.
            if (end - Math.Max(lastVideoTime, lastVideoTick) > TimeSpan.TicksPerSecond / 2)
            { RequireSink().SendStreamTick(videoStream, end); lastVideoTick = end; }
            RequireSink().WriteSample(audioStream, sample);
            audioSamplesWritten++;
        }
        finally { Release(sample); }
    }
    public bool TryEnqueue(IDirect3DSurface surface, PixelRect crop, long time)
    {
        lock (acceptingGate)
        {
            if (!accepting || abort || queue.Count >= queue.BoundedCapacity) return false;
            IMFSample? sample = null;
            try
            {
                sample = CopyFrame(surface, crop);
                if (sample is null) return false;
                if (queue.TryAdd((sample, time))) { sample = null; return true; }
                return false;
            }
            finally { Release(sample); }
        }
    }
    private unsafe IMFSample? CopyFrame(IDirect3DSurface surface, PixelRect crop)
    {
        lock (allocatorGate)
        {
            if (allocator is null)
            {
                return CopyCpuFrame(surface, crop);
            }
            IMFSample sample;
            try { allocator.AllocateSample(out sample); }
            catch (COMException error) when (error.HResult == (int)global::Windows.Win32.Foundation.HRESULT.MF_E_SAMPLEALLOCATOR_EMPTY) { return null; }
            try
            {
                var source = device.GetTexture(surface); var target = SampleTexture(sample);
                try
                {
                    var box = new D3D11_BOX { left = (uint)crop.Left, top = (uint)crop.Top, right = (uint)crop.Right, bottom = (uint)crop.Bottom, back = 1 };
                    if (configuration.FollowsWindow)
                    {
                        var fit = LetterboxFit.Fit(crop.Size, new(configuration.PixelWidth, configuration.PixelHeight))
                            ?? throw new InvalidOperationException("The window frame is empty.");
                        (compositor ??= new(device)).Compose(source, target, crop, fit);
                    }
                    else lock (device.ContextLock) device.Context.CopySubresourceRegion(target, 0, 0, 0, 0, source, 0, &box);
                }
                finally { Release(target); Release(source); }
                return sample;
            }
            catch { Release(sample); throw; }
        }
    }
    private unsafe IMFSample CopyCpuFrame(IDirect3DSurface surface, PixelRect crop)
    {
        lock (device.ContextLock)
        {
            staging ??= device.CreateStagingTexture(configuration.PixelWidth, configuration.PixelHeight);
            var source = device.GetTexture(surface);
            try
            {
                var box = new D3D11_BOX { left = (uint)crop.Left, top = (uint)crop.Top, right = (uint)crop.Right, bottom = (uint)crop.Bottom, back = 1 };
                if (configuration.FollowsWindow)
                {
                    compositionTexture ??= LetterboxCompositor.CreateOutputTexture(device, configuration.PixelWidth, configuration.PixelHeight);
                    var fit = LetterboxFit.Fit(crop.Size, new(configuration.PixelWidth, configuration.PixelHeight))
                        ?? throw new InvalidOperationException("The window frame is empty.");
                    (compositor ??= new(device)).Compose(source, compositionTexture, crop, fit);
                    device.Context.CopyResource(staging, compositionTexture);
                }
                else device.Context.CopySubresourceRegion(staging, 0, 0, 0, 0, source, 0, &box);
                D3D11_MAPPED_SUBRESOURCE mapped;
                device.Context.Map(staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
                try
                {
                    PInvoke.MFCreateSample(out var sample).ThrowOnFailure();
                    try
                    {
                        var length = checked(configuration.PixelWidth * configuration.PixelHeight * 4);
                        PInvoke.MFCreateMemoryBuffer((uint)length, out var buffer).ThrowOnFailure();
                        try
                        {
                            buffer.Lock(out var destination);
                            try
                            {
                                var sourceLength = checked((configuration.PixelHeight - 1) * (int)mapped.RowPitch + configuration.PixelWidth * 4);
                                CaptureCrop.CopyRows(new ReadOnlySpan<byte>(mapped.pData, sourceLength), (int)mapped.RowPitch,
                                    configuration.PixelWidth, configuration.PixelHeight, new Span<byte>(destination, length));
                            }
                            finally { buffer.Unlock(); }
                            buffer.SetCurrentLength((uint)length); sample.AddBuffer(buffer);
                        }
                        finally { Release(buffer); }
                        return sample;
                    }
                    catch { Release(sample); throw; }
                }
                finally { device.Context.Unmap(staging, 0); }
            }
            finally { Release(source); }
        }
    }
    private static unsafe ID3D11Texture2D SampleTexture(IMFSample sample)
    {
        sample.GetBufferByIndex(0, out var buffer);
        try
        {
            var guid = typeof(ID3D11Texture2D).GUID;
            ((IMFDXGIBuffer)buffer).GetResource(&guid, out var pointer);
            try { return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)pointer); }
            finally { Marshal.Release((nint)pointer); }
        }
        finally { Release(buffer); }
    }
    private static unsafe IMFSample MemorySample(ReadOnlySpan<byte> bytes)
    {
        PInvoke.MFCreateSample(out var sample).ThrowOnFailure();
        try
        {
            PInvoke.MFCreateMemoryBuffer((uint)bytes.Length, out var buffer).ThrowOnFailure();
            try
            {
                buffer.Lock(out var pointer);
                try { bytes.CopyTo(new Span<byte>(pointer, bytes.Length)); }
                finally { buffer.Unlock(); }
                buffer.SetCurrentLength((uint)bytes.Length); sample.AddBuffer(buffer);
            }
            finally { Release(buffer); }
            return sample;
        }
        catch { Release(sample); throw; }
    }
    private unsafe void WriteHoldFrame(long time)
    {
        var previous = lastSample;
        if (previous is null) return;
        IMFSample? hold = null;
        try
        {
            if (allocator is { } gpuAllocator)
            {
                var until = Stopwatch.StartNew();
                while (hold is null)
                {
                    try { lock (allocatorGate) gpuAllocator.AllocateSample(out hold); }
                    catch (COMException error) when (error.HResult == (int)global::Windows.Win32.Foundation.HRESULT.MF_E_SAMPLEALLOCATOR_EMPTY && until.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(5); }
                }
                var source = SampleTexture(previous); var target = SampleTexture(hold);
                try { lock (device.ContextLock) device.Context.CopyResource(target, source); }
                finally { Release(target); Release(source); }
            }
            else
            {
                previous.ConvertToContiguousBuffer(out var buffer);
                try
                {
                    buffer.Lock(out var pointer, out _, out var length);
                    try { hold = MemorySample(new ReadOnlySpan<byte>(pointer, (int)length)); }
                    finally { buffer.Unlock(); }
                }
                finally { Release(buffer); }
            }
            Write(hold, time);
        }
        finally { Release(hold); }
    }
    public void RequestFinish(long? holdFrameSampleTime)
    {
        lock (acceptingGate)
        {
            if (!accepting) return;
            accepting = false; holdTime = holdFrameSampleTime; queue.CompleteAdding();
        }
    }
    public void Abort()
    {
        lock (acceptingGate) { abort = true; accepting = false; queue.CompleteAdding(); begin.Set(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!Completed.IsSet) Abort();
        // A hung native Finalize must not make disposal block the dispatcher or release an in-use device.
    }
}
