using Snapper.Core.App;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class ShortcutRecorderSessionTests
{
    private static KeyChord C(int k,KeyModifiers m=KeyModifiers.Ctrl|KeyModifiers.Shift)=>KeyChord.Create(m,k);
    private static Dictionary<CapturePurpose,KeyChord?> Defaults()=>new()
    {[CapturePurpose.Screenshot]=C(0x34),[CapturePurpose.Record]=C(0x35),[CapturePurpose.Annotate]=C(0x36)};
    private static ShortcutRecorderSession Session()=>new(CapturePurpose.Screenshot,Defaults);
    [Fact] public void FocusStartsAndStopsExactlyOnce()
    {
        var s=Session();Assert.Equal<RecorderEffect>([new RecorderEffect.SuspendHotkeys(),new RecorderEffect.ShowHint(null)],s.FocusGained());Assert.True(s.IsRecording);Assert.Empty(s.FocusGained());
        s.KeyDown(0x11,KeyModifiers.Ctrl);
        Assert.Equal<RecorderEffect>([new RecorderEffect.ShowHint(null),new RecorderEffect.ResumeHotkeys()],s.FocusLost());Assert.False(s.IsRecording);Assert.Equal(KeyModifiers.None,s.HeldModifiers);Assert.Empty(s.FocusLost());
    }
    [Fact] public void IdleKeysHaveNoEffects()
    {var s=Session();Assert.Empty(s.KeyDown(0x34,KeyModifiers.Ctrl));Assert.Empty(s.KeyUp(0x11,KeyModifiers.Ctrl));Assert.Equal(KeyModifiers.None,s.HeldModifiers);}
    [Fact] public void ModifierPreviewUsesDisplayOrder()
    {
        var s=Session();s.FocusGained();Assert.Empty(s.KeyDown(0x11,KeyModifiers.Ctrl));Assert.Equal(KeyModifiers.Ctrl,s.HeldModifiers);
        Assert.Equal("Ctrl+…",ShortcutRecorderSession.PreviewText(true,s.HeldModifiers,null));
        s.KeyDown(0x10,KeyModifiers.Ctrl|KeyModifiers.Shift);Assert.Equal("Ctrl+Shift+…",ShortcutRecorderSession.PreviewText(true,s.HeldModifiers,null));
        s.KeyUp(0x11,KeyModifiers.None);Assert.Equal("Press Shortcut",ShortcutRecorderSession.PreviewText(true,s.HeldModifiers,null));
        Assert.Equal("Ctrl+Alt+Shift+Win+…",ShortcutRecorderSession.PreviewText(true,KeyModifiers.Ctrl|KeyModifiers.Alt|KeyModifiers.Shift|KeyModifiers.Win,null));
    }
    [Theory] [InlineData(KeyModifiers.None)] [InlineData(KeyModifiers.Ctrl)]
    public void EscapeCancelsWithAnyModifiers(KeyModifiers m)
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.ReleaseFocus()],s.KeyDown(0x1B,m));Assert.True(s.IsRecording);}
    [Theory] [InlineData(KeyModifiers.None)] [InlineData(KeyModifiers.Shift)]
    public void TabPassesThrough(KeyModifiers m)
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.ReleaseFocus(),new RecorderEffect.PassKeyThrough()],s.KeyDown(0x09,m));}
    [Theory] [InlineData(0x08)] [InlineData(0x2E)]
    public void DeleteClearsBinding(int k)
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.Commit(null),new RecorderEffect.ReleaseFocus()],s.KeyDown(k,KeyModifiers.None));}
    [Fact] public void ModifiedBackspaceRecordsChord()
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.Commit(C(0x08,KeyModifiers.Ctrl)),new RecorderEffect.ReleaseFocus()],s.KeyDown(0x08,KeyModifiers.Ctrl));}
    [Theory] [InlineData(KeyModifiers.Shift,"Use Ctrl or Alt with another key.")] [InlineData(KeyModifiers.Win|KeyModifiers.Ctrl,"Shortcuts with the Windows key aren't supported.")]
    public void InvalidChordKeepsRecording(KeyModifiers m,string hint)
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.Beep(),new RecorderEffect.ShowHint(hint)],s.KeyDown(0x34,m));Assert.True(s.IsRecording);}
    [Fact] public void InvalidVirtualKeyBeeps()
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.Beep()],s.KeyDown(0xFF,KeyModifiers.Ctrl));}
    [Fact] public void DuplicateReportsOwnerWithoutCommit()
    {var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.ShowDuplicateAlert("Already used by Snapper's “Record” shortcut."),new RecorderEffect.ReleaseFocus()],s.KeyDown(0x35,KeyModifiers.Ctrl|KeyModifiers.Shift));}
    [Fact] public void OwnChordAndNewChordCommit()
    {
        var s=Session();s.FocusGained();Assert.Equal<RecorderEffect>([new RecorderEffect.Commit(C(0x34)),new RecorderEffect.ReleaseFocus()],s.KeyDown(0x34,KeyModifiers.Ctrl|KeyModifiers.Shift));
        Assert.Equal<RecorderEffect>([new RecorderEffect.Commit(C(0x53,KeyModifiers.Ctrl|KeyModifiers.Alt)),new RecorderEffect.ReleaseFocus()],s.KeyDown(0x53,KeyModifiers.Ctrl|KeyModifiers.Alt));
    }
    [Fact] public void BindingsAreReadAtKeyTime()
    {
        var bindings=Defaults();var s=new ShortcutRecorderSession(CapturePurpose.Screenshot,()=>bindings);s.FocusGained();
        bindings[CapturePurpose.Annotate]=C(0x53,KeyModifiers.Ctrl|KeyModifiers.Alt);
        Assert.Equal<RecorderEffect>([new RecorderEffect.ShowDuplicateAlert("Already used by Snapper's “Annotate” shortcut."),new RecorderEffect.ReleaseFocus()],s.KeyDown(0x53,KeyModifiers.Ctrl|KeyModifiers.Alt));
    }
    [Fact] public void SuspendResumePairingAcrossInvalidCancelCommitAndRepeatedLoss()
    {
        var s=Session();var effects=new List<RecorderEffect>();effects.AddRange(s.FocusGained());effects.AddRange(s.KeyDown(0x34,KeyModifiers.None));effects.AddRange(s.KeyDown(0x1B,KeyModifiers.None));effects.AddRange(s.FocusLost());
        effects.AddRange(s.FocusGained());effects.AddRange(s.KeyDown(0x34,KeyModifiers.Ctrl|KeyModifiers.Shift));effects.AddRange(s.FocusLost());effects.AddRange(s.FocusLost());
        Assert.Equal(new[]{typeof(RecorderEffect.SuspendHotkeys),typeof(RecorderEffect.ResumeHotkeys),typeof(RecorderEffect.SuspendHotkeys),typeof(RecorderEffect.ResumeHotkeys)},effects.Where(e=>e is RecorderEffect.SuspendHotkeys or RecorderEffect.ResumeHotkeys).Select(e=>e.GetType()));
    }
    [Fact] public void ClearWhileIdleNeverSuspends()
    {var s=Session();Assert.Equal<RecorderEffect>([new RecorderEffect.Commit(null)],s.ClearClicked());Assert.False(s.IsRecording);}
    [Fact] public void IdlePreviewShowsBindingOrPlaceholder()
    {Assert.Equal("Record Shortcut",ShortcutRecorderSession.PreviewText(false,KeyModifiers.None,null));Assert.Equal("Ctrl+Shift+4",ShortcutRecorderSession.PreviewText(false,KeyModifiers.None,C(0x34)));}
}
