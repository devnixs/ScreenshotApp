using Snapper.Core.App;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class ShortcutValidationTests
{
    private static KeyChord C(KeyModifiers m, int k) => KeyChord.Create(m, k);
    private static Dictionary<CapturePurpose, KeyChord?> Defaults() => new()
    {
        [CapturePurpose.Screenshot] = C(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x34),
        [CapturePurpose.Record] = C(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x35),
        [CapturePurpose.Annotate] = C(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x36)
    };
    [Theory]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x34)]
    [InlineData(KeyModifiers.Alt, 0x34)]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x53)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift, 0x74)]
    [InlineData(KeyModifiers.Ctrl, 0x2E)]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x43)]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x5A)]
    public void ValidChords(KeyModifiers m, int k)
    { var chord = C(m,k); Assert.True(chord.IsValidBinding); Assert.Equal(ShortcutRejection.None, ShortcutValidation.Validate(chord)); }
    [Theory]
    [InlineData(KeyModifiers.Shift, ShortcutRejection.MissingModifier)]
    [InlineData(KeyModifiers.None, ShortcutRejection.MissingModifier)]
    [InlineData(KeyModifiers.Win | KeyModifiers.Ctrl, ShortcutRejection.WindowsKey)]
    [InlineData(KeyModifiers.Win, ShortcutRejection.WindowsKey)]
    public void InvalidBaseChordClassification(KeyModifiers m, ShortcutRejection expected)
    { var chord = C(m, 0x34); Assert.False(chord.IsValidBinding); Assert.Equal(expected, ShortcutValidation.Validate(chord)); }
    [Theory]
    [InlineData(KeyModifiers.Alt,0x73)] [InlineData(KeyModifiers.Ctrl,0x7B)] [InlineData(KeyModifiers.Alt|KeyModifiers.Shift,0x7B)]
    [InlineData(KeyModifiers.Alt,0x20)] [InlineData(KeyModifiers.Alt,0x09)] [InlineData(KeyModifiers.Alt,0x1B)]
    [InlineData(KeyModifiers.Ctrl,0x1B)] [InlineData(KeyModifiers.Ctrl,0x43)] [InlineData(KeyModifiers.Ctrl,0x56)]
    [InlineData(KeyModifiers.Ctrl,0x58)] [InlineData(KeyModifiers.Ctrl,0x41)] [InlineData(KeyModifiers.Ctrl,0x53)]
    [InlineData(KeyModifiers.Ctrl,0x57)] [InlineData(KeyModifiers.Ctrl,0x5A)] [InlineData(KeyModifiers.Ctrl,0x59)]
    [InlineData(KeyModifiers.Ctrl|KeyModifiers.Shift,0x5A)] [InlineData(KeyModifiers.Ctrl,0x0D)]
    public void ReservedChords(KeyModifiers m,int k) => Assert.Equal(ShortcutRejection.Reserved,ShortcutValidation.Validate(C(m,k)));
    [Fact] public void HintsMatchProduct()
    {
        Assert.Null(ShortcutValidation.RejectionHint(ShortcutRejection.None));
        Assert.Equal("Use Ctrl or Alt with another key.",ShortcutValidation.RejectionHint(ShortcutRejection.MissingModifier));
        Assert.Equal("Shortcuts with the Windows key aren't supported.",ShortcutValidation.RejectionHint(ShortcutRejection.WindowsKey));
        Assert.Equal("This shortcut is reserved.",ShortcutValidation.RejectionHint(ShortcutRejection.Reserved));
    }
    [Fact] public void DuplicatesIgnoreSelfAndClearedBindings()
    {
        var d=Defaults();
        Assert.Equal(CapturePurpose.Record,ShortcutValidation.DuplicateOwner(d[CapturePurpose.Record]!.Value,CapturePurpose.Screenshot,d));
        Assert.Null(ShortcutValidation.DuplicateOwner(d[CapturePurpose.Screenshot]!.Value,CapturePurpose.Screenshot,d));
        Assert.Null(ShortcutValidation.DuplicateOwner(C(KeyModifiers.Ctrl|KeyModifiers.Alt,0x39),CapturePurpose.Annotate,d));
        d[CapturePurpose.Record]=null;
        Assert.Null(ShortcutValidation.DuplicateOwner(C(KeyModifiers.Ctrl|KeyModifiers.Shift,0x35),CapturePurpose.Screenshot,d));
        d[CapturePurpose.Annotate]=d[CapturePurpose.Screenshot];d[CapturePurpose.Record]=d[CapturePurpose.Screenshot];
        Assert.Equal(CapturePurpose.Record,ShortcutValidation.DuplicateOwner(d[CapturePurpose.Screenshot]!.Value,CapturePurpose.Screenshot,d));
    }
    [Theory] [InlineData(CapturePurpose.Screenshot)] [InlineData(CapturePurpose.Record)] [InlineData(CapturePurpose.Annotate)]
    public void DuplicateReasonNamesOwner(CapturePurpose p) => Assert.Equal($"Already used by Snapper's “{p}” shortcut.",ShortcutValidation.DuplicateReason(p));
    [Theory]
    [InlineData(0x10)] [InlineData(0x11)] [InlineData(0x12)] [InlineData(0xA0)] [InlineData(0xA1)] [InlineData(0xA2)]
    [InlineData(0xA3)] [InlineData(0xA4)] [InlineData(0xA5)] [InlineData(0x5B)] [InlineData(0x5C)] [InlineData(0x14)]
    [InlineData(0x90)] [InlineData(0x91)] [InlineData(0xE5)] [InlineData(0xE7)]
    public void ModifierAndSpecialKeysIgnored(int k) => Assert.True(ShortcutValidation.IsModifierKey(k));
    [Theory] [InlineData(0x34)] [InlineData(0x41)] [InlineData(0x1B)] [InlineData(0x08)] [InlineData(0x2E)] [InlineData(0x70)]
    public void OrdinaryKeysNotModifiers(int k) => Assert.False(ShortcutValidation.IsModifierKey(k));
}
