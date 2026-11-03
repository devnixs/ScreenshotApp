using System.Diagnostics;
using System.IO;
using Snapper.Core.Recording;
namespace Snapper.Windows.Recording;

internal static class RecordingRecovery
{
    /// <summary>Runs before any new recording can create a temp file. A failed entry never prevents startup.</summary>
    public static Task RecoverAsync(string folder) => Task.Run(() =>
    {
        var entries = new List<RecordingTempEntry>();
        try
        {
            var directory = RecordingTempFiles.Directory(Path.GetTempPath());
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                try { var info = new FileInfo(path); entries.Add(new(path, info.Length, info.CreationTimeUtc)); }
                catch (Exception error) { Debug.WriteLine($"Snapper: Inspecting recovered recording: {error.Message}"); }
            }
        }
        catch (Exception error) { Debug.WriteLine($"Snapper: Scanning recovered recordings: {error.Message}"); }
        foreach (var action in RecordingTempFiles.RecoveryPlan(entries, DateTimeOffset.Now))
        {
            try
            {
                switch (action)
                {
                    case RecordingRecoveryAction.SaveOriginal(var path, var start): RecordingOutputFiles.SaveOriginal(path, folder, start); break;
                    case RecordingRecoveryAction.Delete(var path): File.Delete(path); break;
                }
            }
            catch (Exception error) { Debug.WriteLine($"Snapper: Recovering recording: {error.Message}"); }
        }
    });
}
