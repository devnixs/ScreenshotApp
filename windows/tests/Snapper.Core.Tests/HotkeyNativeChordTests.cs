using Snapper.Core.App;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class HotkeyNativeChordTests
{
    [Theory]
    [InlineData(KeyModifiers.Ctrl|KeyModifiers.Shift,0x34,0x4006)]
    [InlineData(KeyModifiers.Ctrl|KeyModifiers.Alt,0x50,0x4003)]
    [InlineData(KeyModifiers.Alt,0x78,0x4001)]
    [InlineData(KeyModifiers.Ctrl|KeyModifiers.Win,0x41,0x400A)]
    [InlineData(KeyModifiers.None,0x41,0x4000)]
    [InlineData(KeyModifiers.Ctrl|KeyModifiers.Alt|KeyModifiers.Shift|KeyModifiers.Win,0xFE,0x400F)]
    public void MapsWinUserArgumentsWithNoRepeat(KeyModifiers modifiers,int key,uint expected)
    {var args=HotkeyNativeChord.ToRegisterHotKeyArgs(KeyChord.Create(modifiers,key));Assert.Equal(expected,args.Modifiers);Assert.Equal((uint)key,args.VirtualKey);Assert.NotEqual(0u,args.Modifiers&0x4000);}
}
