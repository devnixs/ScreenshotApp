using Microsoft.Win32;
using System.Windows.Threading;
using Snapper.Core.App;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Snapper.Windows.App;

internal sealed class TrayIconController : IDisposable
{
    private readonly AppCoordinator coordinator;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Forms.NotifyIcon notifyIcon = new();
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Dictionary<TrayMenuEntry, Forms.ToolStripMenuItem> items = new();
    private Drawing.Icon? icon;
    private TrayIconState state = TrayIconState.Idle;
    private long? menuClosedAt;
    private bool disposed;
    private Action? balloonClick;

    public TrayIconController(AppCoordinator coordinator)
    {
        this.coordinator = coordinator;
        foreach (var entry in TrayMenuLayout.Entries)
        {
            if (entry == TrayMenuEntry.Separator) { menu.Items.Add(new Forms.ToolStripSeparator()); continue; }
            var item = new Forms.ToolStripMenuItem(TrayMenuLayout.Title(entry));
            item.Click += (_, _) => Choose(entry);
            items.Add(entry, item);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menuClosedAt = Environment.TickCount64;
        notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right)
                this.coordinator.Send(new AppModeEvent.TrayClicked(e.Button == Forms.MouseButtons.Left ? TrayButton.Left : TrayButton.Right));
        };
        notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var action = balloonClick;
            balloonClick = null;
            action?.Invoke();
        };
        notifyIcon.BalloonTipClosed += (_, _) => balloonClick = null;
        Refresh();
        notifyIcon.Visible = true;
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
    }

    public Forms.ToolStripMenuItem? Item(TrayMenuEntry entry) => items.GetValueOrDefault(entry);

    public void ShowMenu()
    {
        var sinceClosed = menuClosedAt is { } closed ? Environment.TickCount64 - closed : (long?)null;
        if (!TrayMenuClickGate.ShouldOpenMenu(menu.Visible, sinceClosed, Forms.SystemInformation.DoubleClickTime)) return;
        PInvoke.SetForegroundWindow(new HWND(menu.Handle));
        menu.Show(Forms.Cursor.Position, Forms.ToolStripDropDownDirection.AboveLeft);
    }

    public void CloseMenu()
    {
        if (menu.Visible) menu.Close();
    }

    public void UpdateShortcutTexts(IReadOnlyDictionary<CapturePurpose, KeyChord?> bindings)
    {
        foreach (var (purpose, entry) in new[]
        {
            (CapturePurpose.Screenshot, TrayMenuEntry.Screenshot),
            (CapturePurpose.Record, TrayMenuEntry.Record),
            (CapturePurpose.Annotate, TrayMenuEntry.Annotate)
        }) items[entry].ShortcutKeyDisplayString = bindings[purpose]?.DisplayString ?? string.Empty;
    }

    public void ShowWarningBalloon(string title, string body, Action onClick)
    {
        balloonClick = onClick;
        notifyIcon.ShowBalloonTip(10_000, title, body, Forms.ToolTipIcon.Warning);
    }

    public void SetState(TrayIconState state)
    {
        this.state = state;
        Refresh();
    }

    private void Choose(TrayMenuEntry entry)
    {
        switch (entry)
        {
            case TrayMenuEntry.Screenshot: coordinator.StartScreenshot(); break;
            case TrayMenuEntry.Record: coordinator.StartRecording(); break;
            case TrayMenuEntry.Annotate: coordinator.StartAnnotate(); break;
            case TrayMenuEntry.Settings: coordinator.OpenSettings(); break;
            case TrayMenuEntry.Exit: coordinator.Exit(); break;
        }
    }

    private void Refresh()
    {
        if (disposed) return;
        var appearance = TrayIconStyle.Resolve(state, TrayIconStyle.TaskbarIsLight(ReadSystemUsesLightTheme()));
        var next = TrayIconRenderer.Render(appearance, TrayIconStyle.IconPixelSize(PrimaryMonitorDpi()));
        var previous = icon;
        notifyIcon.Icon = next;
        icon = next;
        notifyIcon.Text = appearance.Tooltip;
        previous?.Dispose();
    }

    private static int? ReadSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") as int?;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }

    private static int PrimaryMonitorDpi()
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(0, 0), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        return PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var x, out _).Succeeded
            ? (int)x : 96;
    }

    private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
            dispatcher.BeginInvoke(Refresh);
    }
    private void DisplaySettingsChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(Refresh);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        balloonClick = null;
        SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        notifyIcon.Visible = false;
        notifyIcon.Icon = null;
        icon?.Dispose();
        menu.Dispose();
        notifyIcon.Dispose();
    }
}
