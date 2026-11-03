using System.Diagnostics;
using Snapper.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Snapper.Windows.Capture;

internal static class DisplayInfoProvider
{
    public static IReadOnlyList<DisplayInfo> GetCurrent()
    {
        var displays = new List<DisplayInfo>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        Enumerate((monitor, info) =>
        {
            var name = info.szDevice.ToString();
            if (!names.Add(name)) return;
            var result = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _);
            if (result.Failed || dpi == 0)
            {
                Debug.WriteLine($"Snapper: GetDpiForMonitor({name}): {result}");
                dpi = DisplayGeometry.BaseDpi;
            }
            displays.Add(new DisplayInfo(name, Rect(info.monitorInfo.rcMonitor), Rect(info.monitorInfo.rcWork), (int)dpi,
                (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0));
        });
        return displays.OrderByDescending(d => d.IsPrimary).ToArray();
    }

    public static bool TryGetMonitorHandle(DisplayInfo display, out HMONITOR monitor)
    {
        HMONITOR found = default;
        var matched = false;
        Enumerate((handle, info) =>
        {
            if (!matched && info.szDevice.ToString() == display.DeviceName && Rect(info.monitorInfo.rcMonitor) == display.Bounds)
            {
                found = handle;
                matched = true;
            }
        });
        monitor = found;
        return matched;
    }

    private static PixelRect Rect(RECT rect) => PixelRect.FromEdges(rect.left, rect.top, rect.right, rect.bottom);

    private static unsafe void Enumerate(Action<HMONITOR, MONITORINFOEXW> visit)
    {
        MONITORENUMPROC callback = (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { monitorInfo = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)) visit(monitor, info);
            return true;
        };
        PInvoke.EnumDisplayMonitors(default, null, callback, default);
        GC.KeepAlive(callback);
    }
}
