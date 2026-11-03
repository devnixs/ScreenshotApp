using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class KeyChordTests
{
    [Fact] public void MasksModifierNoiseAndUsesWin32Flags()
    {
        Assert.Equal(KeyChord.Create(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x34), KeyChord.Create((KeyModifiers)0x4006, 0x34));
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win, KeyChord.Create((KeyModifiers)int.MaxValue, 0x34).Modifiers);
        Assert.Equal(new[] {1,2,4,8}, new[] {(int)KeyModifiers.Alt,(int)KeyModifiers.Ctrl,(int)KeyModifiers.Shift,(int)KeyModifiers.Win});
    }
    [Theory]
    [InlineData(0)] [InlineData(255)] [InlineData(256)] [InlineData(-1)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(91)] [InlineData(92)]
    [InlineData(160)] [InlineData(161)] [InlineData(162)] [InlineData(163)] [InlineData(164)] [InlineData(165)]
    public void RejectsModifierAndOutOfRangeKeys(int key)
    {
        Assert.False(KeyChord.TryCreate(KeyModifiers.Ctrl,key,out _)); Assert.Throws<ArgumentException>(() => KeyChord.Create(KeyModifiers.Ctrl,key));
    }
    [Fact] public void DisplayDefaultsAndModifierOrder()
    {
        for (var i = 4; i <= 6; i++) Assert.Equal("Ctrl+Shift+" + i, KeyChord.Create(KeyModifiers.Ctrl | KeyModifiers.Shift,0x30+i).DisplayString);
        Assert.Equal("Ctrl+Alt+Shift+Win+A",KeyChord.Create((KeyModifiers)15,0x41).DisplayString);
        Assert.Equal("Alt+F1",KeyChord.Create(KeyModifiers.Alt,0x70).DisplayString);
        Assert.Equal("5",KeyChord.Create(KeyModifiers.None,0x35).DisplayString);
    }
    [Fact] public void KeyNamesCoverAllNamedKeysAndFallback()
    {
        for (var i=0;i<10;i++) { Assert.Equal(i.ToString(),KeyChord.KeyName(0x30+i)); Assert.Equal("Num "+i,KeyChord.KeyName(0x60+i)); }
        for (var i=0;i<26;i++) Assert.Equal(((char)('A'+i)).ToString(),KeyChord.KeyName(0x41+i));
        for (var i=1;i<=24;i++) Assert.Equal("F"+i,KeyChord.KeyName(0x6F+i));
        var expected = new Dictionary<int,string> { [0x6A]="Num *",[0x6B]="Num +",[0x6D]="Num -",[0x6E]="Num .",[0x6F]="Num /",[8]="Backspace",[9]="Tab",[13]="Enter",[19]="Pause",[20]="Caps Lock",[27]="Esc",[32]="Space",[33]="Page Up",[34]="Page Down",[35]="End",[36]="Home",[37]="Left",[38]="Up",[39]="Right",[40]="Down",[44]="PrtScn",[45]="Insert",[46]="Delete",[0xBA]=";",[0xBB]="=",[0xBC]=",",[0xBD]="-",[0xBE]=".",[0xBF]="/",[0xC0]="`",[0xDB]="[",[0xDC]="\\",[0xDD]="]",[0xDE]="'",[7]="Key 0x07",[0xE5]="Key 0xE5" };
        foreach (var (key,name) in expected) Assert.Equal(name,KeyChord.KeyName(key));
    }
    [Theory]
    [InlineData(KeyModifiers.Ctrl,true)] [InlineData(KeyModifiers.Alt,true)] [InlineData(KeyModifiers.Ctrl|KeyModifiers.Shift,true)]
    [InlineData(KeyModifiers.Ctrl|KeyModifiers.Alt|KeyModifiers.Shift,true)] [InlineData(KeyModifiers.None,false)] [InlineData(KeyModifiers.Shift,false)]
    [InlineData(KeyModifiers.Win,false)] [InlineData(KeyModifiers.Ctrl|KeyModifiers.Win,false)]
    public void BindingNeedsCtrlOrAltWithoutWin(KeyModifiers modifiers,bool valid) => Assert.Equal(valid,KeyChord.Create(modifiers,0x34).IsValidBinding);
    [Fact] public void EqualityAndHashAreValueBased()
    {
        var a=KeyChord.Create(KeyModifiers.Ctrl,0x34); var b=KeyChord.Create(KeyModifiers.Ctrl,0x34);
        Assert.True(a==b); Assert.False(a!=b); Assert.Equal(a.GetHashCode(),b.GetHashCode()); Assert.True(a.Equals((object)b));
        Assert.NotEqual(a,KeyChord.Create(KeyModifiers.Alt,0x34)); Assert.NotEqual(a,KeyChord.Create(KeyModifiers.Ctrl,0x35)); Assert.False(default(KeyChord).IsValidBinding);
    }
}
