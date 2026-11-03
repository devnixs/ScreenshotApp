using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapper.Core.Geometry;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapper.Windows.Capture;

internal sealed class DisplayConfigurationChangedEventArgs(IReadOnlyList<DisplayInfo> old, IReadOnlyList<DisplayInfo> @new) : EventArgs
{
    public IReadOnlyList<DisplayInfo> Old { get; } = old;
    public IReadOnlyList<DisplayInfo> New { get; } = @new;
}

internal sealed class DisplayChangeWatcher : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly HwndSource source;
    private readonly DispatcherTimer followUp;
    private int pending;
    private bool disposed;

    public DisplayChangeWatcher(Dispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        this.dispatcher = dispatcher;
        Current = DisplayInfoProvider.GetCurrent();
        followUp = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        followUp.Tick += FollowUp;
        source = new HwndSource(new HwndSourceParameters("Snapper display watcher")
        {
            WindowStyle = unchecked((int)WINDOW_STYLE.WS_POPUP),
            ExtendedWindowStyle = (int)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE),
            Width = 0, Height = 0
        });
        source.AddHook(WindowMessage);
        SystemEvents.DisplaySettingsChanged += SystemDisplaySettingsChanged;
    }

    public IReadOnlyList<DisplayInfo> Current { get; private set; }
    public event EventHandler<DisplayConfigurationChangedEventArgs>? Changed;

    public void Recheck()
    {
        // SystemEvents can call from its own thread; one queued check serves the whole burst.
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref pending, 0);
            if (disposed) return;
            Check();
            if (disposed) return;
            followUp.Stop();
            followUp.Start();
        }));
    }

    private void Check()
    {
        var fresh = DisplayInfoProvider.GetCurrent();
        var previous = Current;
        var changed = DisplayConfigurationChange.HasChanged(previous, fresh) ||
            DisplayConfigurationChange.HasWorkAreaChanged(previous, fresh);
        Current = fresh;
        if (!changed) return;
        Debug.WriteLine($"Displays changed: {string.Join("; ", fresh.Select(d => $"{d.DeviceName} {d.Bounds} {d.Dpi} primary={d.IsPrimary}"))}");
        Changed?.Invoke(this, new DisplayConfigurationChangedEventArgs(previous, fresh));
    }

    private void FollowUp(object? sender, EventArgs args)
    {
        followUp.Stop();
        if (!disposed) Check();
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is (int)PInvoke.WM_DISPLAYCHANGE or (int)PInvoke.WM_DPICHANGED or (int)PInvoke.WM_SETTINGCHANGE)
            Recheck();
        return 0;
    }

    private void SystemDisplaySettingsChanged(object? sender, EventArgs args) => Recheck();

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        SystemEvents.DisplaySettingsChanged -= SystemDisplaySettingsChanged;
        followUp.Stop();
        followUp.Tick -= FollowUp;
        source.RemoveHook(WindowMessage);
        source.Dispose();
    }
}
