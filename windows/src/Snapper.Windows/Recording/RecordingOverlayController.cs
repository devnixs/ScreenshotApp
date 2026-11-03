using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using Snapper.Core.Geometry;
using Snapper.Core.Recording;
using Snapper.Windows.Capture;
namespace Snapper.Windows.Recording;

/// <summary>One recording's dispatcher-owned controls. Dismissal always precedes the single action callback.</summary>
internal sealed class RecordingOverlayController
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DisplayChangeWatcher watcher;
    private readonly bool ownsWatcher, followsWindow;
    private readonly long startedAtTimestamp;
    private readonly Action onStop, onCancel;
    private readonly DispatcherTimer timer;
    private DisplayInfo display;
    private PixelRect recordedRect;
    private PixelRect? pillFrame;
    private RecordingBorderWindow? border;
    private RecordingPillWindow? pill;
    private string elapsedText = "0:00";
    private bool dismissed, shown, borderVisible = true;

    public RecordingOverlayController(DisplayInfo display, PixelRect recordedRect, bool followsWindow,
        long startedAtTimestamp, Action onStop, Action onCancel, DisplayChangeWatcher? watcher = null)
    {
        this.display = display; this.recordedRect = recordedRect; this.followsWindow = followsWindow;
        this.startedAtTimestamp = startedAtTimestamp; this.onStop = onStop; this.onCancel = onCancel;
        ownsWatcher = watcher is null;
        this.watcher = watcher ?? new DisplayChangeWatcher(dispatcher);
        timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += Tick;
    }
    public void Show()
    {
        dispatcher.VerifyAccess();
        if (dismissed || shown) return;
        if (!RefreshDisplay()) return;
        shown = true;
        try
        {
            border = new RecordingBorderWindow(watcher);
            pill = new RecordingPillWindow(watcher);
            pill.StopClicked += StopClicked; pill.CancelClicked += CancelClicked;
            pill.SetElapsedText(elapsedText);
            new WindowInteropHelper(border).EnsureHandle();
            new WindowInteropHelper(pill).EnsureHandle();
            Update(recordedRect);
            if (borderVisible) border.Show();
            pill.Show();
            Update(recordedRect); // WPF can apply initial show/DPI sizing; restore our physical frames.
            watcher.Changed += DisplaysChanged;
            timer.Start();
        }
        catch { Dismiss(); throw; }
    }
    public void Update(PixelRect recordedRect, DisplayInfo? display = null)
    {
        dispatcher.VerifyAccess();
        if (dismissed) return;
        this.recordedRect = recordedRect;
        if (display is not null) this.display = display;
        if (!RefreshDisplay() || pill is null) return;
        border?.SetPixelFrame(RecordingBorderGeometry.WindowFrame(recordedRect, this.display.Bounds, this.display.Scale, followsWindow));
        pillFrame = !borderVisible && pillFrame is { } previous
            ? RecordingPillPlacement.ReachableFrame(previous, this.display.WorkArea)
            : RecordingPillPlacement.Place(pill.MeasureDips(), recordedRect, this.display).Frame;
        pill.SetPixelFrame(pillFrame.Value); // Set last: the interactive pill must be above the border.
    }
    public void SetBorderVisible(bool visible)
    {
        dispatcher.VerifyAccess();
        if (dismissed) return;
        borderVisible = visible;
        if (border is not null)
        {
            if (visible) border.Show(); else border.Hide();
        }
        Update(recordedRect);
    }
    private bool RefreshDisplay()
    {
        var current = DisplayInfoProvider.GetCurrent();
        var fresh = followsWindow ? DisplayGeometry.DisplayOverlapping(RectD.FromPixelRect(recordedRect), current) : null;
        fresh ??= current.FirstOrDefault(d => d.DeviceName == display.DeviceName);
        if (followsWindow) fresh ??= DisplayGeometry.PrimaryDisplay(current);
        if (fresh is null) return false;
        display = fresh;
        return true;
    }
    private void DisplaysChanged(object? sender, DisplayConfigurationChangedEventArgs args) => Update(recordedRect);
    private void Tick(object? sender, EventArgs args)
    {
        if (dismissed) return;
        var next = ElapsedTimeFormatter.Format(Stopwatch.GetElapsedTime(startedAtTimestamp));
        if (next == elapsedText) return;
        elapsedText = next;
        pill?.SetElapsedText(next);
        Update(recordedRect);
    }
    private void StopClicked() => Finish(cancel: false);
    private void CancelClicked() => Finish(cancel: true);
    private void Finish(bool cancel)
    {
        if (dismissed) return;
        pill?.DisableButtons();
        Dismiss();
        if (cancel) onCancel(); else onStop();
    }
    public void Dismiss()
    {
        dispatcher.VerifyAccess();
        if (dismissed) return;
        dismissed = true;
        timer.Stop(); timer.Tick -= Tick;
        watcher.Changed -= DisplaysChanged;
        if (pill is not null)
        {
            pill.DisableButtons();
            pill.StopClicked -= StopClicked; pill.CancelClicked -= CancelClicked;
            pill.Close(); pill = null;
        }
        border?.Close(); border = null;
        if (ownsWatcher) watcher.Dispose();
    }
}
