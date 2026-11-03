using System.Windows;
using System.Windows.Media;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Capture;
namespace Snapper.Windows.Recording;

internal sealed class RecordingBorderWindow : RecordingOverlayWindow
{
    private static readonly Pen Underlay = new(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), RecordingBorderGeometry.UnderlayWidthDips);
    private static readonly Pen Dashes = new(Brushes.White, RecordingBorderGeometry.DashWidthDips)
    {
        DashStyle = new DashStyle([RecordingBorderGeometry.DashLengthDips, RecordingBorderGeometry.DashGapDips], 0),
        DashCap = PenLineCap.Flat, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat
    };
    public RecordingBorderWindow(DisplayChangeWatcher watcher) : base(watcher, clickThrough: true)
    { IsHitTestVisible = false; SnapsToDevicePixels = true; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (PixelFrame is not { } frame) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var stroke = RecordingBorderGeometry.StrokeRect(new SizeD(DisplayGeometry.PixelsToDips(frame.Width, dpi.DpiScaleX),
            DisplayGeometry.PixelsToDips(frame.Height, dpi.DpiScaleY)));
        if (stroke.Width <= 0 || stroke.Height <= 0) return;
        var rect = new Rect(stroke.X, stroke.Y, stroke.Width, stroke.Height);
        dc.DrawRectangle(null, Underlay, rect);
        dc.DrawRectangle(null, Dashes, rect);
    }
}
