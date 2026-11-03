using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Snapper.Core.Settings;
using Snapper.Windows.Support;
using Snapper.Windows.Capture;
using System.Windows.Threading;

namespace Snapper.Windows.App;

internal sealed class AppCoordinator : IDisposable
{
    private TrayIconController? tray;
    private Window? settingsWindow;

    public AppSettings Settings { get; } = new(
        new JsonSettingsStore(JsonSettingsStore.DefaultFilePath(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))), SaveFolder.DefaultPath);

    internal DisplayChangeWatcher Displays { get; }

    public AppCoordinator()
    {
        Settings.SaveFailed += SettingsSaveFailed;
        Displays = new DisplayChangeWatcher(Dispatcher.CurrentDispatcher);
    }
    private static void SettingsSaveFailed(object? sender, SettingsSaveFailedEventArgs args) => Debug.WriteLine($"Snapper: {args.Exception}");
    public void Start() => tray ??= new TrayIconController(this);
    public void StartScreenshot() => Debug.WriteLine("Snapper: StartScreenshot() not implemented yet");
    public void StartRecording() => Debug.WriteLine("Snapper: StartRecording() not implemented yet");
    public void StartAnnotate() => Debug.WriteLine("Snapper: StartAnnotate() not implemented yet");
    public void TrayClicked() => tray?.ShowMenu();

    public void OpenSettings(SettingsTab tab = SettingsTab.General)
    {
        if (settingsWindow is null)
        {
            settingsWindow = new Window
            {
                Title = "Snapper Settings", Width = 420, Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = true,
                Content = new TextBlock
                {
                    Text = "Settings will appear here.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        settingsWindow.Show();
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        Displays.Dispose();
        tray?.Dispose();
        tray = null;
    }
}
