namespace Snapper.Core.App;

public enum CapturePurpose { Screenshot, Record, Annotate }

public static class CapturePurposes
{
    public static IReadOnlyList<CapturePurpose> All { get; } = Array.AsReadOnly(new[]
        { CapturePurpose.Screenshot, CapturePurpose.Record, CapturePurpose.Annotate });
}
