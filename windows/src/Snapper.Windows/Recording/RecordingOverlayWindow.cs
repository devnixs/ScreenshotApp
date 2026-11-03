using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Snapper.Core.Geometry;
using Snapper.Windows.Capture;
using Snapper.Windows.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace Snapper.Windows.Recording;

/// <summary>Shared nonactivating, capture-excluded HWND ownership and physical-pixel placement.</summary>
internal abstract class RecordingOverlayWindow : Window
{
    private readonly DisplayChangeWatcher watcher;
    private readonly bool clickThrough;
    private HwndSource? source;
    private HWND handle;
    private PixelRect? frame;
    private bool closed;
    protected RecordingOverlayWindow(DisplayChangeWatcher watcher, bool clickThrough)
    {
        this.watcher = watcher;
        this.clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        SourceInitialized += InitializeSource;
        Closed += WindowClosed;
    }
    public void SetPixelFrame(PixelRect value)
    {
        if (closed) return;
        frame = value;
        if (handle != default)
            PInvoke.SetWindowPos(handle, new HWND(-1), value.X, value.Y, value.Width, value.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        InvalidateVisual();
    }
    protected PixelRect? PixelFrame => frame;
    private void InitializeSource(object? sender, EventArgs args)
    {
        handle = new HWND(new WindowInteropHelper(this).Handle);
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowMessage);
        var styles = WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST;
        if (clickThrough) styles |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_LAYERED;
        PInvoke.SetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            PInvoke.GetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) | (nint)styles);
        CaptureExclusion.Exclude(handle);
        CaptureExclusion.Register(handle);
        if (frame is { } current) SetPixelFrame(current);
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == PInvoke.WM_MOUSEACTIVATE) { handled = true; return new nint(PInvoke.MA_NOACTIVATE); }
        if (message == PInvoke.WM_DPICHANGED)
        {
            watcher.Recheck();
            // The border may span monitors; its physical frame is authoritative, never the suggested DPI rect.
            if (clickThrough) handled = true;
            Dispatcher.BeginInvoke(() => { if (frame is { } current) SetPixelFrame(current); });
        }
        return 0;
    }
    private void WindowClosed(object? sender, EventArgs args)
    {
        closed = true;
        CaptureExclusion.Unregister(handle);
        source?.RemoveHook(WindowMessage);
        source = null;
        SourceInitialized -= InitializeSource;
        Closed -= WindowClosed;
    }
}
