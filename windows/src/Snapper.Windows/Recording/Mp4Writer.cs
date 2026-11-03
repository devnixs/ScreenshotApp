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
    private uint videoStream;
    private long samplesWritten;
    private long? holdTime;
    private volatile bool abort;
    private bool accepting = true;
    private bool disposed;
    public ManualResetEventSlim Completed { get; } = new(false);
    public long TimelineOrigin { get; private set; }
    public bool FileHasContent { get { try { return Interlocked.Read(ref samplesWritten) > 0 && new FileInfo(path).Length > 0; } catch (IOException) { return false; } } }
    public event Action? FirstSampleWritten;
    public event Action? Finished;
    public event Action<RecordingFailure, bool>? Failed;
    private Mp4Writer(string path, RecordingConfiguration configuration, Direct3D11Helper.DeviceLease device)
    {
        this.path = path; this.configuration = configuration; this.device = device;
        thread = new Thread(Run) { IsBackground = false, Name = "Snapper MP4 writer" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    public static Task<Mp4Writer> CreateAsync(string path, RecordingConfiguration configuration, Direct3D11Helper.DeviceLease device) =>
        new Mp4Writer(path, configuration, device).created.Task;
    public Task BeginWritingAsync() { begin.Set(); return began.Task; }

    private void Run()
    {
        Exception? failure = null;
        var runtimeActive = false;
        try
        {
            WriterStarted(); runtimeActive = true;
            try { Create(true); }
            catch (Exception error) { Debug.WriteLine($"Snapper: Using CPU encoder: {error.Message}"); ResetForFallback(); Create(false); }
            created.TrySetResult(this);
            begin.Wait();
            if (!abort)
            {
                try { lock (sinkGate) RequireSink().BeginWriting(); }
                catch when (allocator is not null) { ResetForFallback(); Create(false); lock (sinkGate) RequireSink().BeginWriting(); }
                TimelineOrigin = QpcTicks;
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
                    if (holdTime is { } time && lastSample is not null) WriteHoldFrame(time);
                    lock (sinkGate) RequireSink().Finalize();
                }
            }
        }
        catch (Exception error)
        {
            failure = error;
            created.TrySetException(error); began.TrySetException(error);
            if (!abort && Interlocked.Read(ref samplesWritten) > 0)
                try { lock (sinkGate) sink?.Finalize(); } catch (Exception finalize) { Debug.WriteLine($"Snapper: Finalizing partial recording: {finalize.Message}"); }
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
        }
        // The same writer thread posts first-sample before all completion callbacks; dispatcher FIFO preserves startup order.
        if (abort) return;
        if (failure is null) Finished?.Invoke();
        else Failed?.Invoke(new RecordingFailure(failure.Message), FileHasContent);
    }
    private IMFSinkWriter RequireSink() => sink ?? throw new ObjectDisposedException(nameof(Mp4Writer));
    private void Create(bool hardware)
    {
        PInvoke.MFCreateAttributes(out var attributes, 3).ThrowOnFailure();
        IMFMediaType? output = null, input = null;
        try
        {
            attributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1u : 0);
            attributes.SetGUID(PInvoke.MF_TRANSCODE_CONTAINERTYPE, PInvoke.MFTranscodeContainerType_MPEG4);
            if (hardware)
            {
                PInvoke.MFCreateDXGIDeviceManager(out var token, out var createdManager).ThrowOnFailure(); manager = createdManager;
                manager.ResetDevice(device.Device, token);
                attributes.SetUnknown(PInvoke.MF_SINK_WRITER_D3D_MANAGER, manager);
            }
            PInvoke.MFCreateSinkWriterFromURL(path, null, attributes, out var createdSink).ThrowOnFailure(); sink = createdSink;
            PInvoke.MFCreateMediaType(out output).ThrowOnFailure(); ConfigureType(output, PInvoke.MFVideoFormat_H264);
            output.SetUINT32(PInvoke.MF_MT_AVG_BITRATE, (uint)configuration.VideoBitrate);
            output.SetUINT32(PInvoke.MF_MT_MPEG2_PROFILE, (uint)eAVEncH264VProfile.eAVEncH264VProfile_High);
            output.SetUINT32(PInvoke.MF_MT_YUV_MATRIX, (uint)MFVideoTransferMatrix.MFVideoTransferMatrix_BT709);
            lock (sinkGate) RequireSink().AddStream(output, out videoStream);
            PInvoke.MFCreateMediaType(out input).ThrowOnFailure(); // GPU allocator textures must match WGC B8G8R8A8; RGB32 maps to B8G8R8X8.
            ConfigureType(input, hardware ? PInvoke.MFVideoFormat_ARGB32 : PInvoke.MFVideoFormat_RGB32);
            input.SetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, checked((uint)configuration.PixelWidth * 4));
            lock (sinkGate) RequireSink().SetInputMediaType(videoStream, input, null);
            if (hardware)
            {
                PInvoke.MFCreateVideoSampleAllocatorEx(out IMFVideoSampleAllocatorEx createdAllocator).ThrowOnFailure(); allocator = createdAllocator;
                allocator.SetDirectXManager(manager ?? throw new InvalidOperationException("Missing device manager."));
                PInvoke.MFCreateAttributes(out var allocatorAttributes, 2).ThrowOnFailure();
                try
                {
                    allocatorAttributes.SetUINT32(PInvoke.MF_SA_D3D11_BINDFLAGS, (uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET);
                    allocatorAttributes.SetUINT32(PInvoke.MF_SA_D3D11_USAGE, (uint)D3D11_USAGE.D3D11_USAGE_DEFAULT);
                    allocator.InitializeSampleAllocatorEx(4, 8, allocatorAttributes, input);
                    // Validate the allocated texture before capture: incompatible GPU copies have no HRESULT.
                    allocator.AllocateSample(out var probe);
                    try { ValidateTexture(probe); }
                    finally { Release(probe); }
                }
                finally { Release(allocatorAttributes); }
            }
        }
        finally { Release(input); Release(output); Release(attributes); }
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
        sample.SetSampleTime(time); sample.SetSampleDuration((long)Math.Round(configuration.FrameIntervalTicks));
        lock (sinkGate) RequireSink().WriteSample(videoStream, sample);
        if (Interlocked.Increment(ref samplesWritten) == 1) FirstSampleWritten?.Invoke();
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
