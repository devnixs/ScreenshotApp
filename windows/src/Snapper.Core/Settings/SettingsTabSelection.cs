namespace Snapper.Core.Settings;
public static class SettingsTabSelection
{
    public static SettingsTab Resolve(SettingsTab? requested, SettingsTab? current, IReadOnlyCollection<SettingsTab> available)
    {
        var tab = requested ?? current ?? SettingsTab.General;
        return available.Contains(tab) ? tab : SettingsTab.General;
    }
}
