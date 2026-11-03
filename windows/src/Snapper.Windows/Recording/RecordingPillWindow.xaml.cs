using System.Windows;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Capture;
namespace Snapper.Windows.Recording;

internal sealed partial class RecordingPillWindow : RecordingOverlayWindow
{
    public RecordingPillWindow(DisplayChangeWatcher watcher) : base(watcher, clickThrough: false) => InitializeComponent();
    public event Action? StopClicked, CancelClicked;
    public SizeD MeasureDips()
    {
        Pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new(Math.Ceiling(Pill.DesiredSize.Width), RecordingPillPlacement.PillHeightDips);
    }
    public void SetElapsedText(string text) => ElapsedLabel.Text = text;
    public void DisableButtons() { StopButton.IsEnabled = false; CancelButton.IsEnabled = false; }
    private void StopPressed(object sender, RoutedEventArgs args) => StopClicked?.Invoke();
    private void CancelPressed(object sender, RoutedEventArgs args) => CancelClicked?.Invoke();
}
