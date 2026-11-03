using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace Snapper.Windows.Support;

/// <summary>Dispatcher-owned registry shared by capture overlays and future window hit-testing.</summary>
internal static class CaptureExclusion
{
    private static readonly HashSet<HWND> windows = [];
    public static IReadOnlySet<HWND> RegisteredWindows { get; } = new ReadOnlySet<HWND>(windows);
    public static bool Exclude(HWND hwnd)
    {
        if (PInvoke.SetWindowDisplayAffinity(hwnd, WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE)) return true;
        Debug.WriteLine($"Snapper: SetWindowDisplayAffinity failed: {Marshal.GetLastWin32Error()}");
        return false;
    }
    public static void Register(HWND hwnd) => windows.Add(hwnd);
    public static void Unregister(HWND hwnd) => windows.Remove(hwnd);
    public static bool IsSnapperWindow(HWND hwnd)
    {
        if (windows.Contains(hwnd)) return true;
        PInvoke.GetWindowThreadProcessId(hwnd, out var process);
        return process == Environment.ProcessId;
    }
}
