using System.Windows;
using Snapper.Core.Settings;
using Snapper.Windows.App;
using Forms = System.Windows.Forms;

namespace Snapper.Windows;

public partial class SnapperApplication : Application
{
    private SingleInstance? instance;
    private AppCoordinator? coordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalPrimary();
            Shutdown(0);
            return;
        }
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        coordinator = new AppCoordinator();
        coordinator.Start();
        instance.Listen(() => Dispatcher.BeginInvoke(() => coordinator?.OpenSettings(SettingsTab.General)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        coordinator?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
