using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Core.Settings;
using Snapper.Windows.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
namespace Snapper.Windows.Annotate;
internal sealed class AnnotationWindowManager(AppSettings settings)
{
    private readonly List<AnnotationWindow> windows = [];
    public IReadOnlyList<AnnotationWindow> Windows => windows.AsReadOnly();
    public void Open(CapturedImage captured, DisplayInfo? captureDisplay, DateTime captureDate)
    {
        var displays = DisplayInfoProvider.GetCurrent();
        var target = displays.FirstOrDefault(d => d.DeviceName == captureDisplay?.DeviceName) ?? DisplayGeometry.PrimaryDisplay(displays)
            ?? throw new InvalidOperationException("No display is available for the annotation window.");
        var bitmap = BitmapSource.Create(captured.Width, captured.Height, 96, 96, PixelFormats.Bgra32, null, captured.Bgra, captured.Width * 4);
        bitmap.Freeze();
        var document = new AnnotationDocument(captured.Size, captured.ScaleFactor);
        var window = new AnnotationWindow(document, bitmap, settings, captureDate);
        try
        {
            var origins = windows.Select(w => PInvoke.GetWindowRect(new HWND(w.Handle), out var r) ? new PixelPoint(r.left, r.top) : (PixelPoint?)null)
                .OfType<PixelPoint>().ToArray();
            window.Place(target, origins);
            window.Closed += WindowClosed;
            windows.Add(window);
            window.Show(); window.Activate();
            var hwnd = new HWND(window.Handle);
            if (PInvoke.GetForegroundWindow() != hwnd) PInvoke.SetForegroundWindow(hwnd);
            if (PInvoke.GetForegroundWindow() != hwnd) { window.Topmost = true; window.Topmost = false; }
            window.FocusCanvas();
        }
        catch { windows.Remove(window); window.Closed -= WindowClosed; window.Close(); throw; }
    }
    private void WindowClosed(object? sender, EventArgs args)
    { if (sender is AnnotationWindow window) { window.Closed -= WindowClosed; windows.Remove(window); } }
    public void CommitAllTextEdits() { foreach (var window in windows.ToArray()) window.TextEditing.Commit(); }
    public void CloseAll() { foreach (var window in windows.ToArray()) window.Close(); }
}
