using Snapper.Core.App;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class HotkeyFailureMessageTests
{
    private static KeyChord C(int n)=>KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Shift,0x30+n);
    [Theory]
    [InlineData(1,"Ctrl+Shift+4 is used by another app. Change it in Settings.")]
    [InlineData(2,"Ctrl+Shift+4 and Ctrl+Shift+5 are used by other apps. Change them in Settings.")]
    [InlineData(3,"Ctrl+Shift+4, Ctrl+Shift+5, and Ctrl+Shift+6 are used by other apps. Change them in Settings.")]
    public void BodyMatchesProductWording(int count,string expected)=>Assert.Equal(expected,HotkeyFailureMessage.Body(Enumerable.Range(4,count).Select(C).ToArray()));
    [Fact] public void EmptyThrows()=>Assert.Throws<ArgumentException>(()=>HotkeyFailureMessage.Body([]));
    [Fact] public void TitlesMatchProductWording(){Assert.Equal("Shortcut unavailable",HotkeyFailureMessage.Title);Assert.Equal("Another app is using this shortcut.",HotkeyFailureMessage.SettingsWarning);}
}
