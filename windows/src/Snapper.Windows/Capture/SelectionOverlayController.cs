using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using Snapper.Core.App;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using Snapper.Windows.Support;
using Windows.Win32;

namespace Snapper.Windows.Capture;

internal sealed class SelectionOverlayController(DisplayChangeWatcher watcher)
{
    public static readonly TimeSpan DismissalDelay = TimeSpan.FromMilliseconds(50);
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ForegroundRestorer foreground = new();
    private readonly List<SelectionOverlayWindow> windows = [];
    private IReadOnlyList<DisplayInfo> displays = [];
    private SelectionSession session;
    private CapturePurpose purpose;
    private TaskCompletionSource<SelectionResult?>? completion;
    private bool isDismissing;

    public Task<SelectionResult?> SelectRegionAsync(CapturePurpose purpose)
    {
        dispatcher.VerifyAccess();
        if (completion is not null)
        {
            Debug.Fail("Selection requested while already presenting");
            Trace.WriteLine("Snapper: Selection requested while already presenting");
            return Task.FromResult<SelectionResult?>(null);
        }
        var pending = new TaskCompletionSource<SelectionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = pending;
        this.purpose = purpose;
        foreground.Remember();
        try
        {
            displays = DisplayInfoProvider.GetCurrent();
            if (displays.Count == 0) { Cancel(); return pending.Task; }
            foreach (var display in displays)
            {
                var window = new SelectionOverlayWindow(display, this, watcher);
                windows.Add(window);
                window.Prepare();
                window.Show();
                window.Position();
                window.CheckPlacement();
            }
            PInvoke.GetCursorPos(out var cursor);
            var targetDisplay = DisplayGeometry.DisplayContaining(new(cursor.X, cursor.Y), displays);
            var target = windows.FirstOrDefault(w => w.Display.DeviceName == targetDisplay?.DeviceName) ?? windows[0];
            target.Activate();
            PInvoke.SetForegroundWindow(target.Handle);
            Keyboard.Focus(target);
            if (PInvoke.GetForegroundWindow() != target.Handle) Debug.WriteLine("Snapper: Overlay activation was refused.");
            watcher.Changed += DisplaysChanged;
        }
        catch
        {
            Cancel();
            throw;
        }
        return pending.Task;
    }
    public void MouseDown(PointD point)
    {
        if (completion is null || isDismissing) return;
        session.MouseDown(point, displays);
        Render();
    }
    public void MouseDragged(PointD point)
    {
        if (completion is null || isDismissing) return;
        session.MouseDragged(point);
        Render();
    }
    public void MouseUp(PointD point)
    {
        if (completion is null || isDismissing) return;
        switch (session.MouseUp(point))
        {
            case SelectionSessionOutcome.Rectangle(var result): _ = FinishAsync(result); break;
            case SelectionSessionOutcome.None: Render(); break;
            default: throw new UnreachableException();
        }
    }
    public void CaptureLost()
    {
        if (completion is null || isDismissing) return;
        session.Reset();
        Render();
    }
    private void Render()
    {
        var tracker = session.Tracker;
        foreach (var window in windows) window.SetFrame(SelectionOverlayRenderState.For(window.Display, tracker, window.MeasureLabel));
    }
    private void DisplaysChanged(object? sender, DisplayConfigurationChangedEventArgs args)
    {
        if (DisplayConfigurationChange.HasChanged(displays, args.New)) Cancel();
    }
    public void Cancel()
    {
        dispatcher.VerifyAccess();
        if (completion is null || isDismissing) return;
        Close(SelectionOutcome.Cancelled);
        Complete(null);
    }
    private async Task FinishAsync(SelectionResult result)
    {
        isDismissing = true;
        Close(SelectionOutcome.Selected);
        PInvoke.DwmFlush();
        await Task.Delay(DismissalDelay);
        Complete(result);
    }
    private void Close(SelectionOutcome outcome)
    {
        session.Reset();
        watcher.Changed -= DisplaysChanged;
        foreach (var window in windows)
        {
            CaptureExclusion.Unregister(window.Handle);
            window.CloseFromController();
        }
        windows.Clear();
        displays = [];
        if (SelectionFocusPolicy.RestoresPreviousWindow(purpose, outcome)) foreground.Restore();
        else foreground.Forget();
    }
    private void Complete(SelectionResult? result)
    {
        var pending = completion;
        completion = null;
        isDismissing = false;
        pending?.TrySetResult(result);
    }
}
