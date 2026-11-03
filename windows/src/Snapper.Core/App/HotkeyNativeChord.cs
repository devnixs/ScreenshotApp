using Snapper.Core.Support;

namespace Snapper.Core.App;

public static class HotkeyNativeChord
{
    // WinUser.h HOT_KEY_MODIFIERS flags. Core deliberately uses plain numbers.
    private const uint Alt = 0x0001, Control = 0x0002, Shift = 0x0004, Win = 0x0008, NoRepeat = 0x4000;
    public static (uint Modifiers, uint VirtualKey) ToRegisterHotKeyArgs(KeyChord chord)
    {
        uint modifiers = NoRepeat;
        if ((chord.Modifiers & KeyModifiers.Alt) != 0) modifiers |= Alt;
        if ((chord.Modifiers & KeyModifiers.Ctrl) != 0) modifiers |= Control;
        if ((chord.Modifiers & KeyModifiers.Shift) != 0) modifiers |= Shift;
        if ((chord.Modifiers & KeyModifiers.Win) != 0) modifiers |= Win;
        return (modifiers, (uint)chord.VirtualKey);
    }
}
