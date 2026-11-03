namespace Snapper.Core.Settings;
public interface ISettingsStore
{
    SettingsValues Load();
    void Save(SettingsValues values);
}
public sealed class SettingsSaveException : Exception
{
    public SettingsSaveException(Exception innerException) : base("Couldn't save settings.", innerException) { }
}
