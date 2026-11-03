using Snapper.Core.App;

namespace Snapper.Core.Tests;

public class TrayMenuLayoutTests
{
    [Fact]
    public void EntriesFollowProductOrder() => Assert.Equal(
        new[] { TrayMenuEntry.Screenshot, TrayMenuEntry.Record, TrayMenuEntry.Annotate, TrayMenuEntry.Separator, TrayMenuEntry.Settings, TrayMenuEntry.Exit },
        TrayMenuLayout.Entries);

    [Fact]
    public void TitlesMatchProductMenu() => Assert.Equal(
        new[] { "Screenshot", "Record", "Annotate", null, "Settings…", "Exit Snapper" },
        TrayMenuLayout.Entries.Select(TrayMenuLayout.Title));

    [Fact]
    public void SettingsTitleUsesRealEllipsis()
    {
        Assert.EndsWith("…", TrayMenuLayout.Title(TrayMenuEntry.Settings));
        Assert.DoesNotContain("...", TrayMenuLayout.Title(TrayMenuEntry.Settings));
    }

    [Fact]
    public void OnlySeparatorHasNoTitle()
    {
        foreach (var entry in TrayMenuLayout.Entries)
            Assert.Equal(entry == TrayMenuEntry.Separator, TrayMenuLayout.Title(entry) is null);
    }
}
