using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using Windows.UI.ViewManagement;
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
    private Guid? sessionToken;
    private WindowHitTester? hitTester;
    private WindowCandidate? hoveredWindow;
    private Color accent;

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
        session = new();
        hitTester = null; hoveredWindow = null;
        var token = Guid.NewGuid();
        sessionToken = token;
        accent = ReadAccent();
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
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (sessionToken != token || isDismissing) return;
                try
                {
                    var candidates = WindowCandidateProvider.Snapshot();
                    if (sessionToken != token || isDismissing) return;
                    hitTester = new(candidates, (uint)Environment.ProcessId, CaptureExclusion.RegisteredHandles);
                    if (PInvoke.GetCursorPos(out var at)) MouseMoved(new(at.X, at.Y));
                }
                catch (Exception error) { Debug.WriteLine($"Snapper: Window snapshot unavailable: {error.Message}"); }
            }));
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
        if (session.Mode == SelectionMode.Window) MouseMoved(point);
        else Render();
    }
    public void MouseUp(PointD point)
    {
        if (completion is null || isDismissing) return;
        switch (session.MouseUp(point))
        {
            case SelectionSessionOutcome.Rectangle(var result): _ = FinishAsync(result); break;
            case SelectionSessionOutcome.WindowClick(var at):
                if (WindowSelection.Resolve(hitTester, at, displays) is { } window) _ = FinishAsync(window);
                break;
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
    public void ToggleMode()
    {
        if (completion is null || isDismissing) return;
        session.ToggleMode();
        hoveredWindow = null;
        if (PInvoke.GetCursorPos(out var at)) MouseMoved(new(at.X, at.Y));
        Render();
    }
    public void MouseMoved(PointD point)
    {
        if (completion is null || isDismissing || session.Mode != SelectionMode.Window) return;
        var candidate = hitTester?.WindowAt(new((int)point.X, (int)point.Y));
        if (hoveredWindow?.Handle == candidate?.Handle) return;
        hoveredWindow = candidate;
        Render();
    }
    private static Color ReadAccent()
    {
        try
        {
            var c = new UISettings().GetColorValue(UIColorType.Accent);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { return Color.FromRgb(0, 120, 212); }
    }
    private void Render()
    {
        var tracker = session.Tracker;
        foreach (var window in windows)
        {
            window.SetMode(session.Mode, hoveredWindow?.Frame, accent);
            if (session.Mode == SelectionMode.Rectangle)
                window.SetFrame(SelectionOverlayRenderState.For(window.Display, tracker, window.MeasureLabel));
        }
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
        session = new();
        sessionToken = null; hitTester = null; hoveredWindow = null;
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
