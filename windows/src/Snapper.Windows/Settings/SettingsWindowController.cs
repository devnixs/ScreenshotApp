using System.Windows;
using System.Windows.Interop;
using Snapper.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
namespace Snapper.Windows.Settings;
internal sealed class SettingsWindowController(SettingsViewModel model) : IDisposable
{
    private SettingsWindow? window;
    public void Show(SettingsTab? tab)
    {
        window ??= new SettingsWindow(model);
        window.SelectedTab = SettingsTabSelection.Resolve(tab, window.SelectedTab, window.AvailableTabs);
        model.RefreshOnActivate();
        // Replace remembered content focus before restoring/showing can activate a recorder.
        window.FocusTabs();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show(); window.Activate(); window.FocusTabs();
        PInvoke.SetForegroundWindow(new HWND(new WindowInteropHelper(window).Handle));
    }
    public void AllowClose() { if (window is not null) window.AllowClose = true; }
    public void Dispose()
    {
        if (window is not null) { window.AllowClose = true; window.Close(); window = null; }
        model.Dispose();
    }
}
