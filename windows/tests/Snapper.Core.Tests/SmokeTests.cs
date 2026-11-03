using Snapper.Core.App;
using Snapper.Core.Settings;

namespace Snapper.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() => Assert.Equal("Snapper.Core", typeof(TrayMenuLayout).Assembly.GetName().Name);

    [Fact]
    public void CoreReferencesNoPlatformAssemblies()
    {
        string[] forbidden = ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms",
            "System.Drawing", "System.Drawing.Common", "Microsoft.Windows.SDK.NET", "Microsoft.Windows.CsWin32"];
        Assert.DoesNotContain(typeof(TrayMenuLayout).Assembly.GetReferencedAssemblies(), reference => forbidden.Contains(reference.Name));
    }

    [Fact]
    public void SettingsTabHasThreeTabsInOrder() => Assert.Equal(
        new[] { SettingsTab.General, SettingsTab.Shortcuts, SettingsTab.Recording }, Enum.GetValues<SettingsTab>());
}
