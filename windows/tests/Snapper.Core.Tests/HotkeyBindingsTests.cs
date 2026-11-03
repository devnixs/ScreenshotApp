using Snapper.Core.App;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class HotkeyBindingsTests
{
    private static AppSettings Settings()=>new(new InMemorySettingsStore(),@"C:\Screenshots");
    [Fact] public void DefaultsMapToCanonicalPurposes()
    {
        var settings=Settings();var bindings=HotkeyBindings.From(settings);Assert.Equal(3,bindings.Count);
        Assert.Equal(settings.ScreenshotShortcut,bindings[CapturePurpose.Screenshot]);Assert.Equal(settings.RecordShortcut,bindings[CapturePurpose.Record]);Assert.Equal(settings.AnnotateShortcut,bindings[CapturePurpose.Annotate]);
    }
    [Fact] public void ChangedRecordOnlyAffectsRecordAndSnapshotsDoNotMutate()
    {
        var settings=Settings();var initial=HotkeyBindings.From(settings);settings.RecordShortcut=KeyChord.Create(KeyModifiers.Alt,0x78);var next=HotkeyBindings.From(settings);
        Assert.Equal(initial[CapturePurpose.Screenshot],next[CapturePurpose.Screenshot]);Assert.Equal(initial[CapturePurpose.Annotate],next[CapturePurpose.Annotate]);Assert.NotEqual(initial[CapturePurpose.Record],next[CapturePurpose.Record]);Assert.Equal(settings.RecordShortcut,next[CapturePurpose.Record]);
    }
    [Fact] public void UnboundShortcutStaysNull()
    {var settings=Settings();settings.AnnotateShortcut=null;Assert.Null(HotkeyBindings.From(settings)[CapturePurpose.Annotate]);}
}
