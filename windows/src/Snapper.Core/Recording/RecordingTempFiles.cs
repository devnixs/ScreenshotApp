using Snapper.Core.Settings;
namespace Snapper.Core.Recording;

public sealed record RecordingTempEntry(string Path, long Length, DateTimeOffset CreationTime);
public abstract record RecordingRecoveryAction(string Path)
{
    public sealed record SaveOriginal(string Source, DateTimeOffset StartTime) : RecordingRecoveryAction(Source);
    public sealed record Delete(string Source) : RecordingRecoveryAction(Source);
}
public static class RecordingTempFiles
{
    public static string Directory(string tempRoot) => Path.Combine(tempRoot, "Snapper");
    public static (string Capture, string Output) For(string tempRoot, Guid id)
    {
        var directory = Directory(tempRoot);
        return (Path.Combine(directory, $"Snapper-{id:N}.mp4"), Path.Combine(directory, $"Snapper-{id:N}-out.mp4"));
    }
    public static string OutputFor(string tempRoot, Guid id, RecordingFormat format) =>
        Path.Combine(Directory(tempRoot), $"Snapper-{id:N}-out.{format.FileExtension()}");
    public static IReadOnlyList<RecordingRecoveryAction> RecoveryPlan(IEnumerable<RecordingTempEntry> entries, DateTimeOffset now)
    {
        var actions = new List<RecordingRecoveryAction>();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry.Path);
            if (!name.StartsWith("Snapper-", StringComparison.OrdinalIgnoreCase)) continue;
            var outputGif = name.EndsWith("-out.gif", StringComparison.OrdinalIgnoreCase);
            var output = outputGif || name.EndsWith("-out.mp4", StringComparison.OrdinalIgnoreCase);
            var suffix = outputGif ? "-out.gif" : output ? "-out.mp4" : ".mp4";
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(name.AsSpan(8, Math.Max(0, name.Length - 8 - suffix.Length)), "N", out _)) continue;
            actions.Add(output || entry.Length <= 0 ? new RecordingRecoveryAction.Delete(entry.Path)
                : new RecordingRecoveryAction.SaveOriginal(entry.Path, entry.CreationTime == DateTimeOffset.MinValue ? now : entry.CreationTime));
        }
        return actions;
    }
}
public enum ForcedRecordingEndAction { ExistingStopBehavior, CancelAndSaveOriginal }
public static class ForcedRecordingEndDecision
{
    public static ForcedRecordingEndAction Decide(bool isConverting) => isConverting
        ? ForcedRecordingEndAction.CancelAndSaveOriginal : ForcedRecordingEndAction.ExistingStopBehavior;
}
