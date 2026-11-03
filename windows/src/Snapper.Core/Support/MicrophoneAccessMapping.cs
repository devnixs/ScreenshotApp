using Snapper.Core.Settings;
namespace Snapper.Core.Support;
public enum MicrophoneCapabilityStatus { Allowed, DeniedByUser, DeniedBySystem, UserPromptRequired, NotDeclaredByApp, Unavailable }
public enum MicrophoneOpenResult { Opened, AccessDenied, NoDevice, Failed }
public static class MicrophoneAccessMapping
{
    public const int AccessDenied = unchecked((int)0x80070005), NotFound = unchecked((int)0x80070490);
    // mferror.h, MF_E_NO_CAPTURE_DEVICES_AVAILABLE.
    public const int NoCaptureDevicesAvailable = unchecked((int)0xC00DABE0);
    public static MicrophoneAuthorization FromCapability(MicrophoneCapabilityStatus status) => status switch
    {
        MicrophoneCapabilityStatus.Allowed => MicrophoneAuthorization.Authorized,
        MicrophoneCapabilityStatus.DeniedByUser => MicrophoneAuthorization.Denied,
        MicrophoneCapabilityStatus.DeniedBySystem => MicrophoneAuthorization.Restricted,
        _ => MicrophoneAuthorization.NotDetermined
    };
    public static bool ProbeGrantsAccess(MicrophoneOpenResult result) => result != MicrophoneOpenResult.AccessDenied;
    public static MicrophoneOpenResult ClassifyHResult(int result) => result switch
    {
        AccessDenied => MicrophoneOpenResult.AccessDenied,
        NotFound or NoCaptureDevicesAvailable => MicrophoneOpenResult.NoDevice,
        _ => MicrophoneOpenResult.Failed
    };
}
