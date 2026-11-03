using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class SettingsTabSelectionTests
{
    [Theory]
    [InlineData(SettingsTab.Shortcuts,SettingsTab.General,SettingsTab.Shortcuts)]
    [InlineData(null,SettingsTab.Shortcuts,SettingsTab.Shortcuts)]
    [InlineData(null,null,SettingsTab.General)]
    [InlineData(SettingsTab.Recording,SettingsTab.Shortcuts,SettingsTab.General)]
    public void ResolvesAvailableTabs(SettingsTab? request,SettingsTab? current,SettingsTab expected)=>Assert.Equal(expected,SettingsTabSelection.Resolve(request,current,new[]{SettingsTab.General,SettingsTab.Shortcuts}));
    [Fact] public void FutureRecordingTabCanBeSelected()=>Assert.Equal(SettingsTab.Recording,SettingsTabSelection.Resolve(SettingsTab.Recording,null,Enum.GetValues<SettingsTab>()));
}
