using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Capture;
using Snapper.Windows.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
namespace Snapper.Windows.Recording;

internal sealed class CaptureFrameSource : IDisposable
{
    private readonly object gate = new();
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly RecordingConfiguration configuration;
    private readonly RecordingFrameClock clock;
    private readonly Mp4Writer writer;
    private bool stopped;
    public event Action? SourceClosed;
    public event Action<Exception>? Failed;
    private CaptureFrameSource(Direct3D11Helper.DeviceLease device, RecordingConfiguration configuration,
        DisplayInfo display, Mp4Writer writer)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new ScreenshotCaptureException(ScreenshotCaptureErrorKind.NotSupported);
        this.configuration = configuration; this.writer = writer;
        clock = new RecordingFrameClock(configuration.FrameIntervalTicks, writer.TimelineOrigin);
        item = GraphicsCaptureItems.CreateMonitorItem(display);
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.WinRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        try { session = pool.CreateCaptureSession(item); session.IsCursorCaptureEnabled = configuration.ShowsCursor; }
        catch { pool.Dispose(); throw; }
        pool.FrameArrived += FrameArrived;
        item.Closed += ItemClosed;
    }
    public static async Task<CaptureFrameSource> CreateAsync(Direct3D11Helper.DeviceLease device,
        RecordingConfiguration configuration, DisplayInfo display, Mp4Writer writer)
    {
        var source = new CaptureFrameSource(device, configuration, display, writer);
        try { await GraphicsCaptureItems.ConfigureBorderlessAsync(source.session); return source; }
        catch { source.Dispose(); throw; }
    }
    public void Start() { lock (gate) { if (!stopped) session.StartCapture(); } }
    private void ItemClosed(GraphicsCaptureItem sender, object args) => SourceClosed?.Invoke();
    private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Exception? failure = null;
        lock (gate)
        {
            if (stopped) return;
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame?.SystemRelativeTime is not { } time) return;
                if (configuration.SourceBoxForFrame(frame.ContentSize.Width, frame.ContentSize.Height) is not { } crop) return;
                if (clock.Accept(time.Ticks) is not { } sampleTime) return;
                writer.TryEnqueue(frame.Surface, crop, sampleTime);
            }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) Failed?.Invoke(failure);
    }
    public long? Stop(long stopTicks)
    {
        lock (gate)
        {
            if (stopped) return null;
            stopped = true;
            pool.FrameArrived -= FrameArrived; item.Closed -= ItemClosed;
            session.Dispose(); pool.Dispose();
            return clock.FinalFrameTime(stopTicks);
        }
    }
    public void Dispose() => Stop(MediaFoundationRuntime.QpcTicks);
}
