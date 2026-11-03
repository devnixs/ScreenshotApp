using Snapper.Core.App;
using Snapper.Core.Support;
namespace Snapper.Core.Settings;

public abstract record RecorderEffect
{
    public sealed record SuspendHotkeys : RecorderEffect;
    public sealed record ResumeHotkeys : RecorderEffect;
    public sealed record Commit(KeyChord? Chord) : RecorderEffect;
    public sealed record Beep : RecorderEffect;
    public sealed record ShowHint(string? Text) : RecorderEffect;
    public sealed record ShowDuplicateAlert(string Reason) : RecorderEffect;
    public sealed record ReleaseFocus : RecorderEffect;
    public sealed record PassKeyThrough : RecorderEffect;
}
public sealed class ShortcutRecorderSession(CapturePurpose purpose, Func<IReadOnlyDictionary<CapturePurpose, KeyChord?>> currentBindings)
{
    public bool IsRecording { get; private set; }
    public KeyModifiers HeldModifiers { get; private set; }
    public IReadOnlyList<RecorderEffect> FocusGained()
    {
        if (IsRecording) return [];
        IsRecording = true;
        return [new RecorderEffect.SuspendHotkeys(), new RecorderEffect.ShowHint(null)];
    }
    public IReadOnlyList<RecorderEffect> FocusLost()
    {
        if (!IsRecording) return [];
        IsRecording = false; HeldModifiers = KeyModifiers.None;
        return [new RecorderEffect.ShowHint(null), new RecorderEffect.ResumeHotkeys()];
    }
    public IReadOnlyList<RecorderEffect> KeyDown(int key, KeyModifiers modifiers)
    {
        if (!IsRecording) return [];
        HeldModifiers = modifiers;
        if (ShortcutValidation.IsModifierKey(key)) return [];
        if (key == 0x1B) return [new RecorderEffect.ReleaseFocus()];
        if (key == 0x09 && (modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
            return [new RecorderEffect.ReleaseFocus(), new RecorderEffect.PassKeyThrough()];
        if (key is 0x08 or 0x2E && modifiers == KeyModifiers.None)
            return [new RecorderEffect.Commit(null), new RecorderEffect.ReleaseFocus()];
        if (!KeyChord.TryCreate(modifiers, key, out var chord)) return [new RecorderEffect.Beep()];
        var rejection = ShortcutValidation.Validate(chord);
        if (rejection != ShortcutRejection.None)
            return [new RecorderEffect.Beep(), new RecorderEffect.ShowHint(ShortcutValidation.RejectionHint(rejection))];
        if (ShortcutValidation.DuplicateOwner(chord, purpose, currentBindings()) is { } owner)
            return [new RecorderEffect.ShowDuplicateAlert(ShortcutValidation.DuplicateReason(owner)), new RecorderEffect.ReleaseFocus()];
        return [new RecorderEffect.Commit(chord), new RecorderEffect.ReleaseFocus()];
    }
    public IReadOnlyList<RecorderEffect> KeyUp(int key, KeyModifiers modifiers)
    { if (IsRecording) HeldModifiers = modifiers; return []; }
    public IReadOnlyList<RecorderEffect> ClearClicked() => [new RecorderEffect.Commit(null)];
    public static string PreviewText(bool isRecording, KeyModifiers held, KeyChord? binding)
    {
        if (!isRecording) return binding?.DisplayString ?? "Record Shortcut";
        if (held == KeyModifiers.None) return "Press Shortcut";
        return string.Concat(KeyChord.ModifierNames.Where(x => held.HasFlag(x.Flag)).Select(x => x.Name + "+")) + "…";
    }
}
