using Snapper.Core.App;
using Snapper.Core.Support;
namespace Snapper.Core.Settings;

public enum ShortcutRejection { None, MissingModifier, WindowsKey, Reserved }
public static class ShortcutValidation
{
    public static ShortcutRejection Validate(KeyChord chord)
    {
        if (!chord.IsValidBinding)
            return chord.Modifiers.HasFlag(KeyModifiers.Win) ? ShortcutRejection.WindowsKey : ShortcutRejection.MissingModifier;
        var m = chord.Modifiers;
        var k = chord.VirtualKey;
        var reserved = k == 0x7B ||
            (m.HasFlag(KeyModifiers.Alt) && k is 0x73 or 0x09 or 0x1B or 0x20) ||
            (m == KeyModifiers.Ctrl && k is 0x1B or 0x41 or 0x43 or 0x56 or 0x58 or 0x5A or 0x59 or 0x53 or 0x57 or 0x0D) ||
            (m == (KeyModifiers.Ctrl | KeyModifiers.Shift) && k == 0x5A);
        return reserved ? ShortcutRejection.Reserved : ShortcutRejection.None;
    }
    public static string? RejectionHint(ShortcutRejection rejection) => rejection switch
    {
        ShortcutRejection.None => null,
        ShortcutRejection.MissingModifier => "Use Ctrl or Alt with another key.",
        ShortcutRejection.WindowsKey => "Shortcuts with the Windows key aren't supported.",
        ShortcutRejection.Reserved => "This shortcut is reserved.",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection))
    };
    public static CapturePurpose? DuplicateOwner(KeyChord candidate, CapturePurpose recordingFor, IReadOnlyDictionary<CapturePurpose, KeyChord?> bindings)
    {
        foreach (var purpose in CapturePurposes.All)
            if (purpose != recordingFor && bindings[purpose] == candidate) return purpose;
        return null;
    }
    public static string DuplicateReason(CapturePurpose owner) => $"Already used by Snapper's “{owner}” shortcut.";
    public static bool IsModifierKey(int key) => key is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C or 0x14 or 0x90 or 0x91 or 0xE5 or 0xE7;
}
