using System.Windows.Threading;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
namespace Snapper.Windows.Recording;

/// <summary>Dispatcher-owned polling. A transient DWM failure never means that a window closed.</summary>
internal sealed class WindowFrameTracker : IDisposable
{
    private readonly HWND hwnd;
    private readonly uint expectedThreadId;
    private readonly Action<WindowTrackingEvent> onEvent;
    private readonly DispatcherTimer timer;
    private WindowTrackingState state;
    private bool disposed;
    public WindowFrameTracker(HWND hwnd, uint expectedThreadId, PixelRect initialFrame, Action<WindowTrackingEvent> onEvent)
    {
        this.hwnd = hwnd; this.expectedThreadId = expectedThreadId; this.onEvent = onEvent;
        state = new(initialFrame);
        timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += Tick;
    }
    public void Start() { timer.Dispatcher.VerifyAccess(); if (disposed || state.IsClosed || timer.IsEnabled) return; timer.Start(); Poll(); }
    public void Stop() { timer.Dispatcher.VerifyAccess(); timer.Stop(); }
    private void Tick(object? sender, EventArgs args) => Poll();
    private void Poll()
    {
        var (next, e) = WindowTrackingReducer.Reduce(state, PollOnce(hwnd, expectedThreadId)); state = next;
        if (e is WindowTrackingEvent.Closed) Stop();
        if (e is not WindowTrackingEvent.Unchanged) onEvent(e);
    }
    public static unsafe WindowPollResult PollOnce(HWND hwnd, uint expectedThreadId)
    {
        var exists = PInvoke.IsWindow(hwnd);
        var thread = PInvoke.GetWindowThreadProcessId(hwnd, out _);
        uint cloaked = 0;
        _ = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint));
        PixelRect? frame = TryGetFrames(hwnd, out _, out var extended) ? extended : null;
        return WindowPollClassifier.Classify(new(exists, thread, PInvoke.IsWindowVisible(hwnd), PInvoke.IsIconic(hwnd), cloaked != 0, frame),
            expectedThreadId, DisplayInfoProvider.GetCurrent());
    }
    public static unsafe bool TryGetFrames(HWND hwnd, out PixelRect window, out PixelRect extended)
    {
        window = extended = default;
        if (!PInvoke.GetWindowRect(hwnd, out var raw)) return false;
        RECT bounds;
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &bounds, (uint)sizeof(RECT)).Failed) return false;
        window = PixelRect.FromEdges(raw.left, raw.top, raw.right, raw.bottom);
        extended = PixelRect.FromEdges(bounds.left, bounds.top, bounds.right, bounds.bottom);
        return !window.IsEmpty && !extended.IsEmpty;
    }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); timer.Tick -= Tick; }
}
