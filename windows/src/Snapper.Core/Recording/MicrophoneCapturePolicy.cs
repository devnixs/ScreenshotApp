using Snapper.Core.Settings;
namespace Snapper.Core.Recording;
public readonly record struct MicrophoneCaptureInputs(bool RecordMicrophoneSetting, MicrophoneAuthorization Authorization, bool HasAudioInputDevice, RecordingFormat OutputFormat);
public static class MicrophoneCapturePolicy
{
    /// <summary>GIF never captures the microphone (PRD §7.2). Until 0046 the session passes Mp4, its only output format. NotDetermined attempts activation without prompting.</summary>
    public static bool ShouldAttemptMicrophone(MicrophoneCaptureInputs i) => i.RecordMicrophoneSetting && i.OutputFormat == RecordingFormat.Mp4 && i.HasAudioInputDevice && i.Authorization is MicrophoneAuthorization.Authorized or MicrophoneAuthorization.NotDetermined;
}
