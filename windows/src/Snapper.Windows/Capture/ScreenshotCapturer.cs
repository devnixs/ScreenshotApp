using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Windows.Interop;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Security.Authorization.AppCapabilityAccess;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT.Graphics.Capture;
using WinRT;

namespace Snapper.Windows.Capture;

internal sealed class ScreenshotCapturer(Direct3D11Helper d3d)
{
    private static Task? borderlessAccess;
    private static int borderWarningLogged;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    public async Task<CapturedImage> CaptureMonitorRegionAsync(DisplayInfo display, PixelRect selection)
    {
        dispatcher.VerifyAccess();
        if (!GraphicsCaptureSession.IsSupported()) throw new ScreenshotCaptureException(ScreenshotCaptureErrorKind.NotSupported);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var item = CreateMonitorItem(display);
                return await CaptureItemAsync(item, size => CaptureCrop.MonitorLocal(selection, display.Bounds, size), display.Scale);
            }
            catch (COMException error) when (attempt == 0 && unchecked((uint)error.HResult) is 0x887A0005 or 0x887A0007)
            { d3d.Reset(); }
        }
    }
    private static unsafe GraphicsCaptureItem CreateMonitorItem(DisplayInfo display)
    {
        var center = new System.Drawing.Point(display.Bounds.X + display.Bounds.Width / 2, display.Bounds.Y + display.Bounds.Height / 2);
        var monitor = PInvoke.MonitorFromPoint(center, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor == default || !PInvoke.GetMonitorInfo(monitor, &info) ||
            info.rcMonitor.left != display.Bounds.Left || info.rcMonitor.top != display.Bounds.Top ||
            info.rcMonitor.right != display.Bounds.Right || info.rcMonitor.bottom != display.Bounds.Bottom)
            throw new ScreenshotCaptureException(ScreenshotCaptureErrorKind.DisplayUnavailable);
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        try
        {
            var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem, Windows SDK.
            factory.CreateForMonitor(monitor, &iid, out var pointer);
            try { return MarshalInterface<GraphicsCaptureItem>.FromAbi((nint)pointer); }
            finally { Marshal.Release((nint)pointer); }
        }
        finally { if (Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory); }
    }
    private static async Task RequestBorderlessAccess()
    {
        try
        {
            var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            if (status != AppCapabilityAccessStatus.Allowed) LogBorderWarning(status.ToString());
        }
        catch (Exception error) { LogBorderWarning(error.Message); }
    }
    private static void LogBorderWarning(string reason)
    {
        if (Interlocked.Exchange(ref borderWarningLogged, 1) == 0)
            Debug.WriteLine($"Snapper: Capture will retain the system border: {reason}");
    }
    private async Task<CapturedImage> CaptureItemAsync(GraphicsCaptureItem item, Func<PixelSize, PixelRect?> crop, double scale)
    {
        borderlessAccess ??= RequestBorderlessAccess();
        await borderlessAccess;
        using var device = d3d.Acquire();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.WinRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            try { session.IsBorderRequired = false; }
            catch (Exception error) { LogBorderWarning(error.Message); }
        }
        var completion = new TaskCompletionSource<CapturedImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameGate = new object();
        var finished = false;
        void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (frameGate)
            {
                if (finished) return;
                try
                {
                    using var frame = sender.TryGetNextFrame();
                    if (frame is null) return;
                    finished = true;
                    var region = crop(new PixelSize(frame.ContentSize.Width, frame.ContentSize.Height))
                        ?? throw new ScreenshotCaptureException(ScreenshotCaptureErrorKind.DisplayUnavailable);
                    completion.TrySetResult(device.CopySurface(frame.Surface, region, scale));
                }
                catch (Exception error) { finished = true; completion.TrySetException(error); }
            }
        }
        pool.FrameArrived += FrameArrived;
        try
        {
            session.StartCapture();
            try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { throw new ScreenshotCaptureException(ScreenshotCaptureErrorKind.TimedOut); }
        }
        finally
        {
            pool.FrameArrived -= FrameArrived;
            // An event already in flight must finish using its frame/device before disposal.
            lock (frameGate) { finished = true; }
        }
    }
}
