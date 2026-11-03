using System.Diagnostics;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
namespace Snapper.Windows.Capture;

internal static class WindowCandidateProvider
{
    public static IReadOnlyList<WindowCandidate> Snapshot()
    {
        var candidates = new List<WindowCandidate>();
        // EnumWindows order is front to back. A disappearing HWND must never abort enumeration.
        PInvoke.EnumWindows((hwnd, parameter) =>
        {
            try
            {
                if (!PInvoke.IsWindowVisible(hwnd) || !TryGetFrames(hwnd, out _, out var frame)) return true;
                Span<char> name = stackalloc char[256];
                var length = PInvoke.GetClassName(hwnd, name);
                PInvoke.GetWindowThreadProcessId(hwnd, out var pid);
                var tool = (PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) &
                    (nint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0;
                candidates.Add(new WindowCandidate(hwnd, frame, pid, name[..length].ToString(), true,
                    PInvoke.IsIconic(hwnd), IsCloaked(hwnd), tool, null));
            }
            catch (Exception error) { Debug.WriteLine($"Snapper: Skipping disappearing window: {error.Message}"); }
            return true;
        }, default);
        return candidates;
    }
    private static unsafe bool IsCloaked(HWND hwnd)
    {
        uint value = 0;
        return PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &value, sizeof(uint)).Succeeded && value != 0;
    }
    /// <summary>Fresh physical bounds, safe on capture worker threads as well as the dispatcher.</summary>
    public static unsafe bool TryGetFrames(HWND hwnd, out PixelRect window, out PixelRect extended)
    {
        window = extended = default;
        if (!PInvoke.GetWindowRect(hwnd, out var rect)) return false;
        window = PixelRect.FromEdges(rect.left, rect.top, rect.right, rect.bottom);
        RECT frame;
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT)).Failed)
            frame = rect;
        extended = PixelRect.FromEdges(frame.left, frame.top, frame.right, frame.bottom);
        return !window.IsEmpty && !extended.IsEmpty;
    }
}
