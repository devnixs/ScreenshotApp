namespace Snapper.Core.Settings;
public static class RecordingSettingsPresentation
{
    public static IReadOnlyList<(Mp4Quality Value, string Title)> Mp4QualityOptions { get; } =
        Array.AsReadOnly(Enum.GetValues<Mp4Quality>().Select(q => (q, q.Title())).ToArray());
    public const string Mp4QualityCaption = "Medium and Low re-encode after you stop, for smaller files.";
}
