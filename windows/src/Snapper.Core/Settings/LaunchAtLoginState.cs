namespace Snapper.Core.Settings;
public enum LaunchAtLoginNote { DisabledInStartupApps, Failed }
public sealed record LaunchAtLoginState(bool IsOn, LaunchAtLoginNote? Note, string? FailureMessage = null)
{
    public const string RunValueName = "Snapper";
    public static string RunValue(string exePath) => "\"" + exePath + "\"";
    public static string? CommandPath(string? runValue)
    {
        if (string.IsNullOrWhiteSpace(runValue)) return null;
        var value = runValue.Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end < 0 ? value[1..] : value[1..end];
        }
        var exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe < 0 ? value : value[..(exe + 4)];
    }
    public static bool PointsTo(string? runValue, string exePath) => string.Equals(CommandPath(runValue)?.Trim(), exePath.Trim(), StringComparison.OrdinalIgnoreCase);
    public static bool IsStartupApprovedDisabled(byte[]? data) => data is { Length: > 0 } && (data[0] & 1) == 1;
    public static LaunchAtLoginState From(string? runValue, string exePath, byte[]? startupApproved, string? error = null)
    {
        var on = PointsTo(runValue, exePath);
        return new(on, error is not null ? LaunchAtLoginNote.Failed : on && IsStartupApprovedDisabled(startupApproved) ? LaunchAtLoginNote.DisabledInStartupApps : null, error);
    }
    public string? NoteText => Note switch
    {
        LaunchAtLoginNote.DisabledInStartupApps => "Snapper is turned off in Startup apps.",
        LaunchAtLoginNote.Failed => $"Couldn't change launch at login: {FailureMessage}",
        _ => null
    };
}
