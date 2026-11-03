namespace Snapper.Core.Settings;
public enum MicrophoneAuthorization { NotDetermined, Authorized, Denied, Restricted }
public readonly record struct MicrophoneToggleOutcome(bool StoredValue, bool ShowsDeniedNote, bool ShouldRequestAccess);
public static class MicrophoneToggleDecision
{
    public static MicrophoneToggleOutcome UserToggled(bool requested, MicrophoneAuthorization status) => !requested ? new(false, false, false) : status switch
    {
        MicrophoneAuthorization.Authorized => AccessRequestFinished(true),
        MicrophoneAuthorization.NotDetermined => new(false, false, true),
        _ => AccessRequestFinished(false)
    };
    public static MicrophoneToggleOutcome AccessRequestFinished(bool granted) => new(granted, !granted, false);
    /// <summary>Unpackaged Windows apps normally remain NotDetermined; keep intent until device activation resolves access.</summary>
    public static MicrophoneToggleOutcome ReconcileOnAppear(bool stored, MicrophoneAuthorization status) =>
        !stored ? new(false, false, false) : status is MicrophoneAuthorization.Denied or MicrophoneAuthorization.Restricted ? AccessRequestFinished(false) : AccessRequestFinished(true);
}
public sealed class MicrophoneRequestState
{
    public bool WantsMicrophone { get; private set; }
    public bool IsPending { get; private set; }
    public void UserToggled(bool value) => WantsMicrophone = value;
    public bool BeginRequest() { if (IsPending) return false; IsPending = true; return true; }
    public bool FinishRequest() { IsPending = false; return WantsMicrophone; }
}
