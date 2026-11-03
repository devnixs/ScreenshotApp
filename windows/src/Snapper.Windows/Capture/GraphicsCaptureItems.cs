using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Windows.Interop;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Security.Authorization.AppCapabilityAccess;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT.Graphics.Capture;
using WinRT;

namespace Snapper.Windows.Capture;

internal static class GraphicsCaptureItems
{
    private static readonly object borderGate = new();
    private static Task? borderlessAccess;
    private static int borderWarningLogged;
    public static unsafe GraphicsCaptureItem CreateWindowItem(nint hwnd)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        try
        {
            var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem, Windows SDK.
            try
            {
                factory.CreateForWindow(new HWND(hwnd), &iid, out var pointer);
                try { return MarshalInterface<GraphicsCaptureItem>.FromAbi((nint)pointer); }
                finally { Marshal.Release((nint)pointer); }
            }
            catch (COMException error) when (error.HResult == unchecked((int)0x80070057) || !PInvoke.IsWindow(new HWND(hwnd)))
            { throw new WindowUnavailableException(); }
        }
        finally { if (Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory); }
    }
    public static unsafe GraphicsCaptureItem CreateMonitorItem(DisplayInfo display)
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
    public static async Task ConfigureBorderlessAsync(GraphicsCaptureSession session)
    {
        Task access;
        lock (borderGate) access = borderlessAccess ??= RequestBorderlessAccess();
        await access;
        if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired")) return;
        try { session.IsBorderRequired = false; }
        catch (Exception error) { LogBorderWarning(error.Message); }
    }
}
