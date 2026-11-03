using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Capture;
using Snapper.Windows.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D11;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
namespace Snapper.Windows.Recording;

internal sealed record WindowRecordingSource(nint Handle, uint ThreadId, PixelRect WindowRect, PixelRect ExtendedFrame);

internal sealed class CaptureFrameSource : IDisposable
{
    private readonly object gate = new();
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly RecordingConfiguration configuration;
    private RecordingFrameClock? clock;
    private readonly Mp4Writer writer;
    private bool stopped;
    private readonly Direct3D11Helper.DeviceLease device;
    private readonly WindowRecordingSource? window;
    private PixelRect lastWindowRect, lastExtendedFrame;
    private SizeInt32 poolSize;
    public event Action? SourceClosed;
    public event Action<Exception>? Failed;
    private CaptureFrameSource(Direct3D11Helper.DeviceLease device, RecordingConfiguration configuration,
        DisplayInfo display, Mp4Writer writer, WindowRecordingSource? window)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new ScreenshotCaptureException(ScreenshotCaptureErrorKind.NotSupported);
        this.configuration = configuration; this.writer = writer; this.device = device; this.window = window;
        if (window is not null)
        {
            if (!PInvoke.IsWindow(new HWND(window.Handle)) || PInvoke.GetWindowThreadProcessId(new HWND(window.Handle), out _) != window.ThreadId)
                throw new WindowUnavailableException();
            lastWindowRect = window.WindowRect; lastExtendedFrame = window.ExtendedFrame;
            // Window-only WGC items do not include the independent pill/border (also WDA_EXCLUDEFROMCAPTURE).
            item = GraphicsCaptureItems.CreateWindowItem(window.Handle);
        }
        else item = GraphicsCaptureItems.CreateMonitorItem(display);
        poolSize = item.Size;
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.WinRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        try { session = pool.CreateCaptureSession(item); session.IsCursorCaptureEnabled = configuration.ShowsCursor; }
        catch { pool.Dispose(); throw; }
        pool.FrameArrived += FrameArrived;
        item.Closed += ItemClosed;
    }
    public static async Task<CaptureFrameSource> CreateAsync(Direct3D11Helper.DeviceLease device,
        RecordingConfiguration configuration, DisplayInfo display, Mp4Writer writer, WindowRecordingSource? window)
    {
        var source = new CaptureFrameSource(device, configuration, display, writer, window);
        try { await GraphicsCaptureItems.ConfigureBorderlessAsync(source.session); return source; }
        catch { source.Dispose(); throw; }
    }
    public void Start() { lock (gate) { if (!stopped) session.StartCapture(); } }
    private void ItemClosed(GraphicsCaptureItem sender, object args) => SourceClosed?.Invoke();
    private unsafe void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Exception? failure = null;
        lock (gate)
        {
            if (stopped) return;
            try
            {
                var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                var content = frame.ContentSize;
                using (frame)
                {
                    if (frame.SystemRelativeTime is { } time && content.Width > 0 && content.Height > 0)
                    {
                        PixelRect? crop;
                        if (window is not null)
                        {
                            if (WindowFrameTracker.TryGetFrames(new HWND(window.Handle), out var currentWindow, out var extended))
                            { lastWindowRect = currentWindow; lastExtendedFrame = extended; }
                            var texture = device.GetTexture(frame.Surface);
                            PixelSize size;
                            try
                            {
                                D3D11_TEXTURE2D_DESC description; texture.GetDesc(&description);
                                size = new(Math.Min(content.Width, (int)description.Width), Math.Min(content.Height, (int)description.Height));
                            }
                            finally { Marshal.ReleaseComObject(texture); }
                            crop = WindowFrameCrop.SourceRect(size, lastWindowRect, lastExtendedFrame);
                        }
                        else crop = configuration.SourceBoxForFrame(content.Width, content.Height);
                        if (crop is { IsEmpty: false } area)
                        {
                            clock ??= new RecordingFrameClock(configuration.FrameIntervalTicks, time.Ticks);
                            if (clock.Accept(time.Ticks) is not null) writer.TryEnqueue(frame.Surface, area, time.Ticks);
                        }
                    }
                }
                // The current frame must release its surface before recreating a resized window pool.
                if (window is not null && content.Width > 0 && content.Height > 0 &&
                    (content.Width != poolSize.Width || content.Height != poolSize.Height))
                {
                    pool.Recreate(device.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
                    poolSize = content;
                }
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
            return clock?.FinalFrameTime(stopTicks) is not null ? stopTicks : null;
        }
    }
    public void Dispose() => Stop(MediaFoundationRuntime.QpcTicks);
}
