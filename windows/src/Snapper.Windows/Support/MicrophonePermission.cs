using System.Diagnostics;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Snapper.Windows.Interop;
using Snapper.Windows.Recording;
using Windows.Security.Authorization.AppCapabilityAccess;
using Windows.Win32.Media.Audio;
namespace Snapper.Windows.Support;
internal static class MicrophonePermission
{
    public static MicrophoneCapabilityStatus CapabilityStatus()
    {
        try
        {
            return AppCapability.Create("microphone").CheckAccess() switch
            {
                AppCapabilityAccessStatus.Allowed => MicrophoneCapabilityStatus.Allowed,
                AppCapabilityAccessStatus.DeniedByUser => MicrophoneCapabilityStatus.DeniedByUser,
                AppCapabilityAccessStatus.DeniedBySystem => MicrophoneCapabilityStatus.DeniedBySystem,
                AppCapabilityAccessStatus.UserPromptRequired => MicrophoneCapabilityStatus.UserPromptRequired,
                AppCapabilityAccessStatus.NotDeclaredByApp => MicrophoneCapabilityStatus.NotDeclaredByApp,
                _ => MicrophoneCapabilityStatus.Unavailable
            };
        }
        catch (Exception error) { Debug.WriteLine($"Snapper: Microphone capability unavailable: {error.Message}"); return MicrophoneCapabilityStatus.Unavailable; }
    }
    public static MicrophoneAuthorization Authorization => MicrophoneAccessMapping.FromCapability(CapabilityStatus());
    public static bool HasAudioInputDevice
    {
        get
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try { enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator(); enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out device); return device is not null; }
            catch (Exception error) { Debug.WriteLine($"Snapper: No microphone endpoint: {error.Message}"); return false; }
            finally { MediaFoundationRuntime.Release(device); MediaFoundationRuntime.Release(enumerator); }
        }
    }
    public static Task<MicrophoneOpenResult> ProbeAsync() => Task.Run(() =>
    {
        var result = MicrophoneSource.TryOpen(out var source);
        source?.Dispose();
        return result;
    });
}
