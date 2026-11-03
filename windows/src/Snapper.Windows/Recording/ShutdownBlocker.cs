using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Snapper.Windows.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace Snapper.Windows.Recording;

/// <summary>A shown, transparent top-level window receives session-end messages and owns the shared block reason.</summary>
internal sealed class ShutdownBlocker : IDisposable
{
    private readonly HwndSource source;
    private readonly Func<bool> canEndSession;
    private readonly Action sessionEnding;
    private bool disposed;
    public ShutdownBlocker(Func<bool> canEndSession, Action sessionEnding)
    {
        this.canEndSession = canEndSession; this.sessionEnding = sessionEnding;
        source = new HwndSource(new HwndSourceParameters("Snapper session end")
        {
            WindowStyle = unchecked((int)(WINDOW_STYLE.WS_POPUP | WINDOW_STYLE.WS_VISIBLE)),
            ExtendedWindowStyle = (int)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_LAYERED),
            Width = 1, Height = 1, PositionX = -32000, PositionY = -32000
        });
        var hwnd = new HWND(source.Handle);
        PInvoke.SetLayeredWindowAttributes(hwnd, default, 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        PInvoke.SetWindowPos(hwnd, default, -32000, -32000, 1, 1, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        CaptureExclusion.Register(hwnd); CaptureExclusion.Exclude(hwnd);
        source.AddHook(Message);
    }
    public void SetReason(string? reason)
    {
        if (disposed) return;
        var hwnd = new HWND(source.Handle);
        var success = reason is null ? PInvoke.ShutdownBlockReasonDestroy(hwnd) : PInvoke.ShutdownBlockReasonCreate(hwnd, reason);
        if (!success) Debug.WriteLine($"Snapper: Shutdown block reason: {Marshal.GetLastWin32Error()}");
    }
    private nint Message(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == PInvoke.WM_QUERYENDSESSION)
        {
            handled = true;
            var canEnd = canEndSession();
            return canEnd || (lParam.ToInt64() & PInvoke.ENDSESSION_CRITICAL) != 0 ? 1 : 0;
        }
        if (message == PInvoke.WM_ENDSESSION && wParam != 0) { handled = true; sessionEnding(); }
        return 0;
    }
    public void Dispose()
    {
        if (disposed) return;
        SetReason(null); disposed = true;
        CaptureExclusion.Unregister(new HWND(source.Handle)); source.RemoveHook(Message); source.Dispose();
    }
}
