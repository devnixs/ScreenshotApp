using System.Globalization;
namespace Snapper.Core.Support;
[Flags]
public enum KeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }
public readonly struct KeyChord : IEquatable<KeyChord>
{
    public KeyModifiers Modifiers { get; }
    public int VirtualKey { get; }
    private KeyChord(KeyModifiers modifiers, int virtualKey) { Modifiers = modifiers; VirtualKey = virtualKey; }
    public static bool TryCreate(KeyModifiers modifiers, int virtualKey, out KeyChord chord)
    {
        chord = default;
        if (virtualKey is < 1 or > 0xFE or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) return false;
        chord = new(modifiers & (KeyModifiers.Alt | KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Win), virtualKey);
        return true;
    }
    public static KeyChord Create(KeyModifiers modifiers, int virtualKey) => TryCreate(modifiers, virtualKey, out var chord) ? chord : throw new ArgumentException("A shortcut requires a non-modifier virtual key.", nameof(virtualKey));
    public bool IsValidBinding => VirtualKey is >= 1 and <= 0xFE && (Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0 && (Modifiers & KeyModifiers.Win) == 0;
    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            foreach (var (flag, name) in ModifierNames)
                if ((Modifiers & flag) != 0) parts.Add(name);
            parts.Add(KeyName(VirtualKey));
            return string.Join("+", parts);
        }
    }
    internal static readonly (KeyModifiers Flag, string Name)[] ModifierNames = [(KeyModifiers.Ctrl, "Ctrl"), (KeyModifiers.Alt, "Alt"), (KeyModifiers.Shift, "Shift"), (KeyModifiers.Win, "Win")];
    public static string KeyName(int key)
    {
        if (key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)key).ToString();
        if (key is >= 0x70 and <= 0x87) return "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture);
        if (key is >= 0x60 and <= 0x69) return "Num " + (key - 0x60).ToString(CultureInfo.InvariantCulture);
        return key switch
        {
            0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6E => "Num .", 0x6F => "Num /",
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x13 => "Pause", 0x14 => "Caps Lock", 0x1B => "Esc", 0x20 => "Space",
            0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x2C => "PrtScn", 0x2D => "Insert", 0x2E => "Delete",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => "Key 0x" + key.ToString("X2", CultureInfo.InvariantCulture)
        };
    }
    public bool Equals(KeyChord other) => Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
    public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
    public static bool operator ==(KeyChord left, KeyChord right) => left.Equals(right);
    public static bool operator !=(KeyChord left, KeyChord right) => !left.Equals(right);
}
