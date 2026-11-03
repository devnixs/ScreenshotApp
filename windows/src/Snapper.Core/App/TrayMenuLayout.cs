namespace Snapper.Core.App;

public enum TrayMenuEntry { Screenshot, Record, Annotate, Separator, Settings, Exit }

public static class TrayMenuLayout
{
    public static IReadOnlyList<TrayMenuEntry> Entries { get; } = Array.AsReadOnly(new[]
    {
        TrayMenuEntry.Screenshot, TrayMenuEntry.Record, TrayMenuEntry.Annotate,
        TrayMenuEntry.Separator, TrayMenuEntry.Settings, TrayMenuEntry.Exit
    });

    public static string? Title(TrayMenuEntry entry) => entry switch
    {
        TrayMenuEntry.Screenshot => "Screenshot",
        TrayMenuEntry.Record => "Record",
        TrayMenuEntry.Annotate => "Annotate",
        TrayMenuEntry.Separator => null,
        TrayMenuEntry.Settings => "Settings…",
        TrayMenuEntry.Exit => "Exit Snapper",
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };
}
