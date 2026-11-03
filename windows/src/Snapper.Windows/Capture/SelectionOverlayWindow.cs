using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Windows.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapper.Windows.Capture;

internal sealed class SelectionOverlayWindow : Window
{
    private readonly SelectionOverlayController controller;
    private readonly DisplayChangeWatcher watcher;
    private readonly OverlaySurface surface = new();
    private HwndSource? source;
    private bool controllerClosed, capturing;
    public DisplayInfo Display { get; }
    public HWND Handle { get; private set; }

    public SelectionOverlayWindow(DisplayInfo display, SelectionOverlayController controller, DisplayChangeWatcher watcher)
    {
        Display = display;
        this.controller = controller;
        this.watcher = watcher;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        Cursor = Cursors.Cross;
        Focusable = true;
        Content = surface;
        SourceInitialized += InitializeSource;
        PreviewKeyDown += KeyPressed;
        Closing += WindowClosing;
        Closed += WindowClosed;
    }

    public void Prepare() => new WindowInteropHelper(this).EnsureHandle();
    private void InitializeSource(object? sender, EventArgs args)
    {
        Handle = new HWND(new WindowInteropHelper(this).Handle);
        source = HwndSource.FromHwnd(Handle);
        source?.AddHook(WindowMessage);
        var style = PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style | (nint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
        CaptureExclusion.Exclude(Handle);
        CaptureExclusion.Register(Handle);
        Position();
    }
    public void Position()
    {
        if (controllerClosed) return;
        var b = Display.Bounds;
        // WinUser.h HWND_TOPMOST pseudo-handle.
        PInvoke.SetWindowPos(Handle, new HWND(-1), b.X, b.Y, b.Width, b.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
    [Conditional("DEBUG")]
    public void CheckPlacement()
    {
        PInvoke.GetWindowRect(Handle, out var r);
        var b = Display.Bounds;
        if (r.left != b.Left || r.top != b.Top || r.right != b.Right || r.bottom != b.Bottom ||
            Math.Abs(VisualTreeHelper.GetDpi(this).DpiScaleX - Display.Scale) > 1e-6)
            Debug.WriteLine($"Snapper: Overlay bounds/DPI mismatch on {Display.DeviceName}: {r}, {VisualTreeHelper.GetDpi(this)}");
    }
    public void SetFrame(OverlayFrame frame) => surface.SetFrame(frame);
    public SizeD MeasureLabel(string text) => surface.MeasureLabel(text);
    public void CloseFromController()
    {
        controllerClosed = true;
        if (capturing) { capturing = false; PInvoke.ReleaseCapture(); }
        Close();
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (controllerClosed) return 0;
        if (message == PInvoke.WM_DPICHANGED)
        {
            watcher.Recheck();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Position));
        }
        else if (message == PInvoke.WM_CAPTURECHANGED)
        {
            if (capturing) { capturing = false; controller.CaptureLost(); }
        }
        else if (message is (int)PInvoke.WM_LBUTTONDOWN or (int)PInvoke.WM_MOUSEMOVE or (int)PInvoke.WM_LBUTTONUP)
        {
            // PMv2 client coordinates are signed physical-pixel offsets, including while captured outside this monitor.
            var point = new PointD(Display.Bounds.X + (short)((long)lParam & 0xffff),
                Display.Bounds.Y + (short)(((long)lParam >> 16) & 0xffff));
            if (message == PInvoke.WM_LBUTTONDOWN)
            {
                capturing = true;
                PInvoke.SetCapture(Handle);
                controller.MouseDown(point);
            }
            else if (message == PInvoke.WM_LBUTTONUP)
            {
                capturing = false; // ReleaseCapture synchronously sends WM_CAPTURECHANGED; don't reset a completed press.
                PInvoke.ReleaseCapture();
                controller.MouseUp(point);
            }
            else if (((long)wParam & 1) != 0) controller.MouseDragged(point); // MK_LBUTTON
            handled = true;
        }
        return 0;
    }
    private void KeyPressed(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true;
        controller.Cancel();
    }
    private void WindowClosing(object? sender, CancelEventArgs args)
    {
        if (controllerClosed) return;
        args.Cancel = true;
        // Close all overlays after this Closing event has unwound; WPF disallows recursive Close().
        Dispatcher.BeginInvoke(new Action(controller.Cancel));
    }
    private void WindowClosed(object? sender, EventArgs args)
    {
        CaptureExclusion.Unregister(Handle);
        source?.RemoveHook(WindowMessage);
        source = null;
    }

    private sealed class OverlaySurface : FrameworkElement
    {
        private OverlayFrame frame = OverlayFrame.Empty;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));
        private static readonly Brush Hole = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private static readonly Brush Label = new SolidColorBrush(Color.FromArgb(0xbf, 0, 0, 0));
        private static readonly Typeface Font = new("Segoe UI");
        public void SetFrame(OverlayFrame next) { frame = next; InvalidateVisual(); }
        private FormattedText Format(string text) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Font, 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        public SizeD MeasureLabel(string text)
        {
            var formatted = Format(text);
            return new(formatted.Width + 12, formatted.Height + 6);
        }
        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new RectangleGeometry(new Rect(RenderSize));
            if (frame.SelectionDips is not { } r) { dc.DrawGeometry(Dim, null, bounds); return; }
            var selection = new Rect(r.X, r.Y, r.Width, r.Height);
            dc.DrawGeometry(Dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, bounds, new RectangleGeometry(selection)));
            dc.DrawRectangle(Hole, null, selection);
            if (r.Width >= 1 && r.Height >= 1)
            {
                var border = new Rect(r.X + 0.5, r.Y + 0.5, r.Width - 1, r.Height - 1);
                dc.PushGuidelineSet(new GuidelineSet([border.Left, border.Right], [border.Top, border.Bottom]));
                dc.DrawRectangle(null, new Pen(Brushes.White, 1), border);
                dc.Pop();
            }
            if (frame.ReadoutText is not { } text || frame.ReadoutOriginDips is not { } origin) return;
            var formatted = Format(text);
            dc.DrawRoundedRectangle(Label, null, new Rect(origin.X, origin.Y, formatted.Width + 12, formatted.Height + 6), 4, 4);
            dc.DrawText(formatted, new Point(origin.X + 6, origin.Y + 3));
        }
    }
}
