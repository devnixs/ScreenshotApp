using Snapper.Core.Support;

namespace Snapper.Core.App;

public static class HotkeyFailureMessage
{
    public const string Title = "Shortcut unavailable";
    public const string SettingsWarning = "Another app is using this shortcut.";
    public static string Body(IReadOnlyList<KeyChord> failed)
    {
        if (failed.Count == 0) throw new ArgumentException("At least one failed shortcut is required.", nameof(failed));
        var names = failed.Select(c => c.DisplayString).ToArray();
        if (names.Length == 1) return $"{names[0]} is used by another app. Change it in Settings.";
        var joined = names.Length == 2 ? string.Join(" and ", names)
            : string.Join(", ", names[..^1]) + ", and " + names[^1];
        return $"{joined} are used by other apps. Change them in Settings.";
    }
}
