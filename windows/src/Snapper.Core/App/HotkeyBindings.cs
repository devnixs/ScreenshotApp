using Snapper.Core.Settings;
using Snapper.Core.Support;
using System.Collections.ObjectModel;

namespace Snapper.Core.App;

public static class HotkeyBindings
{
    public static IReadOnlyDictionary<CapturePurpose, KeyChord?> From(AppSettings settings) =>
        new ReadOnlyDictionary<CapturePurpose, KeyChord?>(new Dictionary<CapturePurpose, KeyChord?>
        {
            [CapturePurpose.Screenshot] = settings.ScreenshotShortcut,
            [CapturePurpose.Record] = settings.RecordShortcut,
            [CapturePurpose.Annotate] = settings.AnnotateShortcut
        });
}
