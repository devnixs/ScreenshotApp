using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;
using Snapper.Core.Settings;
namespace Snapper.Windows.Settings;
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public static LaunchAtLoginState Read(string? error = null)
    {
        if (Environment.ProcessPath is not { } exe) return new(false, LaunchAtLoginNote.Failed, "Couldn't find Snapper's location.");
        string? runValue = null;
        byte[]? approvalData = null;
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            runValue = run?.GetValue(LaunchAtLoginState.RunValueName) as string;
            using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKey);
            approvalData = approval?.GetValue(LaunchAtLoginState.RunValueName) as byte[];
            return LaunchAtLoginState.From(runValue, exe, approvalData, error);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        { return LaunchAtLoginState.From(runValue, exe, approvalData, error ?? e.Message); }
    }
    public static string? SetEnabled(bool on)
    {
        if (Environment.ProcessPath is not { } exe) return "Couldn't find Snapper's location.";
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on)
            {
                run.SetValue(LaunchAtLoginState.RunValueName, LaunchAtLoginState.RunValue(exe), RegistryValueKind.String);
                using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, writable: true);
                approval?.DeleteValue(LaunchAtLoginState.RunValueName, throwOnMissingValue: false);
            }
            else run.DeleteValue(LaunchAtLoginState.RunValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException) { return e.Message; }
    }
    public static void OpenStartupApps()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Exception e) { Debug.WriteLine($"Snapper: Couldn't open Startup apps: {e}"); }
    }
}
